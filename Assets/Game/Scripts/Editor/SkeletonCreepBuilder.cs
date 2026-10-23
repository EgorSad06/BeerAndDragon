using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Собирает готового врага «скелет-крип» из модели Skeleton.fbx:
//  - Skeleton.controller: Idle <-> Walk по Speed, Attack по триггеру (из Idle и Walk), Death по триггеру Die отовсюду;
//  - SkeletonCreep.prefab: модель + Animator + SkeletonCreep + Damageable + CapsuleCollider.
// Запуск: меню Tools/BeerAndDragon/Build Skeleton Creep (или -executeMethod SkeletonCreepBuilder.Build).
public static class SkeletonCreepBuilder
{
    private const string Dir = "Assets/Game/Assets/enemies/Skeleton";
    private const string Model = Dir + "/Skeleton.fbx";
    private const string ControllerPath = Dir + "/Skeleton.controller";
    private const string PrefabPath = Dir + "/SkeletonCreep.prefab";

    [MenuItem("Tools/BeerAndDragon/Build Skeleton Creep")]
    public static void Build()
    {
        AnimatorController controller = BuildController();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "SkeletonCreep";

        Animator animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.3f;

        Damageable health = go.AddComponent<Damageable>();
        health.maxHealth = 60f;
        health.destroyOnDeath = true;
        health.destroyDelay = 6f;   // тело лежит, потом исчезает

        go.AddComponent<SkeletonCreep>();

        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkeletonCreep] {PrefabPath}, {ControllerPath}");
    }

    private static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
            .Where(a => !a.name.StartsWith("__preview__")).ToArray();
        AnimationClip Clip(string name) => clips.First(a => a.name == name);

        AnimatorStateMachine sm = c.layers[0].stateMachine;
        AnimatorState idle = sm.AddState("Idle", new Vector3(300, 0));
        AnimatorState walk = sm.AddState("Walk", new Vector3(300, 120));
        AnimatorState attack = sm.AddState("Attack", new Vector3(550, 60));
        AnimatorState death = sm.AddState("Death", new Vector3(550, 220));
        idle.motion = Clip("Idle");
        walk.motion = Clip("Walk");
        attack.motion = Clip("Attack");
        death.motion = Clip("Death");
        sm.defaultState = idle;

        AnimatorStateTransition t = idle.AddTransition(walk);
        Instant(t, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        t = walk.AddTransition(idle);
        Instant(t, 0.15f).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        foreach (AnimatorState from in new[] { idle, walk })
            Instant(from.AddTransition(attack), 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Attack");
        t = attack.AddTransition(idle);
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
