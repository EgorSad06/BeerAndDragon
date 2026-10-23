using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Зона появления скелетов: круг радиуса radius вокруг объекта (виден только в редакторе), в нём один раз
// появляются creeps крипов, shields щитовиков и archers лучников в случайных точках круга -- на земле
// (луч сверху вниз), не ближе minSpacing друг к другу, лицом в случайную сторону.
// Когда: при старте сцены или когда игрок подошёл к кругу ближе triggerDistance.
// Создать: GameObject > BeerAndDragon > Skeleton Spawn Zone; радиус -- полем или маркером на круге в сцене.
public class SkeletonSpawnZone : MonoBehaviour
{
    public enum When { OnStart, PlayerNear }

    [Min(0.5f)] public float radius = 6f;

    [Header("Сколько каждого вида")]
    [Min(0)] public int creeps = 2;
    [Min(0)] public int shields = 1;
    [Min(0)] public int archers = 1;

    [Header("Когда появляются")]
    public When spawnWhen = When.OnStart;
    [Tooltip("Для PlayerNear: игрок ближе этого к краю круга")]
    public float triggerDistance = 10f;

    [Header("Расстановка")]
    public float minSpacing = 1.2f;
    [Tooltip("0 -- каждый раз по-разному")]
    public int seed = 0;
    public LayerMask groundMask = ~0;

    [Header("Префабы (подставляются сами)")]
    public GameObject creepPrefab;
    public GameObject shieldPrefab;
    public GameObject archerPrefab;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private bool done;

    public bool HasSpawned => done;
    public IReadOnlyList<GameObject> Spawned => spawned;

    // Все появившиеся скелеты мертвы (или удалены)
    public bool Cleared
    {
        get
        {
            if (!done) return false;
            foreach (GameObject s in spawned)
                if (s != null && !s.GetComponent<SkeletonCreep>().IsDead) return false;
            return true;
        }
    }

    void Start()
    {
        if (spawnWhen == When.OnStart) Spawn();
    }

    void Update()
    {
        if (done || spawnWhen != When.PlayerNear) return;
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
        if (player == null) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude <= radius + triggerDistance) Spawn();
    }

    // Расставить скелетов (один раз)
    public void Spawn()
    {
        if (done) return;
        done = true;
        var rng = seed != 0 ? new System.Random(seed) : new System.Random();
        var kinds = new List<GameObject>();
        AddKind(kinds, creepPrefab, creeps);
        AddKind(kinds, shieldPrefab, shields);
        AddKind(kinds, archerPrefab, archers);
        List<Vector2> points = PickPoints(kinds.Count, radius, minSpacing, rng);
        for (int i = 0; i < kinds.Count; i++)
        {
            Vector3 pos = Ground(transform.position + new Vector3(points[i].x, 0f, points[i].y));
            Quaternion rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            spawned.Add(Instantiate(kinds[i], pos, rot));
        }
    }

    private static void AddKind(List<GameObject> kinds, GameObject prefab, int count)
    {
        if (prefab == null) return;
        for (int i = 0; i < count; i++) kinds.Add(prefab);
    }

    // Точка на земле под (над) pos: луч сверху вниз; если есть NavMesh -- на него
    private Vector3 Ground(Vector3 pos)
    {
        Vector3 from = pos + Vector3.up * 20f;
        foreach (RaycastHit h in SortedHits(from))
        {
            if (h.collider.GetComponentInParent<SkeletonCreep>() != null) continue;
            pos = h.point;
            break;
        }
        if (NavMesh.SamplePosition(pos, out NavMeshHit nav, 1.5f, NavMesh.AllAreas)) pos = nav.position;
        return pos + Vector3.up * 0.05f;
    }

    private RaycastHit[] SortedHits(Vector3 from)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 60f, groundMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    // count случайных точек в круге радиуса radius (равномерно по площади), по возможности не ближе spacing
    // друг к другу: до 30 попыток на точку, потом -- лучшая из попыток (самая далёкая от остальных)
    public static List<Vector2> PickPoints(int count, float radius, float spacing, System.Random rng)
    {
        var points = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 best = Vector2.zero;
            float bestGap = -1f;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                float r = radius * Mathf.Sqrt((float)rng.NextDouble());
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                float gap = float.MaxValue;
                foreach (Vector2 q in points) gap = Mathf.Min(gap, Vector2.Distance(p, q));
                if (gap > bestGap)
                {
                    best = p;
                    bestGap = gap;
                }
                if (gap >= spacing) break;
            }
            points.Add(best);
        }
        return points;
    }

#if UNITY_EDITOR
    // Префабы по умолчанию -- при добавлении компонента
    void Reset()
    {
        creepPrefab = Load("Skeleton/SkeletonCreep");
        shieldPrefab = Load("SkeletonShield/SkeletonShield");
        archerPrefab = Load("SkeletonArcher/SkeletonArcher");
    }

    private static GameObject Load(string path) =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Assets/enemies/{path}.prefab");

    // Круг виден в редакторе всегда (в игре -- нет): контур, у выбранной зоны -- заливка и подпись
    void OnDrawGizmos() => DrawZone(false);
    void OnDrawGizmosSelected() => DrawZone(true);

    private void DrawZone(bool selected)
    {
        var color = new Color(0.85f, 0.3f, 0.25f);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius, selected ? 3f : 1.5f);
        if (!selected) return;
        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 0.08f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, radius);
        if (spawnWhen == When.PlayerNear)
        {
            UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 0.5f);
            UnityEditor.Handles.DrawDottedLine(transform.position + Vector3.right * radius,
                                               transform.position + Vector3.right * (radius + triggerDistance), 4f);
        }
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f,
            $"Скелеты: крипы {creeps}, щитовики {shields}, лучники {archers}");
    }
#endif
}
