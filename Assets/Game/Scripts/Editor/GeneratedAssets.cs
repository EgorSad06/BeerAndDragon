using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Генерация ассетов для WeaponSetupTool: материалы, иконки, меши (меч, банка, ящик патронов),
// префабы декалей/вспышки/подбираемых предметов. Всё складывается в Assets/Game/Assets/guns/Generated.
public static class GeneratedAssets
{
    public const string Dir = "Assets/Game/Assets/guns/Generated";
    public const string IconsDir = Dir + "/Icons";
    public const string PrefabsDir = Dir + "/Prefabs";
    public const string ItemsDir = Dir + "/Items";

    public const string Lit = "Universal Render Pipeline/Lit";
    public const string Unlit = "Universal Render Pipeline/Unlit";

    // ---------------- Folders / materials ----------------

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    public static Material Material(string name, string shaderName, Color color, Texture tex, float metallic = 0f, float smoothness = 0.1f)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = m == null;
        if (created)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (created) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    // Unlit с отсечением по альфе (PNG с прозрачностью), двусторонний
    public static Material CutoutMaterial(string name, Texture tex, Color color)
    {
        Material m = Material(name, Unlit, color, tex);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------- Textures / icons ----------------

    static void ConfigureTexture(string path, bool sprite, bool point)
    {
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) return;
        ti.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (sprite) ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = !sprite;
        ti.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.SaveAndReimport();
    }

    static void SavePng(Texture2D tex, string path, bool sprite, bool point)
    {
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        ConfigureTexture(path, sprite, point);
    }

    // Вырезает кусок картинки (координаты в пикселях, от ЛЕВОГО ВЕРХНЕГО угла) и сохраняет спрайтом
    public static Sprite IconFromImage(string srcPath, RectInt cropTopLeft, string outName, int maxSize = 256)
    {
        string outPath = IconsDir + "/" + outName + ".png";
        if (!File.Exists(outPath) && File.Exists(srcPath))
        {
            Texture2D src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(srcPath));

            RectInt r = cropTopLeft;
            r.x = Mathf.Clamp(r.x, 0, src.width - 1);
            r.width = Mathf.Clamp(r.width, 1, src.width - r.x);
            r.height = Mathf.Clamp(r.height, 1, src.height - Mathf.Clamp(r.y, 0, src.height - 1));
            int y = src.height - r.y - r.height;

            float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(r.width, r.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height * scale));
            Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                float u = (r.x + (px + 0.5f) / w * r.width) / src.width;
                float v = (y + (py + 0.5f) / h * r.height) / src.height;
                dst.SetPixel(px, py, src.GetPixelBilinear(u, v));
            }
            dst.Apply();
            SavePng(dst, outPath, true, false);
        }
        else if (File.Exists(outPath))
        {
            ConfigureTexture(outPath, true, false);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(outPath);
    }

    public static Sprite SwordIcon()
    {
        string path = IconsDir + "/Icon_Sword.png";
        if (!File.Exists(path))
        {
            PixelCanvas c = new PixelCanvas(64);
            Color steel = new Color(0.75f, 0.78f, 0.84f), light = new Color(0.95f, 0.97f, 1f);
            Color gold = new Color(0.9f, 0.65f, 0.2f), leather = new Color(0.42f, 0.22f, 0.1f);
            c.Segment(new Vector2(22, 22), new Vector2(57, 57), 3.2f, steel);
            c.Segment(new Vector2(24, 23), new Vector2(56, 55), 0.9f, light);
            c.Segment(new Vector2(12, 29), new Vector2(29, 12), 2.6f, gold);
            c.Segment(new Vector2(9, 9), new Vector2(20, 20), 2.4f, leather);
            c.Segment(new Vector2(6, 6), new Vector2(8, 8), 3.6f, gold);
            c.Outline(Color.black);
            SavePng(c.ToTexture(), path, true, true);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    public static Sprite CanIcon()
    {
        string path = IconsDir + "/Icon_Monster.png";
        if (!File.Exists(path))
        {
            PixelCanvas c = new PixelCanvas(64);
            Color body = new Color(0.07f, 0.07f, 0.08f), silver = new Color(0.75f, 0.76f, 0.8f);
            Color green = new Color(0.45f, 1f, 0.15f);
            c.Rect(20, 8, 24, 48, body);
            c.Rect(21, 55, 22, 3, silver);
            c.Rect(21, 6, 22, 3, silver);
            DrawClaws(c, 25, 16, 32, 5, green);
            c.Outline(Color.black);
            SavePng(c.ToTexture(), path, true, true);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Три рваные "когтистые" полосы
    static void DrawClaws(PixelCanvas c, int x0, int y0, int height, int spacing, Color color)
    {
        for (int i = 0; i < 3; i++)
            for (int y = 0; y < height; y++)
            {
                int x = x0 + i * spacing + ((y / 3) % 2 == 0 ? 0 : 1) + (i == 1 ? 0 : (y > height / 2 ? 1 : 0));
                c.Set(x, y0 + y, color);
                c.Set(x + 1, y0 + y, color);
            }
    }

    public static Texture2D CanTexture()
    {
        string path = Dir + "/T_MonsterCan.png";
        if (!File.Exists(path))
        {
            PixelCanvas c = new PixelCanvas(128, 128);
            Color body = new Color(0.06f, 0.06f, 0.07f), silver = new Color(0.72f, 0.73f, 0.78f);
            Color green = new Color(0.45f, 1f, 0.15f);
            c.Rect(0, 0, 128, 128, body);
            c.Rect(0, 0, 128, 8, silver);
            c.Rect(0, 120, 128, 8, silver);
            // Логотип с двух сторон банки
            DrawClaws(c, 18, 30, 70, 9, green);
            DrawClaws(c, 82, 30, 70, 9, green);
            SavePng(c.ToTexture(), path, false, true);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static Texture2D MuzzleFlashTexture()
    {
        string path = Dir + "/T_MuzzleFlash.png";
        if (!File.Exists(path))
        {
            PixelCanvas c = new PixelCanvas(64);
            Color outer = new Color(1f, 0.55f, 0.1f), inner = new Color(1f, 0.95f, 0.6f);
            Vector2 mid = new Vector2(32, 32);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f + 0.3f;
                float len = i % 2 == 0 ? 30f : 20f;
                c.Segment(mid, mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, 3f, outer);
            }
            c.Disc(mid, 11f, outer);
            c.Disc(mid, 7f, inner);
            SavePng(c.ToTexture(), path, false, true);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- Meshes ----------------

    public static Mesh SwordMesh()
    {
        string path = Dir + "/SwordMesh.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int STEEL = 0, GOLD = 1, LEATHER = 2;
        LowPolyBuilder mb = new LowPolyBuilder(3);

        Vector3[] Diamond(float y, float w, float t) => new[]
        {
            new Vector3(w, y, 0), new Vector3(0, y, t), new Vector3(-w, y, 0), new Vector3(0, y, -t)
        };
        Vector3[] b0 = Diamond(0.03f, 0.034f, 0.009f);
        Vector3[] b1 = Diamond(0.55f, 0.03f, 0.008f);
        Vector3[] b2 = Diamond(0.70f, 0.022f, 0.006f);
        Vector3 bladeC = new Vector3(0f, 0.4f, 0f);
        mb.Loft(STEEL, b0, b1, bladeC);
        mb.Loft(STEEL, b1, b2, bladeC);
        mb.Cone(STEEL, b2, new Vector3(0f, 0.8f, 0f), bladeC);
        mb.Cap(STEEL, b0, bladeC);

        mb.Box(GOLD, new Vector3(0f, 0.015f, 0f), new Vector3(0.2f, 0.03f, 0.035f));
        Vector3[] gl = { new Vector3(-0.1f, 0f, -0.0175f), new Vector3(-0.1f, 0.03f, -0.0175f), new Vector3(-0.1f, 0.03f, 0.0175f), new Vector3(-0.1f, 0f, 0.0175f) };
        mb.Cone(GOLD, gl, new Vector3(-0.14f, 0.03f, 0f), new Vector3(-0.09f, 0.015f, 0f));
        Vector3[] gr = { new Vector3(0.1f, 0f, -0.0175f), new Vector3(0.1f, 0.03f, -0.0175f), new Vector3(0.1f, 0.03f, 0.0175f), new Vector3(0.1f, 0f, 0.0175f) };
        mb.Cone(GOLD, gr, new Vector3(0.14f, 0.03f, 0f), new Vector3(0.09f, 0.015f, 0f));

        mb.Loft(LEATHER, LowPolyBuilder.Ring(8, -0.17f, 0.017f), LowPolyBuilder.Ring(8, 0f, 0.015f), new Vector3(0f, -0.085f, 0f));

        Vector3[] p0 = LowPolyBuilder.Ring(8, -0.195f, 0.032f);
        Vector3 pc = new Vector3(0f, -0.195f, 0f);
        mb.Cone(GOLD, p0, new Vector3(0f, -0.165f, 0f), pc);
        mb.Cone(GOLD, p0, new Vector3(0f, -0.235f, 0f), pc);

        return SaveMesh(mb.Build("SwordMesh"), path);
    }

    // Банка: боковина с UV развёрткой (submesh 0) + крышка и дно (submesh 1). Низ банки в y=0.
    public static Mesh CanMesh()
    {
        string path = Dir + "/CanMesh.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int sides = 12;
        const float r = 0.033f, h = 0.16f, rTop = 0.028f;
        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> side = new List<int>(), caps = new List<int>();

        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(dir * r); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 0f));
            v.Add(dir * r + Vector3.up * h); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 1f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            side.AddRange(new[] { a, b, d, a, d, c });
        }

        // Крышка (чуть уже -- как у настоящей банки) и дно
        void Cap(float y, float radius, Vector3 normal)
        {
            int center = v.Count;
            v.Add(new Vector3(0f, y, 0f)); n.Add(normal); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)); n.Add(normal); uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                if (normal.y > 0) caps.AddRange(new[] { center, center + i + 2, center + i + 1 });
                else caps.AddRange(new[] { center, center + i + 1, center + i + 2 });
            }
        }
        Cap(h + 0.004f, rTop, Vector3.up);
        Cap(0f, r * 0.9f, Vector3.down);
        // Скос от боковины к крышке
        int ring0 = v.Count;
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 nn = (dir + Vector3.up).normalized;
            v.Add(dir * r + Vector3.up * h); n.Add(nn); uv.Add(Vector2.one * 0.5f);
            v.Add(dir * rTop + Vector3.up * (h + 0.004f)); n.Add(nn); uv.Add(Vector2.one * 0.5f);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = ring0 + i * 2, b = a + 1, c = a + 2, d = a + 3;
            caps.AddRange(new[] { a, b, d, a, d, c });
        }

        Mesh m = new Mesh { name = "CanMesh" };
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetUVs(0, uv);
        m.subMeshCount = 2;
        m.SetTriangles(side, 0);
        m.SetTriangles(caps, 1);
        m.RecalculateBounds();
        return SaveMesh(m, path);
    }

    // Открытый деревянный ящик, из которого торчат патроны
    public static Mesh AmmoBoxMesh()
    {
        string path = Dir + "/AmmoBoxMesh.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int WOOD = 0, RED = 1, BRASS = 2;
        LowPolyBuilder mb = new LowPolyBuilder(3);
        const float w = 0.34f, d = 0.22f, h = 0.14f, t = 0.02f;
        mb.Box(WOOD, new Vector3(0f, t * 0.5f, 0f), new Vector3(w, t, d));                       // дно
        mb.Box(WOOD, new Vector3(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t));      // стенки
        mb.Box(WOOD, new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t));
        mb.Box(WOOD, new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - 2 * t));
        mb.Box(WOOD, new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - 2 * t));
        mb.Box(RED, new Vector3(0f, h * 0.55f, d * 0.5f + 0.001f), new Vector3(w * 0.6f, h * 0.35f, 0.004f)); // красная полоса-этикетка

        for (int row = 0; row < 2; row++)
        for (int col = 0; col < 5; col++)
        {
            float x = -0.12f + col * 0.06f;
            float z = -0.045f + row * 0.09f;
            float top = 0.17f + ((row + col) % 2) * 0.02f;
            Vector3 c = new Vector3(x, 0f, z);
            mb.Loft(BRASS, Shift(LowPolyBuilder.Ring(6, t, 0.019f), c), Shift(LowPolyBuilder.Ring(6, t + 0.04f, 0.019f), c), c + Vector3.up * (t + 0.02f));
            mb.Loft(RED, Shift(LowPolyBuilder.Ring(6, t + 0.04f, 0.017f), c), Shift(LowPolyBuilder.Ring(6, top, 0.017f), c), c + Vector3.up * ((t + top) * 0.5f));
            mb.Cap(RED, Shift(LowPolyBuilder.Ring(6, top, 0.017f), c), c + Vector3.up * (top - 0.02f));
        }

        return SaveMesh(mb.Build("AmmoBoxMesh"), path);
    }

    static Vector3[] Shift(Vector3[] ring, Vector3 by)
    {
        for (int i = 0; i < ring.Length; i++) ring[i] += by;
        return ring;
    }

    static Mesh SaveMesh(Mesh mesh, string path)
    {
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ---------------- Prefabs ----------------

    public static GameObject SavePrefab(GameObject temp, string name)
    {
        EnsureFolder(PrefabsDir);
        string path = PrefabsDir + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        return prefab;
    }

    public static GameObject BulletHolePrefab(Texture holeTex)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "BulletHole";
        Object.DestroyImmediate(q.GetComponent<Collider>()); // иначе дробь будет попадать в дырки
        q.transform.localScale = Vector3.one * 0.12f;
        MeshRenderer r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = CutoutMaterial("M_BulletHole", holeTex, Color.white);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return SavePrefab(q, "BulletHole");
    }

    public static GameObject MuzzleFlashPrefab()
    {
        GameObject root = new GameObject("MuzzleFlash");
        Material mat = CutoutMaterial("M_MuzzleFlash", MuzzleFlashTexture(), Color.white);
        for (int i = 0; i < 2; i++)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localScale = Vector3.one * (i == 0 ? 0.28f : 0.35f);
            q.transform.localRotation = i == 0 ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            q.transform.localPosition = new Vector3(0f, 0f, i == 0 ? 0f : 0.1f);
            MeshRenderer r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Light l = root.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.7f, 0.35f);
        l.range = 5f;
        l.intensity = 4f;
        root.AddComponent<RandomSpin>();
        return SavePrefab(root, "MuzzleFlash");
    }

    // Корень с триггером + дочерний Visual, который крутится
    public static GameObject PickupBase(string name, out Transform visual)
    {
        GameObject root = new GameObject(name);
        root.layer = 2; // Ignore Raycast -- чтобы Raycast-ы движения не цеплялись за подбираемые штуки
        SphereCollider sc = root.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.7f;
        sc.center = new Vector3(0f, 0.5f, 0f);
        visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = new Vector3(0f, 0.45f, 0f);
        return root;
    }

    public static GameObject MeshObject(string name, Transform parent, Mesh mesh, params Material[] mats)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        return go;
    }

    // Вписывает модель в размер size (по наибольшей стороне) и ставит центром в 0
    public static void FitToSize(Transform model, float size)
    {
        List<Vector3> pts = CollectVertices(model, model.parent);
        if (pts.Count == 0) return;
        Bounds b = BoundsOf(pts);
        float k = size / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        model.localScale *= k;
        b = BoundsOf(CollectVertices(model, model.parent));
        model.localPosition -= b.center;
    }

    public static List<Vector3> CollectVertices(Transform model, Transform space)
    {
        List<Vector3> pts = new List<Vector3>();
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in mf.sharedMesh.vertices)
                pts.Add(m.MultiplyPoint3x4(v));
        }
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            Matrix4x4 m = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            foreach (Vector3 v in smr.sharedMesh.vertices)
                pts.Add(m.MultiplyPoint3x4(v));
        }
        return pts;
    }

    public static Bounds BoundsOf(List<Vector3> pts)
    {
        if (pts.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds b = new Bounds(pts[0], Vector3.zero);
        foreach (Vector3 p in pts) b.Encapsulate(p);
        return b;
    }

    // ---------------- Helpers ----------------

    // Мини-"пейнт" для пиксельных иконок
    class PixelCanvas
    {
        readonly int w, h;
        readonly Color[] px;

        public PixelCanvas(int size) : this(size, size) { }
        public PixelCanvas(int width, int height)
        {
            w = width; h = height;
            px = new Color[w * h];
        }

        public void Set(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = c;
        }

        public void Rect(int x, int y, int rw, int rh, Color c)
        {
            for (int j = y; j < y + rh; j++)
                for (int i = x; i < x + rw; i++)
                    Set(i, j, c);
        }

        public void Disc(Vector2 center, float r, Color c)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) <= r) Set(x, y, c);
        }

        public void Segment(Vector2 a, Vector2 b, float r, Color c)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                    if (Vector2.Distance(p, a + ab * t) <= r) Set(x, y, c);
                }
        }

        // Чёрная обводка по краю непрозрачных пикселей
        public void Outline(Color c)
        {
            Color[] copy = (Color[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (copy[y * w + x].a > 0f) continue;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        for (int dx = -1; dx <= 1 && !edge; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            edge = copy[ny * w + nx].a > 0f && copy[ny * w + nx] != c;
                        }
                    if (edge) px[y * w + x] = c;
                }
        }

        public Texture2D ToTexture()
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }

    // Строит flat-shaded low-poly меш из выпуклых кусков. Нормаль каждого треугольника
    // разворачивается наружу от центра куска -- так не надо следить за порядком вершин.
    public class LowPolyBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] tris;

        public LowPolyBuilder(int submeshes)
        {
            tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) tris[i] = new List<int>();
        }

        public static Vector3[] Ring(int sides, float y, float r)
        {
            Vector3[] ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f;
                ring[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return ring;
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 center)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            Vector3 centroid = (a + b + c) / 3f;
            if (Vector3.Dot(n, centroid - center) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 center)
        {
            Tri(sub, a, b, c, center);
            Tri(sub, a, c, d, center);
        }

        public void Loft(int sub, Vector3[] r0, Vector3[] r1, Vector3 center)
        {
            for (int i = 0; i < r0.Length; i++)
            {
                int j = (i + 1) % r0.Length;
                Quad(sub, r0[i], r0[j], r1[j], r1[i], center);
            }
        }

        public void Cone(int sub, Vector3[] ring, Vector3 apex, Vector3 center)
        {
            for (int i = 0; i < ring.Length; i++)
                Tri(sub, ring[i], ring[(i + 1) % ring.Length], apex, center);
        }

        public void Cap(int sub, Vector3[] ring, Vector3 center)
        {
            Vector3 mid = Vector3.zero;
            foreach (Vector3 p in ring) mid += p;
            mid /= ring.Length;
            for (int i = 0; i < ring.Length; i++)
                Tri(sub, ring[i], ring[(i + 1) % ring.Length], mid, center);
        }

        public void Box(int sub, Vector3 c, Vector3 size)
        {
            Vector3 e = size * 0.5f;
            Vector3[] p =
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(e.x, e.y, -e.z),   c + new Vector3(-e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(e.x, e.y, e.z),    c + new Vector3(-e.x, e.y, e.z),
            };
            Quad(sub, p[0], p[1], p[2], p[3], c);
            Quad(sub, p[5], p[4], p[7], p[6], c);
            Quad(sub, p[4], p[0], p[3], p[7], c);
            Quad(sub, p[1], p[5], p[6], p[2], c);
            Quad(sub, p[3], p[2], p[6], p[7], c);
            Quad(sub, p[4], p[5], p[1], p[0], c);
        }

        public Mesh Build(string name)
        {
            Mesh m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}
