using System.Collections.Generic;
using UnityEngine;

// Перила / край, по которому можно грайндить. Линия задаётся двумя точками в ЛОКАЛЬНЫХ
// координатах объекта (учитывают его масштаб). Видна в сцене жёлтым гизмо.
//
// Ставить руками не обязательно: любой коллайдер на слое "Grind" при старте сам получает
// линии грайнда (см. BuildForLayer) -- тонкий брусок/труба = одна линия по центру верха,
// широкий ящик/бордюр = две линии по длинным верхним рёбрам.
public class GrindRail : MonoBehaviour
{
    public const string LayerName = "Grind";
    const float SingleLineMaxWidth = 0.4f;   // уже этого -- перила, шире -- ящик с двумя краями

    public Vector3 localStart = new Vector3(0f, 0.5f, -0.5f);
    public Vector3 localEnd = new Vector3(0f, 0.5f, 0.5f);

    private static readonly List<GrindRail> all = new List<GrindRail>();
    public static IReadOnlyList<GrindRail> All => all;

    public Vector3 Start => transform.TransformPoint(localStart);
    public Vector3 End => transform.TransformPoint(localEnd);
    public float Length => Vector3.Distance(Start, End);
    public Vector3 Direction => (End - Start).normalized;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    public Vector3 PointAt(float t) => Vector3.Lerp(Start, End, t);

    public Vector3 ClosestPoint(Vector3 p, out float t)
    {
        Vector3 a = Start, ab = End - Start;
        t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return a + ab * t;
    }

    // Все коллайдеры на слое Grind без ручной разметки получают линии. Возвращает, сколько создано.
    public static int BuildForLayer(int layer)
    {
        if (layer < 0) return 0;
        int created = 0;
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c.gameObject.layer != layer || c.isTrigger) continue;
            if (c.GetComponent<GrindRail>() != null) continue;
            created += AddFromCollider(c);
        }
        return created;
    }

    public static int AddFromCollider(Collider c)
    {
        Transform t = c.transform;
        if (c is BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            Vector3 ls = t.lossyScale;
            float worldX = Mathf.Abs(box.size.x * ls.x), worldZ = Mathf.Abs(box.size.z * ls.z);
            bool alongZ = worldZ >= worldX;
            float top = box.center.y + half.y;
            float width = alongZ ? worldX : worldZ;

            if (width <= SingleLineMaxWidth)
            {
                Add(t, box.center, alongZ, half, top, 0f);
                return 1;
            }
            Add(t, box.center, alongZ, half, top, -1f);
            Add(t, box.center, alongZ, half, top, 1f);
            return 2;
        }

        // Прочие коллайдеры: линия по центру верха вдоль длинной горизонтальной стороны
        Bounds b = c.bounds;
        bool longX = b.size.x >= b.size.z;
        Vector3 a = new Vector3(longX ? b.min.x : b.center.x, b.max.y, longX ? b.center.z : b.min.z);
        Vector3 e = new Vector3(longX ? b.max.x : b.center.x, b.max.y, longX ? b.center.z : b.max.z);
        GrindRail gr = c.gameObject.AddComponent<GrindRail>();
        gr.localStart = t.InverseTransformPoint(a);
        gr.localEnd = t.InverseTransformPoint(e);
        return 1;
    }

    // side: 0 -- по центру, -1/+1 -- по краю ящика
    static void Add(Transform t, Vector3 center, bool alongZ, Vector3 half, float top, float side)
    {
        GrindRail gr = t.gameObject.AddComponent<GrindRail>();
        if (alongZ)
        {
            float x = center.x + side * half.x;
            gr.localStart = new Vector3(x, top, center.z - half.z);
            gr.localEnd = new Vector3(x, top, center.z + half.z);
        }
        else
        {
            float z = center.z + side * half.z;
            gr.localStart = new Vector3(center.x - half.x, top, z);
            gr.localEnd = new Vector3(center.x + half.x, top, z);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.1f);
        Gizmos.DrawLine(Start, End);
        Gizmos.DrawSphere(Start, 0.05f);
        Gizmos.DrawSphere(End, 0.05f);
    }
}
