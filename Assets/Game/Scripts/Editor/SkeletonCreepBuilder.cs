using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Собирает готовых врагов-скелетов из моделей tools/blender/skeleton_psx.py, skeleton_archer.py, skeleton_shield.py:
//  - <модель>.controller: Idle <-> Walk по Speed; по триггеру Attack (из Idle и Walk) -- цепочка атаки
//    (крип и щитовик: Attack; лучник: Draw -> Shoot), потом Idle; Death по триггеру Die отовсюду;
//    у щитовика ещё Block (пока Blocking), BlockHit (удар по щиту в блоке), Parry (из Idle, Walk, Block);
//  - префаб: модель + Animator + SkeletonCreep + Damageable + CharacterController + SkeletonAI (своя роль);
//  - у лучника -- префаб летящей стрелы SkeletonArrow.prefab (модель Arrow.fbx) в SkeletonAI.arrowPrefab;
//  - у щитовика -- материалы раскрасок SKS_Paint1..8 (копии SKS_Paint1 со своей текстурой) в SkeletonCreep.shieldPaints.
// Запуск: меню Tools/BeerAndDragon/Build Skeleton Enemies (или -executeMethod SkeletonCreepBuilder.Build).
public static class SkeletonCreepBuilder
{
    private const string Root = "Assets/Game/Assets/enemies/";

    private struct Variant
    {
        public string Folder, Model, Prefab;
        public string[] AttackChain;
        public float Health;
        public bool Shield;
        public SkeletonAI.Role Role;
    }

    private static readonly Variant[] Variants =
    {
        new Variant { Folder = "Skeleton", Model = "Skeleton", Prefab = "SkeletonCreep",
                      AttackChain = new[] { "Attack" }, Health = 60f, Role = SkeletonAI.Role.Melee },
        new Variant { Folder = "SkeletonArcher", Model = "SkeletonArcher", Prefab = "SkeletonArcher",
                      AttackChain = new[] { "Draw", "Shoot" }, Health = 40f, Role = SkeletonAI.Role.Archer },
        new Variant { Folder = "SkeletonShield", Model = "SkeletonShield", Prefab = "SkeletonShield",
                      AttackChain = new[] { "Attack" }, Health = 80f, Shield = true, Role = SkeletonAI.Role.Shield },
    };

    [MenuItem("Tools/BeerAndDragon/Build Skeleton Enemies")]
    public static void Build()
    {
        foreach (Variant v in Variants) Build(v);
        AssetDatabase.SaveAssets();
    }

    private static void Build(Variant v)
    {
        string dir = Root + v.Folder;
        string modelPath = $"{dir}/{v.Model}.fbx";
        string prefabPath = $"{dir}/{v.Prefab}.prefab";
        AnimatorController controller = BuildController($"{dir}/{v.Model}.controller", modelPath, v.AttackChain, v.Shield);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = v.Prefab;

        Animator animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        CharacterController body = go.AddComponent<CharacterController>();   // и коллайдер для попаданий
        body.center = new Vector3(0f, 0.9f, 0f);
        body.height = 1.8f;
        body.radius = 0.3f;
        body.stepOffset = 0.35f;

        Damageable health = go.AddComponent<Damageable>();
        health.maxHealth = v.Health;
        health.destroyOnDeath = true;
        health.destroyDelay = 6f;   // тело лежит, потом исчезает

        SkeletonCreep creep = go.AddComponent<SkeletonCreep>();
        if (v.Shield) creep.shieldPaints = BuildShieldPaints(dir);

        SkeletonAI ai = go.AddComponent<SkeletonAI>();
        ai.role = v.Role;
        if (v.Role == SkeletonAI.Role.Shield) ai.walkSpeed = 1.0f;
        if (v.Role == SkeletonAI.Role.Archer) ai.arrowPrefab = BuildArrowPrefab(dir);

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[SkeletonCreep] {prefabPath}");
    }

    private static SkeletonArrow BuildArrowPrefab(string dir)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Arrow.fbx");
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "SkeletonArrow";
        go.AddComponent<SkeletonArrow>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/SkeletonArrow.prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<SkeletonArrow>();
    }

    // Расставить по скелету каждого вида перед игроком в открытой сцене (для проверки; сцену сохраняешь сам)
    [MenuItem("Tools/BeerAndDragon/Spawn Skeletons Near Player")]
    public static void SpawnNearPlayer()
    {
        PlayerHealth player = Object.FindFirstObjectByType<PlayerHealth>();
        Transform p = player != null ? player.transform : null;
        Vector3 origin = p != null ? p.position : Vector3.zero;
        Vector3 fwd = p != null ? Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        for (int i = 0; i < Variants.Length; i++)
        {
            Variant v = Variants[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}{v.Folder}/{v.Prefab}.prefab");
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = origin + fwd * (v.Role == SkeletonAI.Role.Archer ? 14f : 9f) + right * (i - 1) * 2.5f;
            go.transform.rotation = Quaternion.LookRotation(-fwd);
            Undo.RegisterCreatedObjectUndo(go, "Spawn Skeletons");
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    // SKS_Paint1 импортирован из FBX; остальные раскраски -- его копии с текстурами Textures/SKS_Paint<n>.png
    private static Material[] BuildShieldPaints(string dir)
    {
        Material first = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/SKS_Paint1.mat");
        var paints = new System.Collections.Generic.List<Material> { first };
        for (int i = 2; ; i++)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Textures/SKS_Paint{i}.png");
            if (tex == null) break;
            string path = $"{dir}/Materials/SKS_Paint{i}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(first);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(first);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            EditorUtility.SetDirty(m);
            paints.Add(m);
        }
        return paints.ToArray();
    }

    private static AnimatorController BuildController(string path, string modelPath, string[] attackChain, bool shield)
    {
        AssetDatabase.DeleteAsset(path);
        AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(a => !a.name.StartsWith("__preview__")).ToArray();
        AnimationClip Clip(string name) => clips.First(a => a.name == name);

        AnimatorStateMachine sm = c.layers[0].stateMachine;
        AnimatorState idle = sm.AddState("Idle", new Vector3(300, 0));
        AnimatorState walk = sm.AddState("Walk", new Vector3(300, 120));
        AnimatorState death = sm.AddState("Death", new Vector3(550, 260));
        idle.motion = Clip("Idle");
        walk.motion = Clip("Walk");
        death.motion = Clip("Death");
        sm.defaultState = idle;

        AnimatorStateTransition t = idle.AddTransition(walk);
        Instant(t, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        t = walk.AddTransition(idle);
        Instant(t, 0.15f).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // цепочка атаки: первый клип по триггеру, следующие -- когда доиграл предыдущий, в конце -- Idle
        AnimatorState prev = null;
        for (int i = 0; i < attackChain.Length; i++)
        {
            AnimatorState s = sm.AddState(attackChain[i], new Vector3(550 + 200 * i, 60));
            s.motion = Clip(attackChain[i]);
            if (prev == null)
            {
                foreach (AnimatorState from in new[] { idle, walk })
                    Instant(from.AddTransition(s), 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Attack");
            }
            else
            {
                t = prev.AddTransition(s);
                t.hasExitTime = true;
                t.exitTime = 1f;
                t.duration = 0f;
            }
            prev = s;
        }
        t = prev.AddTransition(idle);
        t.hasExitTime = true;
        t.exitTime = 0.95f;
        t.duration = 0.1f;

        if (shield) AddShieldStates(c, sm, idle, walk, Clip);

        t = sm.AddAnyStateTransition(death);
        t.canTransitionToSelf = false;
        Instant(t, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Die");
        return c;
    }

    private static void AddShieldStates(AnimatorController c, AnimatorStateMachine sm, AnimatorState idle,
                                        AnimatorState walk, System.Func<string, AnimationClip> clip)
    {
        c.AddParameter("Blocking", AnimatorControllerParameterType.Bool);
        c.AddParameter("Parry", AnimatorControllerParameterType.Trigger);
        c.AddParameter("BlockHit", AnimatorControllerParameterType.Trigger);
        AnimatorState block = sm.AddState("Block", new Vector3(50, 60));
        AnimatorState hit = sm.AddState("BlockHit", new Vector3(-200, 60));
        AnimatorState parry = sm.AddState("Parry", new Vector3(50, 220));
        block.motion = clip("Block");
        hit.motion = clip("BlockHit");
        parry.motion = clip("Parry");

        foreach (AnimatorState from in new[] { idle, walk })
            Instant(from.AddTransition(block), 0.12f).AddCondition(AnimatorConditionMode.If, 0, "Blocking");
        Instant(block.AddTransition(idle), 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Blocking");
        Instant(block.AddTransition(hit), 0.03f).AddCondition(AnimatorConditionMode.If, 0, "BlockHit");
        AnimatorStateTransition t = hit.AddTransition(block);
        t.hasExitTime = true;
        t.exitTime = 0.9f;
        t.duration = 0.1f;
        foreach (AnimatorState from in new[] { idle, walk, block })
            Instant(from.AddTransition(parry), 0.05f).AddCondition(AnimatorConditionMode.If, 0, "Parry");
        t = parry.AddTransition(idle);
        t.hasExitTime = true;
        t.exitTime = 0.95f;
        t.duration = 0.1f;
    }

    private static AnimatorStateTransition Instant(AnimatorStateTransition t, float blend)
    {
        t.hasExitTime = false;
        t.duration = blend;
        return t;
    }
}
