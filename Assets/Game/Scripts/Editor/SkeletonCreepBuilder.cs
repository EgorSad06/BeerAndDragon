using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Собирает готовых врагов-скелетов из моделей tools/blender/skeleton_psx.py и skeleton_archer.py:
//  - <модель>.controller: Idle <-> Walk по Speed; по триггеру Attack (из Idle и Walk) -- цепочка атаки
//    (крип: Attack; лучник: Draw -> Shoot), потом Idle; Death по триггеру Die отовсюду;
//  - префаб: модель + Animator + SkeletonCreep + Damageable + CapsuleCollider.
// Запуск: меню Tools/BeerAndDragon/Build Skeleton Enemies (или -executeMethod SkeletonCreepBuilder.Build).
public static class SkeletonCreepBuilder
{
    private const string Root = "Assets/Game/Assets/enemies/";

    private struct Variant
    {
        public string Folder, Model, Prefab;
        public string[] AttackChain;
        public float Health;
    }

    private static readonly Variant[] Variants =
    {
        new Variant { Folder = "Skeleton", Model = "Skeleton", Prefab = "SkeletonCreep",
                      AttackChain = new[] { "Attack" }, Health = 60f },
        new Variant { Folder = "SkeletonArcher", Model = "SkeletonArcher", Prefab = "SkeletonArcher",
                      AttackChain = new[] { "Draw", "Shoot" }, Health = 40f },
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
        AnimatorController controller = BuildController($"{dir}/{v.Model}.controller", modelPath, v.AttackChain);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = v.Prefab;

        Animator animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.3f;

        Damageable health = go.AddComponent<Damageable>();
        health.maxHealth = v.Health;
        health.destroyOnDeath = true;
        health.destroyDelay = 6f;   // тело лежит, потом исчезает

        go.AddComponent<SkeletonCreep>();

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[SkeletonCreep] {prefabPath}");
    }

    private static AnimatorController BuildController(string path, string modelPath, string[] attackChain)
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

        t = sm.AddAnyStateTransition(death);
        t.canTransitionToSelf = false;
        Instant(t, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Die");
        return c;
    }

    private static AnimatorStateTransition Instant(AnimatorStateTransition t, float blend)
    {
        t.hasExitTime = false;
        t.duration = blend;
        return t;
    }
}
