using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Собирает готовых врагов-скелетов из моделей tools/blender/skeleton_psx.py, skeleton_archer.py, skeleton_shield.py
// и анимаций Mixamo (Animations/Mixamo, см. MixamoClipImporter):
//  - <модель>.controller: Move -- смешивание по скорости вперёд (Speed) и вбок (Strafe): стойка, ходьба, бег,
//    шаги вбок; по триггеру Attack -- атака номер AttackIndex (у лучника цепочка: достать стрелу, натянуть,
//    выстрелить; состояния с тегом Attack); Hit -- вздрогнуть; Fidget -- почесаться; Die -- смерть DeathIndex;
//    у щитовика ещё Block (пока Blocking), BlockHit (удар по щиту), Parry (свой клип из SkeletonShield.fbx);
//  - префаб: модель + Animator + SkeletonCreep + Damageable + CharacterController + SkeletonDeath (смерть --
//    рассыпаться на кости или рэгдолл) + SkeletonSounds (хруст костей, свист удара, скрип и щелчок тетивы -- Sounds/Skeleton)
//    + SkeletonAI (роль, скорости
//    шагов из анимаций); лучнику -- BowRig (тетива, стрела) и SkeletonArrow.prefab; щитовику -- раскраски щита;
//  - лук и щит поворачиваются в руке так, чтобы в позах Mixamo лук стоял вертикально тетивой к правой руке
//    (прицел), а щит -- лицом вперёд (стойка, ходьба, блок -- в среднем); кости-метки BowTop/BowString,
//    ShieldTop/ShieldFront.
// Запуск: меню Tools/BeerAndDragon/Build Skeleton Enemies (или -executeMethod SkeletonCreepBuilder.Build).
public static class SkeletonCreepBuilder
{
    private const string Root = "Assets/Game/Assets/enemies/";
    private const string Sounds = "Assets/Game/Assets/Sounds/Skeleton/";

    private class Variant
    {
        public string Folder, Model, Prefab;
        public float Health;
        public SkeletonAI.Role Role;
        public (string clip, float strafe, float forward)[] Move;   // скорости -- шаг в анимации, м/с
        public float Walk, Run, RunDistance, Strafe;
        public string[] Attacks;            // цепочка клипов -- через +
        public string Hit, Fidget;
        public string[] Deaths;
        public bool Shield;
    }

    private static readonly Variant[] Variants =
    {
        new Variant
        {
            Folder = "Skeleton", Model = "Skeleton", Prefab = "SkeletonCreep", Health = 60f, Role = SkeletonAI.Role.Melee,
            Move = new[] { ("creep_idle", 0f, 0f), ("creep_walk", 0f, 0.45f), ("creep_run", 0f, 3.1f) },
            Walk = 0.45f, Run = 3.1f, RunDistance = 4f,
            Attacks = new[] { "creep_attack1", "creep_attack2", "creep_attack3" },
            Hit = "creep_hit", Fidget = "creep_idle2", Deaths = new[] { "creep_death1", "creep_death2" },
        },
        new Variant
        {
            Folder = "SkeletonArcher", Model = "SkeletonArcher", Prefab = "SkeletonArcher", Health = 40f,
            Role = SkeletonAI.Role.Archer,
            Move = new[] { ("archer_idle", 0f, 0f), ("archer_walk", 0f, 1.02f),
                           ("archer_strafe_left", -1.35f, 0f), ("archer_strafe_right", 1.39f, 0f) },
            Walk = 1.02f, Strafe = 1.35f,
            Attacks = new[] { "archer_draw+archer_aim+archer_shoot" },
            Hit = "archer_hit", Deaths = new[] { "archer_death" },
        },
        new Variant
        {
            Folder = "SkeletonShield", Model = "SkeletonShield", Prefab = "SkeletonShield", Health = 80f,
            Role = SkeletonAI.Role.Shield, Shield = true,
            Move = new[] { ("shield_idle", 0f, 0f), ("shield_walk", 0f, 1.5f), ("shield_run", 0f, 4.1f),
                           ("shield_strafe_left", -1.09f, 0f), ("shield_strafe_right", 1.09f, 0f) },
            Walk = 1.5f, Run = 4.1f, RunDistance = 9f, Strafe = 1.09f,
            Attacks = new[] { "shield_attack1", "shield_attack2" },
            Hit = "shield_hit", Deaths = new[] { "shield_death" },
        },
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
        var clips = new ClipFinder(modelPath);
        AnimatorController controller = BuildController($"{dir}/{v.Model}.controller", v, clips);
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
        creep.attackClips = v.Attacks;
        creep.hitClip = v.Hit;
        creep.deathVariants = v.Deaths.Length;
        if (v.Shield)
        {
            creep.shieldPaints = BuildShieldPaints(dir);
            AlignToClips(go, new[] { (clips["shield_idle"], 0.5f), (clips["shield_walk"], 0.3f), (clips["shield_block_idle"], 0.3f) },
                         "Shield", "ShieldTop", "ShieldFront", g => g.transform.forward);
        }

        go.AddComponent<SkeletonDeath>();   // смерть: рассыпается на кости
        SkeletonSounds sounds = go.AddComponent<SkeletonSounds>();
        sounds.boneCracks = LoadSounds("bones_crack_");
        sounds.swings = LoadSounds("skeleton_swing_");
        bool archer = v.Role == SkeletonAI.Role.Archer;
        sounds.swings = archer ? new AudioClip[0] : sounds.swings;
        sounds.bowShots = archer ? LoadSounds("skeleton_bow_") : new AudioClip[0];
        sounds.bowDraws = archer ? LoadSounds("skeleton_draw_") : new AudioClip[0];

        SkeletonAI ai = go.AddComponent<SkeletonAI>();
        ai.role = v.Role;
        ai.walkSpeed = v.Walk;
        ai.runSpeed = v.Run;
        ai.runDistance = v.RunDistance;
        ai.strafeSpeed = v.Strafe;
        if (v.Role == SkeletonAI.Role.Archer)
        {
            ai.arrowPrefab = BuildArrowPrefab(dir);
            go.AddComponent<BowRig>();
            AlignToClips(go, new[] { (clips["archer_aim"], 0.5f) }, "Bow", "BowTop", "BowString",
                         g => Find(g, "Hand.R").position - Find(g, "Bow").position);
        }

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[SkeletonCreep] {prefabPath}");
    }

    // Клипы по имени: сначала из модели (свои, например Parry), потом из Animations/Mixamo/<имя>.fbx
    private class ClipFinder
    {
        private readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

        public ClipFinder(string modelPath)
        {
            var files = AssetDatabase.FindAssets("t:Model", new[] { MixamoClipImporter.Folder.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath).Append(modelPath);
            foreach (string path in files)
                foreach (AnimationClip c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview__")) clips[c.name] = c;
        }

        public AnimationClip this[string name] =>
            clips.TryGetValue(name, out AnimationClip c) ? c : throw new KeyNotFoundException("Нет клипа " + name);
    }

    // Повернуть кость (лук, щит) в руке: в позах клипов (клип, момент) метка up смотрит вверх, а метка front -- по
    // направлению wantFront. Поворот для каждой позы свой -- берётся средний. Поза модели потом возвращается как была.
    private static void AlignToClips(GameObject go, (AnimationClip clip, float time)[] poses, string bone, string upMark,
                                     string frontMark, System.Func<GameObject, Vector3> wantFront)
    {
        Transform[] all = go.GetComponentsInChildren<Transform>(true);
        var saved = all.ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
        Transform b = Find(go, bone);
        Vector4 sum = Vector4.zero;
        Quaternion first = Quaternion.identity;
        foreach (var (clip, time) in poses)
        {
            Restore(saved);
            clip.SampleAnimation(go, time);
            Vector3 curUp = (Find(go, upMark).position - b.position).normalized;
            Vector3 curFront = Vector3.ProjectOnPlane(Find(go, frontMark).position - b.position, curUp).normalized;
            Vector3 front = Vector3.ProjectOnPlane(wantFront(go), Vector3.up).normalized;
            Quaternion fix = Quaternion.LookRotation(front, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(curFront, curUp));
            Quaternion local = Quaternion.Inverse(b.parent.rotation) * fix * b.rotation;   // нужный поворот в руке
            if (sum == Vector4.zero) first = local;
            float sign = Quaternion.Dot(first, local) < 0f ? -1f : 1f;
            sum += new Vector4(local.x, local.y, local.z, local.w) * sign;
        }
        sum.Normalize();
        Restore(saved);
        b.localRotation = new Quaternion(sum.x, sum.y, sum.z, sum.w);
    }

    private static void Restore(Dictionary<Transform, (Vector3 localPosition, Quaternion localRotation, Vector3 localScale)> saved)
    {
        foreach (var kv in saved)
        {
            kv.Key.localPosition = kv.Value.localPosition;
            kv.Key.localRotation = kv.Value.localRotation;
            kv.Key.localScale = kv.Value.localScale;
        }
    }

    // Звуки Sounds/Skeleton/<prefix>1, 2, ... по порядку
    private static AudioClip[] LoadSounds(string prefix) =>
        AssetDatabase.FindAssets(prefix + " t:AudioClip", new[] { Sounds.TrimEnd('/') })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .ToArray();

    private static Transform Find(GameObject go, string name) =>
        go.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

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
        var paints = new List<Material> { first };
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

    // ---------------------------------------------------------------- контроллер
    private static AnimatorController BuildController(string path, Variant v, ClipFinder clips)
    {
        AssetDatabase.DeleteAsset(path);
        AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (string f in new[] { "Speed", "Strafe" }) c.AddParameter(f, AnimatorControllerParameterType.Float);
        foreach (string t in new[] { "Attack", "Hit", "Fidget", "Die" }) c.AddParameter(t, AnimatorControllerParameterType.Trigger);
        foreach (string i in new[] { "AttackIndex", "DeathIndex" }) c.AddParameter(i, AnimatorControllerParameterType.Int);
        c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine sm = c.layers[0].stateMachine;

        // движение: точки смешивания -- (вбок, вперёд) в м/с, как шаг в анимации
        var tree = new BlendTree
        {
            name = "Move",
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "Strafe",
            blendParameterY = "Speed",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, c);
        foreach (var (clip, strafe, forward) in v.Move) tree.AddChild(clips[clip], new Vector2(strafe, forward));
        AnimatorState move = sm.AddState("Move", new Vector3(300, 0));
        move.motion = tree;
        sm.defaultState = move;

        for (int i = 0; i < v.Attacks.Length; i++)
        {
            AnimatorState prev = null;
            string[] chain = v.Attacks[i].Split('+');
            for (int k = 0; k < chain.Length; k++)
            {
                AnimatorState s = sm.AddState(chain[k], new Vector3(600 + 220 * k, -120 + 70 * i));
                s.motion = clips[chain[k]];
                s.tag = "Attack";
                if (prev == null)
                {
                    AnimatorStateTransition t = Instant(move.AddTransition(s), 0.1f);
                    t.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                    t.AddCondition(AnimatorConditionMode.Equals, i, "AttackIndex");
                }
                else Then(prev, s, 1f, 0.08f);
                prev = s;
            }
            Then(prev, move, 0.9f, 0.15f);
        }

        AnimatorState hit = sm.AddState("Hit", new Vector3(300, 200));
        hit.motion = clips[v.Hit];
        AnimatorStateTransition h = Instant(sm.AddAnyStateTransition(hit), 0.05f);
        h.canTransitionToSelf = false;
        h.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        h.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        Then(hit, move, 0.85f, 0.15f);

        if (v.Fidget != null)
        {
            AnimatorState fidget = sm.AddState("Fidget", new Vector3(50, -120));
            fidget.motion = clips[v.Fidget];
            Instant(move.AddTransition(fidget), 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Fidget");
            Then(fidget, move, 0.9f, 0.25f);
        }

        for (int i = 0; i < v.Deaths.Length; i++)
        {
            AnimatorState death = sm.AddState("Death" + (i + 1), new Vector3(600, 260 + 70 * i));
            death.motion = clips[v.Deaths[i]];
            AnimatorStateTransition t = Instant(sm.AddAnyStateTransition(death), 0.1f);
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Die");
            t.AddCondition(AnimatorConditionMode.Equals, i, "DeathIndex");
        }

        if (v.Shield) AddShieldStates(c, sm, move, clips);
        return c;
    }

    private static void AddShieldStates(AnimatorController c, AnimatorStateMachine sm, AnimatorState move, ClipFinder clips)
    {
        c.AddParameter("Blocking", AnimatorControllerParameterType.Bool);
        c.AddParameter("Parry", AnimatorControllerParameterType.Trigger);
        c.AddParameter("BlockHit", AnimatorControllerParameterType.Trigger);
        AnimatorState block = sm.AddState("Block", new Vector3(50, 60));
        AnimatorState hit = sm.AddState("BlockHit", new Vector3(-200, 60));
        AnimatorState parry = sm.AddState("Parry", new Vector3(50, 200));
        block.motion = clips["shield_block_idle"];
        hit.motion = clips["shield_block_hit"];
        parry.motion = clips["Parry"];

        Instant(move.AddTransition(block), 0.12f).AddCondition(AnimatorConditionMode.If, 0, "Blocking");
        Instant(block.AddTransition(move), 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Blocking");
        Instant(block.AddTransition(hit), 0.03f).AddCondition(AnimatorConditionMode.If, 0, "BlockHit");
        Then(hit, block, 0.9f, 0.1f);
        foreach (AnimatorState from in new[] { move, block })
            Instant(from.AddTransition(parry), 0.05f).AddCondition(AnimatorConditionMode.If, 0, "Parry");
        Then(parry, move, 0.95f, 0.1f);
    }

    private static AnimatorStateTransition Instant(AnimatorStateTransition t, float blend)
    {
        t.hasExitTime = false;
        t.duration = blend;
        return t;
    }

    // Переход, когда клип from доиграл до exitTime
    private static void Then(AnimatorState from, AnimatorState to, float exitTime, float blend)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = blend;
    }
}
