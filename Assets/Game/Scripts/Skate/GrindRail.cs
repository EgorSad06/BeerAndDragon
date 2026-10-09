using System.Collections.Generic;
using UnityEngine;

// Перила / край, по которому можно грайндить. Линия задаётся двумя точками в ЛОКАЛЬНЫХ
// координатах объекта (учитывают его масштаб). Видна в сцене жёлтым гизмо.
public class GrindRail : MonoBehaviour
{
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

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.1f);
        Gizmos.DrawLine(Start, End);
        Gizmos.DrawSphere(Start, 0.05f);
        Gizmos.DrawSphere(End, 0.05f);
    }
}
