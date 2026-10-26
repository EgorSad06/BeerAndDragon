using System.Collections.Generic;
using UnityEngine;

// Подкат (Shift, само движение -- в KnightMovement): бьёт ногой всех, в кого въехал, и видно ногу.
//  - урон и отброс (вперёд и вверх) каждому IDamageable перед ногами, каждому один раз за подкат;
//  - от первого лица снизу выезжает правая нога рыцаря (кусок его же модели), камера опускается;
//  - при попадании камеру дёргает.
// Добавляется сам из KnightMovement -- в сцене ничего настраивать не нужно.
[RequireComponent(typeof(KnightMovement))]
public class PlayerSlide : MonoBehaviour
{
    [Header("Удар ногой")]
    public float damage = 40f;
    public float knockback = 9f;            // вперёд, м/с для Rigidbody / импульс для Damageable
    public float knockUp = 4f;
    public float hitRadius = 0.7f;
    public float hitReach = 0.8f;           // центр зоны удара впереди игрока, у ног
    public Vector2 hitKick = new Vector2(1.5f, 0f);
    public LayerMask hitMask = ~0;

    [Header("Вид от первого лица")]
    public float cameraDip = 0.5f;          // насколько опускается камера
    public float dipSpeed = 12f;
    public Vector3 legPosition = new Vector3(0.4f, -0.3f, 0.1f);       // бедро относительно камеры (справа снизу)
    public Vector3 legEuler = new Vector3(-92f, -35f, 0f);              // нога вперёд к центру экрана, носок вверх
    public float legSlideIn = 0.1f;         // секунд на то, чтобы нога выехала снизу

    private KnightMovement movement;
    private ThirdPersonCamera thirdPersonCamera;
    private SkaterController skater;
    private CameraPOV look;
    private Vector3 cameraRest;
    private float dip;
    private bool wasSliding;
    private float slideTime;
    private readonly HashSet<object> hitThisSlide = new HashSet<object>();
    private readonly Collider[] buffer = new Collider[32];

    private GameObject leg;
    private Mesh legMesh;

    public bool IsSliding => movement != null && movement.state == KnightMovement.MovementState.sliding;

    void Awake()
    {
        movement = GetComponent<KnightMovement>();
        thirdPersonCamera = GetComponent<ThirdPersonCamera>();
        skater = GetComponent<SkaterController>();
    }

    void Start()
    {
        look = GetComponentInChildren<CameraPOV>();
        if (look == null) look = FindFirstObjectByType<CameraPOV>();
        if (look != null) cameraRest = look.transform.localPosition;   // камера от 1-го лица -- ребёнок игрока
        BuildLeg();
    }

    void OnDestroy()
    {
        if (legMesh != null) Destroy(legMesh);
    }

    void Update()
    {
        bool sliding = IsSliding;
        if (sliding && !wasSliding)
        {
            hitThisSlide.Clear();
            slideTime = 0f;
        }
        wasSliding = sliding;
        if (sliding) slideTime += Time.deltaTime;

        if (sliding) Kick();
        UpdateView(sliding);
    }

    // ---------------- удар ----------------

    private void Kick()
    {
        Vector3 forward = movement.orientation != null ? movement.orientation.forward : transform.forward;
        Vector3 feet = transform.position + Vector3.down * (movement.playerHeight * 0.5f - 0.35f);
        Vector3 center = feet + forward * hitReach;
        int n = Physics.OverlapSphereNonAlloc(center, hitRadius, buffer, hitMask, QueryTriggerInteraction.Ignore);
        bool hitEnemy = false;
        for (int i = 0; i < n; i++)
        {
            Collider col = buffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            bool convex = col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex);
            Vector3 point = convex ? col.ClosestPoint(center) : col.bounds.ClosestPoint(center);
            Vector3 push = forward * knockback + Vector3.up * knockUp;

            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target != null && hitThisSlide.Add(target))
            {
                hitEnemy = true;
                if (target is Damageable d) d.TakeHit(damage, point, push);
                else target.TakeDamage(damage);
            }
            Rigidbody rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic && rb.transform != transform && hitThisSlide.Add(rb))
                rb.AddForce(push, ForceMode.VelocityChange);
        }
        if (hitEnemy && look != null) look.AddRecoil(hitKick.x, Random.Range(-hitKick.y, hitKick.y));
    }

    void OnDrawGizmosSelected()
    {
        KnightMovement m = movement != null ? movement : GetComponent<KnightMovement>();
        if (m == null) return;
        Vector3 forward = m.orientation != null ? m.orientation.forward : transform.forward;
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position + Vector3.down * (m.playerHeight * 0.5f - 0.35f) + forward * hitReach, hitRadius);
    }

    // ---------------- вид ----------------

    private bool FirstPerson => (thirdPersonCamera == null || !thirdPersonCamera.thirdPerson) && (skater == null || !skater.IsRiding);

    private void UpdateView(bool sliding)
    {
        dip = Mathf.MoveTowards(dip, sliding ? 1f : 0f, Time.deltaTime * dipSpeed * 0.5f);
        float eased = dip * dip * (3f - 2f * dip);
        // от 3-го лица и на скейте камерой рулят ThirdPersonCamera / SkaterController
        if (look != null && FirstPerson) look.transform.localPosition = cameraRest + Vector3.down * (cameraDip * eased);

        if (leg == null) return;
        bool show = sliding && FirstPerson;
        if (leg.activeSelf != show) leg.SetActive(show);
        if (!show) return;
        float k = Mathf.Clamp01(slideTime / Mathf.Max(0.01f, legSlideIn));
        k = 1f - (1f - k) * (1f - k);
        Vector3 shake = Random.insideUnitSphere * 0.006f;   // дрожит, пока едем по земле
        leg.transform.localPosition = legPosition + Vector3.down * (0.35f * (1f - k)) + shake;
        leg.transform.localRotation = Quaternion.Euler(legEuler);
    }

    // Правая нога рыцаря: треугольники, привязанные к костям правой ноги, в позе покоя.
    // Координаты -- от бедра: нога висит по -Y, носок смотрит в +Z; под камерой её поворачивает legEuler.
    private void BuildLeg()
    {
        if (look == null) return;
        KnightAnimator knight = GetComponentInChildren<KnightAnimator>(true);
        SkinnedMeshRenderer smr = knight != null ? knight.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) return;
        Mesh src = smr.sharedMesh;
        Transform[] bones = smr.bones;
        int Bone(string name) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == name) return i; return -1; }
        int hip = Bone("UpperLeg.r"), knee = Bone("LowerLeg.r"), foot = Bone("Foot.r");
        int head = Bone("Head"), body = Bone("Body"), armL = Bone("UpperArm.l"), armR = Bone("UpperArm.r");
        if (hip < 0 || knee < 0 || foot < 0 || head < 0 || armL < 0 || armR < 0) return;
        Matrix4x4[] bind = src.bindposes;
        Vector3 BonePos(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);

        Vector3 up = (BonePos(head) - BonePos(body >= 0 ? body : hip)).normalized;
        Vector3 right = (BonePos(armR) - BonePos(armL)).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        Vector3 origin = BonePos(hip);

        var verts = new List<Vector3>(); src.GetVertices(verts);
        var normals = new List<Vector3>(); src.GetNormals(normals);
        var uvs = new List<Vector2>(); src.GetUVs(0, uvs);
        BoneWeight[] w = src.boneWeights;
        var legBones = new HashSet<int> { hip, knee, foot };
        bool IsLeg(int v) => legBones.Contains(Dominant(w[v]));

        var map = new Dictionary<int, int>();
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var tris = new List<int>();
        Vector3 ToLeg(Vector3 p) => new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, forward));
        for (int s = 0; s < src.subMeshCount; s++)
        {
            int[] t = src.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
            {
                if (!(IsLeg(t[i]) && IsLeg(t[i + 1]) && IsLeg(t[i + 2]))) continue;
                for (int j = 0; j < 3; j++)
                {
                    int v = t[i + j];
                    if (!map.TryGetValue(v, out int m))
                    {
                        m = nv.Count;
                        map[v] = m;
                        nv.Add(ToLeg(verts[v] - origin));
                        nn.Add(normals.Count > v ? ToLeg(normals[v]) : Vector3.up);
                        nu.Add(uvs.Count > v ? uvs[v] : Vector2.zero);
                    }
                    tris.Add(m);
                }
            }
        }
        if (tris.Count == 0) return;

        // Модель рыцаря может быть отмасштабирована -- нога в кадре того же размера, что и в мире
        float scale = smr.transform.lossyScale.y;
        legMesh = new Mesh { name = "FirstPersonLeg" };
        legMesh.SetVertices(nv.ConvertAll(p => p * scale));
        legMesh.SetNormals(nn);
        legMesh.SetUVs(0, nu);
        legMesh.SetTriangles(tris, 0);
        legMesh.RecalculateBounds();

        leg = new GameObject("FirstPersonLeg");
        leg.transform.SetParent(look.transform, false);
        leg.AddComponent<MeshFilter>().sharedMesh = legMesh;
        MeshRenderer mr = leg.AddComponent<MeshRenderer>();
        mr.sharedMaterial = smr.sharedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // тень даёт сама модель рыцаря
        leg.SetActive(false);
    }

    private static int Dominant(BoneWeight b)
    {
        int i = b.boneIndex0;
        float best = b.weight0;
        if (b.weight1 > best) { best = b.weight1; i = b.boneIndex1; }
        if (b.weight2 > best) { best = b.weight2; i = b.boneIndex2; }
        if (b.weight3 > best) i = b.boneIndex3;
        return i;
    }
}
