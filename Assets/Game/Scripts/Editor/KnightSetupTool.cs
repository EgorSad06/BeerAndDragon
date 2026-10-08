using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > BeerAndDragon > Setup Knight Model
//   Ставит модель рыцаря на игрока вместо капсулы (в Player/Model, который крутит KnightMovment),
//   подгоняет рост под капсулу, разворачивает лицом вперёд и вешает KnightAnimator
//   (процедурные анимации) + ThirdPersonCamera (V -- вид от 3-го лица).
public static class KnightSetupTool
{
    const string KnightFbx = "Assets/Game/Assets/Knight/knight.fbx";
    const float KnightHeight = 1.9f;

    [MenuItem("Tools/BeerAndDragon/Setup Knight Model")]
    public static void SetupKnight()
    {
        KnightMovment player = Object.FindFirstObjectByType<KnightMovment>();
        if (player == null)
        {
            Debug.LogWarning("[Knight] В сцене нет игрока (KnightMovment).");
            return;
        }
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(KnightFbx);
        if (fbx == null)
        {
            Debug.LogError("[Knight] Не нашёл " + KnightFbx);
            return;
        }

        Transform model = player.model;
        if (model == null)
        {
            GameObject m = new GameObject("Model");
            Undo.RegisterCreatedObjectUndo(m, "Knight Model");
            m.transform.SetParent(player.transform, false);
            Undo.RecordObject(player, "Knight Model");
            player.model = m.transform;
            model = m.transform;
        }

        Transform old = model.Find("KnightVisual");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        Movment look = player.GetComponentInChildren<Movment>(true);
        Transform cam = look != null ? look.transform : null;

        // Прячем болванку-капсулу (коллайдер остаётся) и обломки старого меша рыцаря на самом Player
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (cam != null && r.transform.IsChildOf(cam)) continue;   // оружие у камеры не трогаем
            bool placeholder = r.transform.IsChildOf(model) || r is SkinnedMeshRenderer;
            if (!placeholder || !r.enabled) continue;
            Undo.RecordObject(r, "Hide placeholder");
            r.enabled = false;
        }

        GameObject knight = (GameObject)Object.Instantiate(fbx);
        knight.name = "KnightVisual";
        Undo.RegisterCreatedObjectUndo(knight, "Knight Model");
        Animator anim = knight.GetComponent<Animator>();
        if (anim != null) Object.DestroyImmediate(anim); // позы рисует KnightAnimator
        foreach (Collider c in knight.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        Transform kt = knight.transform;
        kt.SetParent(model, false);
        kt.localPosition = Vector3.zero;
        kt.localRotation = Quaternion.identity;
        kt.localScale = Vector3.one;

        FaceForward(kt, model);

        // Рост как у капсулы, ноги на земле, по центру игрока
        Bounds b = Bounds(knight);
        if (b.size.y > 0.001f) kt.localScale *= KnightHeight / b.size.y;
        b = Bounds(knight);
        float bottom = player.transform.position.y - player.playerHeight * 0.5f;
        Collider capsule = player.capsule != null ? (Collider)player.capsule : player.GetComponentInChildren<CapsuleCollider>();
        if (capsule != null)
        {
            Physics.SyncTransforms();
            bottom = capsule.bounds.min.y; // капсула лежит в Model и сдвинута -- берём её настоящий низ
        }
        kt.position += new Vector3(player.transform.position.x - b.center.x, bottom - b.min.y, player.transform.position.z - b.center.z);

        foreach (SkinnedMeshRenderer smr in knight.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.enabled = true;
            smr.updateWhenOffscreen = true;
        }

        knight.AddComponent<KnightAnimator>();
        if (player.GetComponent<ThirdPersonCamera>() == null)
            Undo.AddComponent<ThirdPersonCamera>(player.gameObject);

        Selection.activeGameObject = knight;
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[Knight] Рыцарь на месте (Player/Model/KnightVisual). V -- вид от 3-го лица. Сохрани сцену.");
    }

    // Разворачивает модель так, чтобы персонаж смотрел в +Z родителя.
    // Направление берём по стопам: правая минус левая = "вправо", вперёд = вправо x вверх.
    static void FaceForward(Transform knight, Transform parent)
    {
        Transform footL = Find(knight, "Foot.l"), footR = Find(knight, "Foot.r");
        Transform toeL = Find(knight, "Foot.l_end");
        if (footL == null || footR == null)
        {
            Debug.LogWarning("[Knight] Не нашёл кости стоп -- проверь, куда смотрит рыцарь, и поверни KnightVisual вручную.");
            return;
        }

        Vector3 right = parent.InverseTransformDirection(footR.position - footL.position);
        right.y = 0f;
        if (right.sqrMagnitude < 1e-6f) return;
        Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
        knight.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.up)) * knight.localRotation;

        // Проверка носками: если носки смотрят назад -- кости названы зеркально, разворачиваем
        if (toeL != null)
        {
            Vector3 toe = parent.InverseTransformDirection(toeL.position - footL.position);
            if (toe.z < -0.3f * toe.magnitude)
                knight.localRotation = Quaternion.Euler(0f, 180f, 0f) * knight.localRotation;
        }
    }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Bounds Bounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
