using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using G = GeneratedAssets;

// Tools > BeerAndDragon > Apply New Art
//   - пиксельные полоски HP / энергетика / спец-шкалы и спидометр из Assets/Game/Assets/UI/Pixel UI pack 3;
//   - банка энергетика -- модель из PS1 Cans Asset Pack с этикеткой Red Bull (в руке, на земле, иконка);
//   - меч -- модель из Medieval Weapons (в руках и иконка);
//   - слой "Grind": на нём любой коллайдер становится перилами; перила скейтпарка переносятся на него;
//     материал искр для грайнда.
// Можно запускать повторно.
public static class ArtUpgradeTool
{
    const string UiDir = "Assets/Game/Assets/UI/Pixel UI pack 3";
    const string UiOut = G.Dir + "/UI";

    const string CansDir = "Assets/Game/Assets/PS1 Cans Asset Pack";
    const string CansTex = CansDir + "/Textures/PS1_SodaCans_BC.png";
    // Где в атласе этикетка Red Bull (UV, низ = 0)
    static readonly Rect RedBullUV = new Rect(0.49f, 0.265f, 0.35f, 0.11f);
    const float CanHeight = 0.164f;   // как у прежней банки: под неё настроены руки и рот

    const string SwordsDir = "Assets/Game/Assets/guns/Medieval Weapons/Medieval Weapons";
    const string SwordsFbx = SwordsDir + "/MedievalWeapons.fbx";
    const string SwordsTex = SwordsDir + "/Swords.png";
    const string SwordObject = "Sword_2";
    const float SwordLength = 1.03f;  // как у прежнего меча: гарда в нуле, клинок вверх по +Y

    [MenuItem("Tools/BeerAndDragon/Apply New Art")]
    public static void Apply()
    {
        G.EnsureFolder(G.Dir);
        G.EnsureFolder(G.IconsDir);
        G.EnsureFolder(UiOut);

        PixelUi();
        RedBullCan();
        MedievalSword();
        GrindSetup();

        AssetDatabase.SaveAssets();
        GameObject any = Object.FindFirstObjectByType<KnightMovement>()?.gameObject;
        if (any != null) EditorSceneManager.MarkSceneDirty(any.scene);
        Debug.Log("[Art] Готово: пиксельный HUD, банка Red Bull, средневековый меч, слой Grind. Сохрани сцену (Ctrl+S).");
    }

    // ================= Pixel UI =================

    public static void PixelUi()
    {
        string bars = UiDir + "/04.png";
        if (!File.Exists(bars))
        {
            Debug.LogWarning("[Art] Нет Pixel UI pack 3 -- HUD остаётся прежним");
            return;
        }

        GameHUD hud = Object.FindFirstObjectByType<GameHUD>();
        if (hud == null)
        {
            Debug.LogWarning("[Art] В сцене нет GameHUD -- сначала Tools > BeerAndDragon > Setup Player");
            return;
        }

        Undo.RecordObject(hud, "Pixel HUD");
        // Координаты -- в пикселях листа от левого верхнего угла
        hud.hpFrame = Crop(bars, new RectInt(0, 35, 48, 11), "UI_HpFrame");      // медная рамка
        hud.hpEmpty = Crop(bars, new RectInt(291, 6, 42, 5), "UI_HpEmpty");
        hud.hpFill = Crop(bars, new RectInt(51, 6, 42, 5), "UI_HpFill");          // красная
        hud.hpTrail = Crop(bars, new RectInt(51, 22, 42, 5), "UI_HpTrail");       // жёлтая -- "след" урона
        hud.smallFrame = Crop(bars, new RectInt(3, 99, 42, 11), "UI_SmallFrame");
        hud.smallEmpty = Crop(bars, new RectInt(295, 70, 34, 5), "UI_SmallEmpty");
        hud.boostFill = Crop(bars, new RectInt(55, 118, 34, 5), "UI_BoostFill");  // зелёная
        hud.specialFill = Crop(bars, new RectInt(55, 86, 34, 5), "UI_SpecialFill"); // жёлтая
        EditorUtility.SetDirty(hud);
    }

    // Точная копия куска листа (без сглаживания), спрайт с point-фильтром
    static Sprite Crop(string src, RectInt topLeft, string name)
    {
        string path = UiOut + "/" + name + ".png";
        Texture2D sheet = LoadReadable(src);
        Texture2D dst = new Texture2D(topLeft.width, topLeft.height, TextureFormat.RGBA32, false);
        int y0 = sheet.height - topLeft.y - topLeft.height;
        dst.SetPixels(sheet.GetPixels(topLeft.x, y0, topLeft.width, topLeft.height));
        dst.Apply();
        File.WriteAllBytes(path, dst.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Texture2D LoadReadable(string path)
    {
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    // ================= Red Bull =================

    public static void RedBullCan()
    {
        if (!File.Exists(CansTex))
        {
            Debug.LogWarning("[Art] Нет текстуры банок " + CansTex);
            return;
        }
        Mesh mesh = FindRedBullMesh(out float share);
        if (mesh == null)
        {
            Debug.LogWarning("[Art] Не нашёл банку с этикеткой Red Bull в " + CansDir);
            return;
        }
        Debug.Log($"[Art] Банка: {mesh.name} ({share:P0} развёртки на этикетке Red Bull)");

        // PS1-вид: без сглаживания текстуры
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(CansTex);
        if (ti != null && ti.filterMode != FilterMode.Point)
        {
            ti.filterMode = FilterMode.Point;
            ti.SaveAndReimport();
        }
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(CansTex);
        Material mat = G.Material("M_RedBullCan", G.Lit, Color.white, atlas, 0.4f, 0.55f);

        // Банка вертикально, высота как у прежней; ноль -- центр крышки (её подносим ко рту)
        Matrix4x4 upright = UprightMatrix(mesh);
        Bounds b = TransformedBounds(mesh, upright);
        float k = CanHeight / Mathf.Max(0.0001f, b.size.y);
        Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z) * k;
        Matrix4x4 held = Matrix4x4.TRS(-top, Quaternion.identity, Vector3.one) * Matrix4x4.Scale(Vector3.one * k) * upright;

        GameObject heldRoot = new GameObject("Held_Monster");
        MeshChild("Model", heldRoot.transform, mesh, mat, held);
        G.SavePrefab(heldRoot, "Held_Monster");   // тот же путь -- ссылки в предмете не теряются

        // Подбираемая банка: меняем только Visual внутри существующего префаба
        string pickupPath = G.PrefabsDir + "/Pickup_Monster.prefab";
        if (File.Exists(pickupPath))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(pickupPath);
            Transform visual = contents.transform.Find("Visual");
            if (visual != null)
            {
                for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);
                GameObject model = MeshChild("Model", visual, mesh, mat, upright);
                G.FitToSize(model.transform, 0.42f);
            }
            PrefabUtility.SaveAsPrefabAsset(contents, pickupPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        // Иконка: банка анфас, повёрнутая к нам этикеткой
        Texture2D readable = LoadReadable(CansTex);
        Matrix4x4 front = Matrix4x4.Rotate(Quaternion.Euler(8f, BestCanYaw(mesh, upright), 0f)) * upright;
        Sprite icon = SaveIcon(RenderIcon(mesh, front, readable, 128, true), "Icon_RedBull");
        SetIcon(G.ItemsDir + "/MonsterEnergy.asset", icon);
    }

    // Банка из пака, у которой больше всего развёртки попадает на этикетку Red Bull
    static Mesh FindRedBullMesh(out float bestShare)
    {
        Mesh best = null;
        bestShare = 0f;
        foreach (string file in Directory.GetFiles(CansDir + "/Assets", "Can*_SM.fbx"))
        {
            string path = file.Replace('\\', '/');
            foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
            {
                Vector2[] uv = m.uv;
                if (uv.Length == 0) continue;
                int inside = uv.Count(p => RedBullUV.Contains(p));
                float share = inside / (float)uv.Length;
                if (share > bestShare)
                {
                    bestShare = share;
                    best = m;
                }
            }
        }
        return best;
    }

    // Длинная ось банки -> +Y
    static Matrix4x4 UprightMatrix(Mesh mesh)
    {
        Vector3 s = mesh.bounds.size;
        Quaternion r = Quaternion.identity;
        if (s.x > s.y && s.x >= s.z) r = Quaternion.FromToRotation(Vector3.right, Vector3.up);
        else if (s.z > s.y && s.z > s.x) r = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
        return Matrix4x4.Rotate(r);
    }

    // Поворот вокруг вертикали, при котором к камере (+Z) обращено больше всего этикетки
    static float BestCanYaw(Mesh mesh, Matrix4x4 upright)
    {
        Vector3[] v = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int[] tris = mesh.triangles;
        float bestYaw = 0f, bestArea = -1f;
        for (int yaw = 0; yaw < 360; yaw += 15)
        {
            Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f)) * upright;
            float centerZ = TransformedBounds(mesh, m).center.z;
            float area = 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 c = (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f;
                if (!RedBullUV.Contains(c)) continue;
                Vector3 a = m.MultiplyPoint3x4(v[tris[i]]), b = m.MultiplyPoint3x4(v[tris[i + 1]]), d = m.MultiplyPoint3x4(v[tris[i + 2]]);
                if ((a.z + b.z + d.z) / 3f <= centerZ) continue;   // задняя половина банки не видна
                area += Mathf.Abs(Vector3.Cross(b - a, d - a).z); // площадь проекции на экран
            }
            if (area > bestArea)
            {
                bestArea = area;
                bestYaw = yaw;
            }
        }
        return bestYaw;
    }

    // ================= Меч =================

    public static void MedievalSword()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SwordsFbx);
        if (fbx == null)
        {
            Debug.LogWarning("[Art] Нет модели мечей " + SwordsFbx);
            return;
        }
        MeshFilter mf = fbx.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == SwordObject)
                        ?? fbx.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("[Art] В " + SwordsFbx + " нет мешей");
            return;
        }
        Mesh mesh = mf.sharedMesh;
        Matrix4x4 inFbx = fbx.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        Matrix4x4 place = SwordPlacement(mesh, inFbx) * inFbx;

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SwordsTex);
        Material mat = G.Material("M_MedievalSword", G.Lit, Color.white, tex, 0.5f, 0.55f);

        Sword sword = Object.FindFirstObjectByType<Sword>(FindObjectsInactive.Include);
        if (sword != null && sword.swingPivot != null)
        {
            Transform old = sword.swingPivot.Find("Model");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            GameObject model = MeshChild("Model", sword.swingPivot, mesh, mat, place);
            model.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            model.layer = sword.swingPivot.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(model, "Medieval Sword");
            Debug.Log($"[Art] Меч: {mf.name} из Medieval Weapons");
        }
        else Debug.LogWarning("[Art] В сцене нет меча (Sword) -- сначала Tools > BeerAndDragon > Setup Player");

        // Иконка: меч по диагонали, остриём вправо-вверх
        Matrix4x4 iconView = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -45f)) * place;
        Sprite icon = SaveIcon(RenderIcon(mesh, iconView, LoadReadable(SwordsTex), 128, false), "Icon_MedievalSword");
        SetIcon(G.ItemsDir + "/Sword.asset", icon);
    }

    // Клинок -> +Y, плоскость клинка -> XY, гарда в нуле, длина SwordLength
    static Matrix4x4 SwordPlacement(Mesh mesh, Matrix4x4 inFbx)
    {
        List<Vector3> pts = mesh.vertices.Select(v => inFbx.MultiplyPoint3x4(v)).ToList();
        Bounds b = G.BoundsOf(pts);
        int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
        int u = (axis + 1) % 3, w = (axis + 2) % 3;
        float min = b.min[axis], len = Mathf.Max(0.0001f, b.size[axis]);

        // Гарда -- самое широкое сечение вдоль длинной оси
        const int slices = 48;
        float[] wu = new float[slices], ww = new float[slices];
        float[] minU = Enumerable.Repeat(float.MaxValue, slices).ToArray(), maxU = Enumerable.Repeat(float.MinValue, slices).ToArray();
        float[] minW = Enumerable.Repeat(float.MaxValue, slices).ToArray(), maxW = Enumerable.Repeat(float.MinValue, slices).ToArray();
        foreach (Vector3 p in pts)
        {
            int s = Mathf.Clamp((int)((p[axis] - min) / len * slices), 0, slices - 1);
            minU[s] = Mathf.Min(minU[s], p[u]); maxU[s] = Mathf.Max(maxU[s], p[u]);
            minW[s] = Mathf.Min(minW[s], p[w]); maxW[s] = Mathf.Max(maxW[s], p[w]);
        }
        int guard = 0;
        float widest = -1f;
        for (int s = 0; s < slices; s++)
        {
            if (maxU[s] < minU[s]) continue;
            wu[s] = maxU[s] - minU[s];
            ww[s] = maxW[s] - minW[s];
            float width = Mathf.Max(wu[s], ww[s]);
            if (width > widest) { widest = width; guard = s; }
        }
        float guardPos = min + (guard + 0.5f) / slices * len;
        float dir = b.max[axis] - guardPos >= guardPos - min ? 1f : -1f;   // клинок длиннее рукояти

        // Какая из поперечных осей -- ширина клинка (считаем по сечениям клинка)
        float sumU = 0f, sumW = 0f;
        for (int s = 0; s < slices; s++)
        {
            float center = min + (s + 0.5f) / slices * len;
            if ((center - guardPos) * dir <= len * 0.1f) continue;
            sumU += wu[s];
            sumW += ww[s];
        }
        int widthAxis = sumU >= sumW ? u : w;

        Vector3 axisVec = Vector3.zero; axisVec[axis] = dir;
        Vector3 widthVec = Vector3.zero; widthVec[widthAxis] = 1f;
        Quaternion r = Quaternion.FromToRotation(axisVec, Vector3.up);
        Vector3 wv = Vector3.ProjectOnPlane(r * widthVec, Vector3.up);
        if (wv.sqrMagnitude > 0.0001f) r = Quaternion.AngleAxis(Vector3.SignedAngle(wv, Vector3.right, Vector3.up), Vector3.up) * r;

        float k = SwordLength / len;
        Vector3 guardPoint = b.center;
        guardPoint[axis] = guardPos;
        Vector3 offset = -(r * (guardPoint * k));
        return Matrix4x4.TRS(offset, r, Vector3.one * k);
    }

    // ================= Грайнд =================

    public static void GrindSetup()
    {
        int layer = EnsureLayer(GrindRail.LayerName);
        if (layer < 0)
        {
            Debug.LogWarning("[Art] Нет свободного слоя для " + GrindRail.LayerName);
            return;
        }

        // Перила скейтпарка -- на слой Grind (бруски, по которым не ходят)
        int moved = 0;
        GameObject park = GameObject.Find("SkatePark");
        if (park != null)
            foreach (Transform t in park.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bar")
                {
                    Undo.RecordObject(t.gameObject, "Grind layer");
                    t.gameObject.layer = layer;
                    moved++;
                }

        SkaterController skater = Object.FindFirstObjectByType<SkaterController>();
        if (skater != null)
        {
            Undo.RecordObject(skater, "Spark material");
            skater.sparkMaterial = SparkMaterial();
            EditorUtility.SetDirty(skater);
        }
        Debug.Log($"[Art] Слой {GrindRail.LayerName} = {layer}, перил на нём: {moved}. " +
                  "Поставь на этот слой любой объект с коллайдером -- по нему можно грайндить.");
    }

    public static int EnsureLayer(string name)
    {
        Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tagManager == null) return -1;
        SerializedObject so = new SerializedObject(tagManager);
        SerializedProperty layers = so.FindProperty("layers");
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = name;
            so.ApplyModifiedProperties();
            return i;
        }
        return -1;
    }

    static Material SparkMaterial()
    {
        string path = G.Dir + "/M_GrindSpark.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            m = new Material(s);
            AssetDatabase.CreateAsset(m, path);
        }
        Texture2D soft = AssetDatabase.LoadAssetAtPath<Texture2D>(G.Dir + "/T_Smoke.png");
        if (soft != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", soft);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", soft);
        }
        Color c = new Color(1f, 0.75f, 0.3f, 1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        ItemUser.SetupTransparent(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================= Общее =================

    static GameObject MeshChild(string name, Transform parent, Mesh mesh, Material mat, Matrix4x4 local)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local.GetColumn(3);
        go.transform.localRotation = local.rotation;
        go.transform.localScale = local.lossyScale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, mesh.subMeshCount)).ToArray();
        return go;
    }

    static Bounds TransformedBounds(Mesh mesh, Matrix4x4 m) =>
        G.BoundsOf(mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToList());

    static void SetIcon(string itemPath, Sprite icon)
    {
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
        if (item == null || icon == null) return;
        item.icon = icon;
        EditorUtility.SetDirty(item);
    }

    static Sprite SaveIcon(Texture2D tex, string name)
    {
        string path = G.IconsDir + "/" + name + ".png";
        G.SavePng(tex, path, true, true);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Маленький программный растеризатор: ортографическая проекция вдоль -Z, текстура по UV,
    // z-буфер, простая подсветка и чёрный контур. Нужен, чтобы иконка точно совпадала с моделью.
    static Texture2D RenderIcon(Mesh mesh, Matrix4x4 view, Texture2D tex, int size, bool shade)
    {
        Vector3[] v = mesh.vertices.Select(p => view.MultiplyPoint3x4(p)).ToArray();
        Vector2[] uv = mesh.uv;
        if (uv.Length != v.Length) uv = new Vector2[v.Length];

        Bounds b = G.BoundsOf(v.ToList());
        float scale = size * 0.86f / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.y));
        Vector2 Screen(Vector3 p) => new Vector2((p.x - b.center.x) * scale + size * 0.5f, (p.y - b.center.y) * scale + size * 0.5f);

        Color[] color = new Color[size * size];
        float[] depth = Enumerable.Repeat(float.MinValue, size * size).ToArray();
        Vector3 light = new Vector3(-0.4f, 0.6f, 0.7f).normalized;
        int[] tris = mesh.triangles;

        for (int i = 0; i < tris.Length; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            Vector2 a = Screen(v[i0]), c = Screen(v[i1]), d = Screen(v[i2]);
            float area = (c.x - a.x) * (d.y - a.y) - (c.y - a.y) * (d.x - a.x);
            if (Mathf.Abs(area) < 1e-6f) continue;

            Vector3 n = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).normalized;
            float lit = shade ? 0.65f + 0.35f * Mathf.Abs(Vector3.Dot(n, light)) : 0.8f + 0.2f * Mathf.Abs(Vector3.Dot(n, light));

            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, c.x, d.x)));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, c.x, d.x)));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, c.y, d.y)));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, c.y, d.y)));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = ((c.x - p.x) * (d.y - p.y) - (c.y - p.y) * (d.x - p.x)) / area;
                float w1 = ((d.x - p.x) * (a.y - p.y) - (d.y - p.y) * (a.x - p.x)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float z = v[i0].z * w0 + v[i1].z * w1 + v[i2].z * w2;
                int idx = y * size + x;
                if (z <= depth[idx]) continue;
                depth[idx] = z;

                Vector2 t = uv[i0] * w0 + uv[i1] * w1 + uv[i2] * w2;
                Color col = tex.GetPixel(Mathf.Clamp((int)(t.x * tex.width), 0, tex.width - 1),
                                         Mathf.Clamp((int)(t.y * tex.height), 0, tex.height - 1));
                col.r *= lit; col.g *= lit; col.b *= lit;
                col.a = 1f;
                color[idx] = col;
            }
        }

        // Контур в 1 пиксель
        Color[] outlined = (Color[])color.Clone();
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            if (color[y * size + x].a > 0f) continue;
            bool edge = false;
            for (int dy = -1; dy <= 1 && !edge; dy++)
            for (int dx = -1; dx <= 1 && !edge; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < size && ny < size && color[ny * size + nx].a > 0f) edge = true;
            }
            if (edge) outlined[y * size + x] = Color.black;
        }

        Texture2D result = new Texture2D(size, size, TextureFormat.RGBA32, false);
        result.SetPixels(outlined);
        result.Apply();
        return result;
    }
}
