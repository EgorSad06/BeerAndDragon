using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using G = GeneratedAssets;

// Tools > BeerAndDragon > Setup Item Use Animations
//   Префабы "в руке" (сигарета, банка), материалы дыма и огонька,
//   у сигарет/энергетика -- анимация и задержка 1 сек, ItemUser на игроке.
public static class ItemUseSetupTool
{
    const string CigFbx = "Assets/Game/Assets/things/PSXCigarette Pack[FIXED]/CigarettePack/Cigarettes/CigaretteFull.fbx";
    const string CigTex = "Assets/Game/Assets/things/PSXCigarette Pack[FIXED]/Textures/CigaretteTexture.png";
    const float CigLength = 0.09f;

    [MenuItem("Tools/BeerAndDragon/Setup Item Use Animations")]
    public static void Setup()
    {
        KnightMovment player = Object.FindFirstObjectByType<KnightMovment>();
        if (player == null)
        {
            Debug.LogWarning("[ItemUse] В сцене нет игрока (KnightMovment).");
            return;
        }
        G.EnsureFolder(G.PrefabsDir);

        GameObject heldCig = BuildHeldCigarette();
        GameObject heldCan = BuildHeldCan();
        Material smoke = SmokeMaterial();
        Material ember = G.Material("M_Ember", G.Unlit, new Color(1f, 0.55f, 0.15f), null);

        SetItem(G.ItemsDir + "/Cigarettes.asset", ItemDefinition.UseStyle.Smoke, heldCig);
        SetItem(G.ItemsDir + "/MonsterEnergy.asset", ItemDefinition.UseStyle.Drink, heldCan);

        ItemUser user = player.GetComponent<ItemUser>();
        if (user == null) user = Undo.AddComponent<ItemUser>(player.gameObject);
        Undo.RecordObject(user, "Item Use");
        // Позы из кода (они могли поменяться) -- поверх старых сохранённых в сцене
        GameObject tmp = new GameObject("tmp") { hideFlags = HideFlags.HideAndDontSave };
        EditorUtility.CopySerialized(tmp.AddComponent<ItemUser>(), user);
        Object.DestroyImmediate(tmp);
        user.smokeMaterial = smoke;
        user.emberMaterial = ember;
        EditorUtility.SetDirty(user);

        Inventory inv = player.GetComponent<Inventory>();
        if (inv != null)
        {
            Undo.RecordObject(inv, "Item Use");
            inv.itemUser = user;
            EditorUtility.SetDirty(inv);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[ItemUse] Готово: сигареты и Монстр теперь с анимацией и срабатывают через 1 сек. Сохрани сцену.");
    }

    static void SetItem(string path, ItemDefinition.UseStyle style, GameObject held)
    {
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            Debug.LogWarning("[ItemUse] Нет " + path + " -- сначала Tools > BeerAndDragon > Setup Player");
            return;
        }
        item.useStyle = style;
        item.useTime = 1f;
        item.heldPrefab = held;
        EditorUtility.SetDirty(item);
    }

    // Сигарета: начало (у рта) в нуле, тянется вдоль +Z, на конце точка Tip для огонька
    static GameObject BuildHeldCigarette()
    {
        GameObject root = new GameObject("Held_Cigarette");
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CigFbx);
        if (fbx != null)
        {
            GameObject model = (GameObject)Object.Instantiate(fbx, root.transform);
            model.name = "Model";
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            Material m = G.Material("M_Cigarette", G.Lit, Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(CigTex));
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }

            // Длинная ось -> Z
            Bounds b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
            if (b.size.x >= b.size.y && b.size.x >= b.size.z) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            else if (b.size.y >= b.size.z) model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Фильтр (рыжий) -- к нулю, бумажный кончик -- в +Z
            if (WhiteEndIsMinZ(model.transform, root.transform))
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;

            b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
            model.transform.localScale *= CigLength / Mathf.Max(0.0001f, b.size.z);
            b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
            model.transform.localPosition -= new Vector3(b.center.x, b.center.y, b.min.z);
        }
        else
        {
            GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(stick.GetComponent<Collider>());
            stick.transform.SetParent(root.transform, false);
            stick.transform.localScale = new Vector3(0.008f, 0.008f, CigLength);
            stick.transform.localPosition = new Vector3(0f, 0f, CigLength * 0.5f);
        }

        GameObject tip = new GameObject("Tip");
        tip.transform.SetParent(root.transform, false);
        tip.transform.localPosition = new Vector3(0f, 0f, CigLength);
        return G.SavePrefab(root, "Held_Cigarette");
    }

    // Смотрим цвет текстуры у концов сигареты: у белого конца синий канал выше, чем у рыжего фильтра
    static bool WhiteEndIsMinZ(Transform model, Transform space)
    {
        if (!File.Exists(CigTex)) return false;
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(CigTex));

        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (Vector3 p in G.CollectVertices(model, space))
        {
            minZ = Mathf.Min(minZ, p.z);
            maxZ = Mathf.Max(maxZ, p.z);
        }
        float len = maxZ - minZ;
        if (len <= 0f) return false;

        float blueMin = 0f, blueMax = 0f;
        int nMin = 0, nMax = 0;
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length != v.Length) continue;
            Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++)
            {
                float z = m.MultiplyPoint3x4(v[i]).z;
                Color c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
                if (z < minZ + len * 0.1f) { blueMin += c.b; nMin++; }
                else if (z > maxZ - len * 0.1f) { blueMax += c.b; nMax++; }
            }
        }
        Object.DestroyImmediate(tex);
        if (nMin == 0 || nMax == 0) return false;
        return blueMin / nMin > blueMax / nMax;
    }

    // Банка: ноль -- в центре крышки (её подносим ко рту), сама банка висит вниз
    static GameObject BuildHeldCan()
    {
        GameObject root = new GameObject("Held_Monster");
        Material body = G.Material("M_MonsterCan", G.Lit, Color.white, G.CanTexture(), 0.6f, 0.6f);
        Material metal = G.Material("M_CanMetal", G.Lit, new Color(0.75f, 0.76f, 0.8f), null, 1f, 0.7f);
        GameObject model = G.MeshObject("Model", root.transform, G.CanMesh(), body, metal);
        model.transform.localPosition = new Vector3(0f, -0.164f, 0f);
        return G.SavePrefab(root, "Held_Monster");
    }

    static Material SmokeMaterial()
    {
        string texPath = G.Dir + "/T_Smoke.png";
        if (!File.Exists(texPath))
        {
            File.WriteAllBytes(texPath, ItemUser.MakeSoftCircle(64).EncodeToPNG());
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (ti != null)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }

        string matPath = G.Dir + "/M_Smoke.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            m = new Material(s);
            AssetDatabase.CreateAsset(m, matPath);
        }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        ItemUser.SetupTransparent(m);
        EditorUtility.SetDirty(m);
        return m;
    }
}
