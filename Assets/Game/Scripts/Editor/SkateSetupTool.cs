using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using G = GeneratedAssets;

// Tools > BeerAndDragon > Setup Skateboard
//   Префаб скейта из Assets/Game/Assets/move, SkaterController на игроке,
//   доска рядом с игроком и мини-скейтпарк за спиной (кикеры, перила, фанбокс).
// Tools > BeerAndDragon > Place Level Pickups
//   Раскладывает сигареты, энергетики и патроны по уровню (на пол и паркурные платформы).
public static class SkateSetupTool
{
    const string SkateFbx = "Assets/Game/Assets/move/Skateboard asset/skateboard test.fbx";
    const string SkateTex = "Assets/Game/Assets/move/Skateboard asset/Cube Base Color.png";
    const float BoardLength = 1.1f;   // крупнее реального -- рыцарь двухметровый

    // ---------------- Pickups ----------------

    [MenuItem("Tools/BeerAndDragon/Place Level Pickups")]
    public static void PlaceLevelPickups()
    {
        if (!CheckScene()) return;
        Physics.SyncTransforms();

        GameObject old = GameObject.Find("LevelPickups");
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject group = new GameObject("LevelPickups");
        Undo.RegisterCreatedObjectUndo(group, "Place Pickups");

        // Точки подобраны под SampleScene: у старта, на паркурных платформах, на дальней площадке
        var spots = new (string prefab, float x, float z)[]
        {
            ("Pickup_Cigarettes", -6f, 6f),          // у старта
            ("Pickup_Cigarettes", -11.53f, 23.85f),  // на паркурной платформе Cube (9)
            ("Pickup_Cigarettes", -35f, 8f),
            ("Pickup_Monster", -13f, 6f),             // у старта
            ("Pickup_Monster", -22.18f, 22.7f),       // на верхней платформе Cube (11)
            ("Pickup_Monster", 16.6f, -3.4f),         // на Platform (1)
            ("Pickup_Ammo", -4f, 8.5f),
            ("Pickup_Ammo", -30f, 2f),
        };

        int placed = 0;
        foreach (var s in spots)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(G.PrefabsDir + "/" + s.prefab + ".prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Pickups] Нет префаба " + s.prefab + " -- сначала Tools > BeerAndDragon > Setup Player");
                continue;
            }
            if (!GroundAt(s.x, s.z, out Vector3 p)) continue;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            go.transform.position = p;
            placed++;
        }

        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log($"[Pickups] Разложено предметов: {placed} (объект LevelPickups). Сохрани сцену.");
    }

    // ---------------- Skateboard ----------------

    [MenuItem("Tools/BeerAndDragon/Setup Skateboard")]
    public static void SetupSkateboard()
    {
        if (!CheckScene()) return;
        KnightMovement player = Object.FindFirstObjectByType<KnightMovement>();
        Physics.SyncTransforms();

        G.EnsureFolder(G.PrefabsDir);
        GameObject boardPrefab = BuildSkateboardPrefab();
        if (boardPrefab == null) return;

        SkaterController skater = player.GetComponent<SkaterController>();
        if (skater == null) skater = Undo.AddComponent<SkaterController>(player.gameObject);
        else ResetToDefaults(skater); // новые скорости/управление из кода, а не старые сохранённые

        Vector3 fwd = Flat(player.orientation != null ? player.orientation.forward : player.transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        // Доска рядом с игроком (если в сцене ещё нет ни одной)
        if (Object.FindFirstObjectByType<Skateboard>() == null)
        {
            Vector3 at = player.transform.position + right * 1.6f + fwd * 1.2f;
            if (GroundAt(at.x, at.z, out Vector3 p))
            {
                GameObject b = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab);
                Undo.RegisterCreatedObjectUndo(b, "Place Skateboard");
                b.transform.SetPositionAndRotation(p + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
            }
        }

        BuildSkatePark(player.transform.position - fwd * 10f, -fwd);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[Skate] Готово: доска рядом с игроком (E -- встать), скейтпарк за спиной. Сохрани сцену.");
    }

    static GameObject BuildSkateboardPrefab()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SkateFbx);
        if (fbx == null)
        {
            Debug.LogError("[Skate] Не нашёл модель скейта: " + SkateFbx);
            return null;
        }

        GameObject root = new GameObject("Skateboard");
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);

        GameObject model = Object.Instantiate(fbx, visual);
        model.name = "Model";
        foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        // Скиннированный меш (арматура x100) даёт в редакторе кривые границы, а клипы из Блендера
        // двигают кости непредсказуемо -- доска проваливалась в пол. Поэтому делаем доску статичной:
        // тот же меш в позе покоя через обычный MeshFilter, размеры считаем по реальным вершинам.
        Material mat = G.Material("M_Skateboard", G.Lit, Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(SkateTex));
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            GameObject go = smr.gameObject;
            Mesh mesh = smr.sharedMesh;
            int subCount = mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
            Object.DestroyImmediate(smr);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material[] mats = new Material[subCount];
            for (int i = 0; i < subCount; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }
        foreach (MeshRenderer mr in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }
        foreach (Animator a in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

        // Длинная сторона доски -> +Z, длина BoardLength, колёса стоят на y = 0 (по вершинам, а не по bounds рендера)
        Bounds b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
        if (b.size.x > b.size.z) visual.localRotation = Quaternion.Euler(0f, 90f, 0f);
        b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
        float len = Mathf.Max(b.size.x, b.size.z);
        visual.localScale = Vector3.one * (BoardLength / Mathf.Max(0.0001f, len));
        b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));
        visual.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = G.BoundsOf(G.CollectVertices(model.transform, root.transform));

        float deckTop = b.max.y, deckHalf = b.extents.z;
        if (deckTop > 0.35f)
        {
            Debug.LogWarning($"[Skate] Подозрительно высокая доска ({deckTop:0.00} м) -- ограничиваю высоту деки до 0.2 м.");
            deckTop = 0.2f;
        }
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, deckTop * 0.5f, 0f);
        box.size = new Vector3(Mathf.Max(0.15f, b.size.x), Mathf.Max(0.06f, deckTop), Mathf.Max(0.3f, b.size.z));

        Skateboard board = root.AddComponent<Skateboard>();
        board.visual = visual;
        board.animator = null;      // трюки процедурные (см. SkaterController.TryFlip)
        board.deckHeight = deckTop;
        board.deckHalfLength = deckHalf;
        Debug.Log($"[Skate] Доска: длина {b.size.z:0.00} м, ширина {b.size.x:0.00} м, высота деки {deckTop:0.00} м.");

        return G.SavePrefab(root, "Skateboard");
    }

    static string Name(Object o) => o != null ? o.name : "-";

    static void ResetToDefaults<T>(T component) where T : Component
    {
        GameObject tmp = new GameObject("tmp") { hideFlags = HideFlags.HideAndDontSave };
        T defaults = tmp.AddComponent<T>();
        Undo.RecordObject(component, "Reset tuning");
        EditorUtility.CopySerialized(defaults, component);
        Object.DestroyImmediate(tmp);
        EditorUtility.SetDirty(component);
    }

    static Bounds RendererBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ---------------- Skate park ----------------

    static void BuildSkatePark(Vector3 origin, Vector3 forward)
    {
        GameObject old = GameObject.Find("SkatePark");
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject park = new GameObject("SkatePark");
        Undo.RegisterCreatedObjectUndo(park, "Build Skate Park");

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;

        Material wood = G.Material("M_RampWood", G.Lit, new Color(0.62f, 0.45f, 0.28f), null, 0f, 0.15f);
        Material concrete = G.Material("M_Concrete", G.Lit, new Color(0.55f, 0.55f, 0.58f), null, 0f, 0.1f);
        Material metal = G.Material("M_RailMetal", G.Lit, new Color(0.7f, 0.72f, 0.78f), null, 1f, 0.75f);

        Vector3 f = Flat(forward);
        Vector3 r = Vector3.Cross(Vector3.up, f);
        Vector3 At(float x, float z)
        {
            Vector3 p = origin + r * x + f * z;
            return GroundAt(p.x, p.z, out Vector3 g) ? g : new Vector3(p.x, origin.y - 1f, p.z);
        }

        // Кикер на въезде
        Ramp(park.transform, "Kicker", At(0f, 0f), f, 2.5f, 0.8f, 2.5f, wood);

        // Перила
        Rail(park.transform, At(-5f, 3f), At(-5f, 17f), 0.55f, metal, concrete);

        // Фанбокс с горками с двух сторон и краями для грайнда
        Vector3 boxBase = At(4f, 12f);
        const float boxW = 3f, boxH = 0.7f, boxL = 6f;
        GameObject fun = Box(park.transform, "Funbox", boxBase + Vector3.up * boxH * 0.5f, Quaternion.LookRotation(f), new Vector3(boxW, boxH, boxL), concrete);
        AddRail(fun, new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));
        AddRail(fun, new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f));
        Ramp(park.transform, "FunboxRampIn", boxBase - f * (boxL * 0.5f + 2.2f), f, 2.2f, boxH, boxW, wood);
        Ramp(park.transform, "FunboxRampOut", boxBase + f * (boxL * 0.5f + 2.2f), -f, 2.2f, boxH, boxW, wood);

        // Кикер обратно
        Ramp(park.transform, "KickerBack", At(0f, 26f), -f, 3f, 1f, 2.5f, wood);

        foreach (Transform t in park.GetComponentsInChildren<Transform>())
            t.gameObject.layer = groundLayer;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Quaternion rot, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(center, rot);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // Наклонная плоскость: нижний край по центру в lowEdge, поднимается вдоль dir
    static void Ramp(Transform parent, string name, Vector3 lowEdge, Vector3 dir, float length, float height, float width, Material mat)
    {
        const float thick = 0.25f;
        float angle = Mathf.Atan2(height, length) * Mathf.Rad2Deg;
        float hyp = Mathf.Sqrt(length * length + height * height);
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-angle, 0f, 0f);
        Vector3 topCenter = lowEdge + dir * (length * 0.5f) + Vector3.up * (height * 0.5f);
        Vector3 center = topCenter - rot * Vector3.up * (thick * 0.5f);
        Box(parent, name, center, rot, new Vector3(width, thick, hyp), mat);

        // Опора под высоким краем, чтобы рампа не висела в воздухе
        Vector3 supportCenter = lowEdge + dir * (length - 0.15f) + Vector3.up * (height * 0.5f - 0.05f);
        Box(parent, name + "_Support", supportCenter, Quaternion.LookRotation(dir), new Vector3(width, height - 0.1f, 0.3f), mat);
    }

    static void Rail(Transform parent, Vector3 a, Vector3 b, float height, Material metal, Material baseMat)
    {
        Vector3 dir = Flat(b - a);
        float len = Vector3.Distance(Flat2(a), Flat2(b));
        float y = Mathf.Max(a.y, b.y);
        Vector3 mid = new Vector3((a.x + b.x) * 0.5f, y, (a.z + b.z) * 0.5f);
        Quaternion rot = Quaternion.LookRotation(dir);

        GameObject rail = new GameObject("GrindRail");
        rail.transform.SetParent(parent, true);
        rail.transform.SetPositionAndRotation(mid, rot);

        GameObject bar = Box(rail.transform, "Bar", mid + Vector3.up * height, rot, new Vector3(0.08f, 0.08f, len), metal);
        AddRail(bar, new Vector3(0f, 0.5f, -0.5f), new Vector3(0f, 0.5f, 0.5f));

        int posts = Mathf.Max(2, Mathf.CeilToInt(len / 4f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float t = i / (float)(posts - 1);
            Vector3 p = mid + dir * ((t - 0.5f) * (len - 0.3f));
            GameObject post = Box(rail.transform, "Post", p + Vector3.up * (height * 0.5f), rot, new Vector3(0.06f, height, 0.06f), metal);
            Object.DestroyImmediate(post.GetComponent<Collider>()); // чтобы не цепляться за стойки
            Box(rail.transform, "Base", p + Vector3.up * 0.02f, rot, new Vector3(0.25f, 0.04f, 0.25f), baseMat);
        }
    }

    static void AddRail(GameObject go, Vector3 localStart, Vector3 localEnd)
    {
        GrindRail gr = go.AddComponent<GrindRail>();
        gr.localStart = localStart;
        gr.localEnd = localEnd;
    }

    // ---------------- Helpers ----------------

    static bool CheckScene()
    {
        if (Object.FindFirstObjectByType<KnightMovement>() != null) return true;
        Debug.LogWarning("[BeerAndDragon] В открытой сцене нет игрока (KnightMovement) -- открой SampleScene.");
        return false;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    static Vector2 Flat2(Vector3 v) => new Vector2(v.x, v.z);

    static bool GroundAt(float x, float z, out Vector3 point)
    {
        if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }
        point = new Vector3(x, 0f, z);
        Debug.LogWarning($"[BeerAndDragon] Под точкой ({x}, {z}) нет пола -- пропускаю.");
        return false;
    }
}

// Одноразовый автозапуск: если в Library лежит файл-метка, после компиляции выполняем
// расстановку на открытой сцене и удаляем метку. (Метку создал Claude по просьбе
// "размести на сцене", чтобы не заставлять жать пункты меню.)
[InitializeOnLoad]
static class PendingSceneSetup
{
    const string Marker = "Library/BeerAndDragon_PendingSetup.txt";

    static PendingSceneSetup()
    {
        if (File.Exists(Marker)) EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(Marker)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }
        if (Object.FindFirstObjectByType<KnightMovement>() == null) return; // не та сцена -- ждём следующей перекомпиляции

        string tasks = File.ReadAllText(Marker);
        File.Delete(Marker);

        if (tasks.Contains("pickups")) SkateSetupTool.PlaceLevelPickups();
        if (tasks.Contains("skate")) SkateSetupTool.SetupSkateboard();
        if (tasks.Contains("knight")) KnightSetupTool.SetupKnight();
        if (tasks.Contains("use")) ItemUseSetupTool.Setup();
        if (tasks.Contains("sound")) SkateSoundsSetupTool.Setup();
        Debug.Log("[BeerAndDragon] Автонастройка сцены выполнена. Нажми Ctrl+S, чтобы сохранить сцену.");
    }
}
