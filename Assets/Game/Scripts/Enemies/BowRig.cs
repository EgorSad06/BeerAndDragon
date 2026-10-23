using UnityEngine;

// Тетива и наложенная стрела лучника. Анимации Mixamo двигают только тело, кости лука двигаются здесь,
// по событиям клипов (MixamoClipImporter): OnArrowTake -- стрела в руке, OnArrowNock -- стрела на тетиве (середина
// тетивы идёт за пальцами), OnArrowRelease -- тетива распрямилась, стрела улетела (её заменяет SkeletonArrow).
// Стрела всегда смотрит на ArrowRest у рукояти. Если атака прервалась (удар, смерть) -- всё сбрасывается.
[RequireComponent(typeof(Animator))]
public class BowRig : MonoBehaviour
{
    private Animator animator;
    private Transform bowString, arrowNock, arrowRest;
    private Vector3 stringRest;          // середина тетивы в покое (в системе кости Bow)
    private Vector3 arrowAxis;           // вдоль стрелы (в системе кости ArrowNock)
    private bool shown, nocked;

    void Awake()
    {
        animator = GetComponent<Animator>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "BowString") bowString = t;
            if (t.name == "ArrowNock") arrowNock = t;
            if (t.name == "ArrowRest") arrowRest = t;
        }
        stringRest = bowString.localPosition;
        // в позе покоя (T-поза модели) стрела смотрит вперёд
        arrowAxis = Quaternion.Inverse(arrowNock.rotation) * transform.forward;
    }

    void OnArrowTake() => shown = true;

    void OnArrowNock()
    {
        shown = true;
        nocked = true;
    }

    void OnArrowRelease()
    {
        shown = false;
        nocked = false;
    }

    void LateUpdate()
    {
        bool inAttack = animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack") ||
                        animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Attack");
        if (!inAttack) shown = nocked = false;

        arrowNock.localScale = shown ? Vector3.one : Vector3.one * 0.001f;
        if (shown)
        {
            Vector3 along = arrowNock.rotation * arrowAxis;
            Vector3 want = arrowRest.position - arrowNock.position;
            arrowNock.rotation = Quaternion.FromToRotation(along, want) * arrowNock.rotation;
        }
        bowString.localPosition = stringRest;
        if (nocked) bowString.position = arrowNock.position;
    }
}
