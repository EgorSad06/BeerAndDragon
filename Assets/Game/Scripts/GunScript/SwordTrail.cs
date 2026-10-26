using System.Collections.Generic;
using UnityEngine;

// Шлейф от клинка во время удара: лента между основанием и кончиком клинка за последние lifetime секунд.
// Точки хранятся в пространстве объекта меча (он ребёнок камеры), поэтому шлейф не размазывается,
// когда игрок бежит или крутит камерой. Создаёт Sword: SwordTrail.Attach(меч, пивот).
public class SwordTrail : MonoBehaviour
{
    public Transform blade;                  // пивот клинка: клинок идёт по его +Y
    public Vector3 baseLocal, tipLocal;      // начало и кончик ленты в пространстве blade
    public float lifetime = 0.12f;
    public Color color = new Color(1f, 0.95f, 0.85f, 0.55f);

    public bool Emitting
    {
        get => emitting;
        set
        {
            if (value && !emitting) samples.Clear();
            emitting = value;
        }
    }

    private struct Sample { public Vector3 b, t; public float time; }
    private readonly List<Sample> samples = new List<Sample>();
    private bool emitting;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private Material material;
    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> tris = new List<int>();

    public static SwordTrail Attach(Transform swordRoot, Transform pivot)
    {
        var go = new GameObject("SwordTrail");
        go.transform.SetParent(swordRoot, false);
        SwordTrail trail = go.AddComponent<SwordTrail>();
        trail.blade = pivot;
        // Длина клинка -- по мешу модели в пространстве пивота: шлейф от трети клинка до кончика
        float minY = 0f, maxY = 0.8f;
        MeshFilter mf = pivot.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            minY = float.MaxValue;
            maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float y = pivot.InverseTransformPoint(mf.transform.TransformPoint(c)).y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }
        trail.baseLocal = new Vector3(0f, Mathf.Lerp(minY, maxY, 0.4f), 0f);
        trail.tipLocal = new Vector3(0f, Mathf.Lerp(minY, maxY, 0.97f), 0f);
        return trail;
    }

    void Awake()
    {
        mesh = new Mesh { name = "SwordTrail" };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s != null)
        {
            material = new Material(s);
            ItemUser.SetupTransparent(material);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);   // видно с обеих сторон
            meshRenderer.sharedMaterial = material;
        }
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }

    void OnDisable()
    {
        samples.Clear();
        emitting = false;
        if (mesh != null) mesh.Clear();
    }

    void LateUpdate()
    {
        float now = Time.time;
        if (emitting && blade != null)
            samples.Add(new Sample
            {
                b = transform.InverseTransformPoint(blade.TransformPoint(baseLocal)),
                t = transform.InverseTransformPoint(blade.TransformPoint(tipLocal)),
                time = now
            });
        samples.RemoveAll(x => now - x.time > lifetime);

        mesh.Clear();
        if (samples.Count < 2) return;
        verts.Clear();
        colors.Clear();
        tris.Clear();
        for (int i = 0; i < samples.Count; i++)
        {
            float fade = 1f - (now - samples[i].time) / lifetime;   // свежие точки -- ярче
            verts.Add(samples[i].b);
            verts.Add(samples[i].t);
            colors.Add(new Color(color.r, color.g, color.b, 0f));
            colors.Add(new Color(color.r, color.g, color.b, color.a * fade * fade));
            if (i == 0) continue;
            int v = i * 2;
            tris.AddRange(new[] { v - 2, v - 1, v + 1, v - 2, v + 1, v });
        }
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
