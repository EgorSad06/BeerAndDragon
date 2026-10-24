using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Смерть скелета физикой вместо анимации (вызывает SkeletonCreep при смерти). Режимы:
//  - Bones -- рассыпается на кости: череп, рёбра, таз, позвонки, плечи, руки, ноги -- отдельные куски с Rigidbody;
//    куски у места попадания (Damageable.LastHitPoint/LastHitImpulse) разлетаются, остальные осыпаются вниз;
//  - Ragdoll -- рэгдолл: на кости ставятся Rigidbody + коллайдеры + CharacterJoint, Animator выключается,
//    тело отбрасывает по направлению удара;
//  - RagdollThenBones -- падает рэгдоллом и через ragdollTime рассыпается на кости;
//  - Animation -- анимация смерти из контроллера.
// Оружие из руки падает отдельно, щит/колчан/лук -- куски своих костей. Меши кусков режутся из скин-меша
// по весам костей (у моделей скиннинг жёсткий) один раз на модель. Останки лежат remainsTime, потом уходят
// под землю (SkeletonRemains). С игроком останки не сталкиваются -- не мешают ходить.
[RequireComponent(typeof(Animator))]
public class SkeletonDeath : MonoBehaviour
{
    public enum Mode { Animation, Ragdoll, Bones, RagdollThenBones }

    public Mode mode = Mode.Bones;

    [Header("Кости")]
    [Tooltip("Скорость разлёта кусков у места попадания (м/с); дальше 0.8 м от него куски просто осыпаются")]
    public float scatterSpeed = 4.5f;
    [Tooltip("Подброс вверх (м/с), случайный до этого значения")]
    public float popUp = 1.5f;
    [Tooltip("Плотность кусков (кг на м³ габарита) -- от неё масса, а значит, как далеко их отбрасывает дробь")]
    public float boneDensity = 250f;
    [Tooltip("Скин-меши, которые не превращаются в куски (тетива)")]
    public string[] skipRenderers = { "BowStringMesh" };

    [Header("Рэгдолл")]
    public float ragdollMass = 20f;
    [Tooltip("Толчок по месту попадания (Н·с)")]
    public float ragdollImpulse = 35f;
    [Tooltip("RagdollThenBones: через сколько секунд рассыпаться")]
    public float ragdollTime = 1.3f;

    [Header("Останки")]
    public float remainsTime = 12f;
    public float sinkTime = 2f;

    private const float HitReach = 0.8f;

    private Animator animator;
    private Transform remains;
    private readonly List<Rigidbody> ragdoll = new List<Rigidbody>();
    // коллайдеры кусков, оружия и рэгдолла: друг с другом не сталкиваются -- куски появляются вплотную
    // и иначе расталкивали бы друг друга во все стороны
    private readonly List<Collider> own = new List<Collider>();

    // Кусок скин-меша: кость, на которой он висит, и меш в её локальных координатах
    private struct Piece
    {
        public int bone;
        public Mesh mesh;
    }

    private static readonly Dictionary<Mesh, Piece[]> PieceCache = new Dictionary<Mesh, Piece[]>();

    // Тело рэгдолла: кость, её родитель в рэгдолле, доля массы, ограничения сустава (поворот вокруг оси
    // «вправо» и два качания)
    private static readonly (HumanBodyBones bone, HumanBodyBones parent, float mass, float twist, float swing1, float swing2)[] Bodies =
    {
        (HumanBodyBones.Hips, HumanBodyBones.LastBone, 0.2f, 0f, 0f, 0f),
        (HumanBodyBones.Chest, HumanBodyBones.Hips, 0.25f, 25f, 20f, 15f),
        (HumanBodyBones.Head, HumanBodyBones.Chest, 0.08f, 40f, 35f, 25f),
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest, 0.04f, 70f, 70f, 40f),
        (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, 0.03f, 75f, 20f, 10f),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.Chest, 0.04f, 70f, 70f, 40f),
        (HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, 0.03f, 75f, 20f, 10f),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, 0.09f, 55f, 35f, 20f),
        (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, 0.07f, 70f, 10f, 5f),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, 0.09f, 55f, 35f, 20f),
        (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, 0.07f, 70f, 10f, 5f),
    };

    public bool Shattered { get; private set; }
    public bool IsRagdoll => ragdoll.Count > 0 && !Shattered;
    public Transform Remains => remains;

    // Смерть физикой; false -- режим Animation, пусть играет анимация
    public bool Play()
    {
        if (mode == Mode.Animation) return false;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (!smr.sharedMesh.isReadable)
            {
                Debug.LogWarning($"SkeletonDeath: меш {smr.sharedMesh.name} без Read/Write -- играет анимация смерти", this);
                return false;
            }
        animator = GetComponent<Animator>();
        BowRig rig = GetComponent<BowRig>();
        if (rig != null) rig.enabled = false;
        GetHit(out Vector3 point, out Vector3 dir);

        DropWeapon(point, dir, mode == Mode.Bones);
        if (mode == Mode.Bones)
        {
            Shatter(point, dir, true);
            return true;
        }

        BuildRagdoll(point, dir);
        IgnoreOwn();
        if (mode == Mode.RagdollThenBones)
        {
            StartCoroutine(ShatterLater(point, dir));
        }
        else
        {
            // тело лежит и уходит под землю само, Damageable его не удаляет
            Damageable health = GetComponent<Damageable>();
            if (health != null) health.destroyOnDeath = false;
            gameObject.AddComponent<SkeletonRemains>().Init(remainsTime, sinkTime);
        }
        return true;
    }

    // Откуда и куда пришёл добивающий удар; без данных -- от камеры к скелету
    private void GetHit(out Vector3 point, out Vector3 dir)
    {
        Damageable health = GetComponent<Damageable>();
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        point = chest != null ? chest.position : transform.position + Vector3.up;
        dir = Vector3.zero;
        if (health != null && health.LastHitImpulse.sqrMagnitude > 0.0001f)
        {
            point = health.LastHitPoint;
            dir = health.LastHitImpulse.normalized;
        }
        else if (Camera.main != null)
        {
            dir = point - Camera.main.transform.position;
        }
        dir.y = Mathf.Max(dir.y, 0f);
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
    }

    private IEnumerator ShatterLater(Vector3 point, Vector3 dir)
    {
        yield return new WaitForSeconds(ragdollTime);
        Shatter(point, dir, false);
    }

    // ---------- Кости ----------

    private void Shatter(Vector3 hitPoint, Vector3 dir, bool fromHit)
    {
        Shattered = true;
        Transform parent = RemainsRoot();
        Collider[] player = PlayerColliders();
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || System.Array.IndexOf(skipRenderers, smr.name) >= 0)
            {
                smr.enabled = false;
                continue;
            }
            foreach (Piece p in Pieces(smr))
            {
                Transform bone = smr.bones[p.bone];
                if (bone == null || bone.lossyScale.x < 0.1f) continue;   // стрела, ещё не взятая в руку
                GameObject go = new GameObject(bone.name);
                go.layer = gameObject.layer;
                go.transform.SetPositionAndRotation(bone.position, bone.rotation);
                go.transform.localScale = bone.lossyScale;
                go.transform.SetParent(parent, true);
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                Collider col = bone == head ? go.AddComponent<SphereCollider>() : go.AddComponent<BoxCollider>();
                Ignore(col, player);
                own.Add(col);
                LiftAboveGround(go.transform, col);

                Rigidbody rb = AddBody(go, p.mesh.bounds.size, bone.lossyScale);
                Vector3 velocity;
                if (fromHit)
                {
                    velocity = Scatter(col.bounds.center, hitPoint, dir);
                }
                else
                {
                    Rigidbody body = bone.GetComponentInParent<Rigidbody>();
                    velocity = (body != null ? body.GetPointVelocity(bone.position) : Vector3.zero)
                               + Random.insideUnitSphere * 0.8f + Vector3.up * Random.Range(0.3f, 1f);
                }
                rb.linearVelocity = velocity;
                rb.angularVelocity = Random.insideUnitSphere * 12f;
            }
            smr.enabled = false;
        }
        // рэгдолл больше не нужен: тело спрятано, куски живут отдельно
        foreach (Rigidbody rb in ragdoll)
        {
            rb.isKinematic = true;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            {
                c.enabled = false;
                own.Remove(c);
            }
        }
        animator.enabled = false;
        IgnoreOwn();
    }

    private void IgnoreOwn()
    {
        for (int i = 0; i < own.Count; i++)
            for (int j = i + 1; j < own.Count; j++)
                Physics.IgnoreCollision(own[i], own[j]);
    }

    // Кусок, появившийся в земле (рэгдолл лежал, лук или щит ушли в пол), поднимается на её поверхность
    private static void LiftAboveGround(Transform piece, Collider col)
    {
        Bounds b = col.bounds;
        Vector3 from = new Vector3(b.center.x, b.max.y + 0.3f, b.center.z);
        float lift = 0f;
        foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, from.y - b.min.y, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.attachedRigidbody == null && !(h.collider is CharacterController))
                lift = Mathf.Max(lift, h.point.y - b.min.y);
        piece.position += Vector3.up * lift;
    }

    // Скорость куска: у места попадания -- по удару и от точки, дальше -- почти только вниз
    private Vector3 Scatter(Vector3 at, Vector3 hitPoint, Vector3 dir)
    {
        Vector3 away = at - hitPoint;
        float near = 1f - Mathf.Clamp01(away.magnitude / HitReach);
        Vector3 push = (dir * 0.65f + away.normalized * 0.35f).normalized;
        return push * scatterSpeed * near * Random.Range(0.6f, 1.2f)
               + Vector3.up * Random.Range(0.2f, 1f) * popUp
               + Random.insideUnitSphere * 0.6f;
    }

    private Rigidbody AddBody(GameObject go, Vector3 size, Vector3 scale)
    {
        Rigidbody rb = go.AddComponent<Rigidbody>();
        Vector3 s = Vector3.Scale(size, scale);
        rb.mass = Mathf.Clamp(s.x * s.y * s.z * boneDensity, 0.3f, 3f);
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.maxDepenetrationVelocity = 2f;
        return rb;
    }

    // Оружие из руки падает отдельным предметом
    private void DropWeapon(Vector3 hitPoint, Vector3 dir, bool scatter)
    {
        Transform socket = FindDeep(transform, SkeletonCreep.SocketName);
        if (socket == null) return;
        Collider[] player = PlayerColliders();
        for (int i = socket.childCount - 1; i >= 0; i--)
        {
            Transform w = socket.GetChild(i);
            MeshFilter mf = w.GetComponent<MeshFilter>();
            if (!w.gameObject.activeSelf || mf == null) continue;
            w.SetParent(RemainsRoot(), true);
            BoxCollider col = w.gameObject.AddComponent<BoxCollider>();
            Ignore(col, player);
            own.Add(col);
            Rigidbody rb = AddBody(w.gameObject, mf.sharedMesh.bounds.size, w.lossyScale);
            rb.mass = Mathf.Max(rb.mass, 1.2f);
            rb.linearVelocity = scatter ? Scatter(w.position, hitPoint, dir)
                                        : dir * 1.5f + Vector3.up * Random.Range(0.5f, 1.5f);
            rb.angularVelocity = Random.insideUnitSphere * 8f;
        }
    }

    private Transform RemainsRoot()
    {
        if (remains == null)
        {
            remains = new GameObject(name + " (останки)").transform;
            remains.gameObject.AddComponent<SkeletonRemains>().Init(remainsTime, sinkTime);
        }
        return remains;
    }

    // Режет скин-меш на куски по костям (пальцы -- вместе с кистью); результат кэшируется на меш
    private Piece[] Pieces(SkinnedMeshRenderer smr)
    {
        Mesh src = smr.sharedMesh;
        if (PieceCache.TryGetValue(src, out Piece[] cached) && System.Array.TrueForAll(cached, p => p.mesh != null))
            return cached;

        Transform[] bones = smr.bones;
        var fingers = new HashSet<Transform>();
        for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++)
        {
            Transform t = animator.GetBoneTransform(b);
            if (t != null) fingers.Add(t);
        }
        int[] owner = new int[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Transform t = bones[i];
            while (t != null && fingers.Contains(t)) t = t.parent;
            int o = System.Array.IndexOf(bones, t);
            owner[i] = o >= 0 ? o : i;
        }

        BoneWeight[] weights = src.boneWeights;
        Matrix4x4[] bind = src.bindposes;
        Vector3[] verts = src.vertices;
        Vector3[] normals = src.normals;
        Vector2[] uv = src.uv;
        var builders = new Dictionary<int, PieceBuilder>();
        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            int[] tris = src.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int o = owner[weights[tris[i]].boneIndex0];
                if (!builders.TryGetValue(o, out PieceBuilder pb))
                    builders[o] = pb = new PieceBuilder(src.subMeshCount);
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    if (!pb.remap.TryGetValue(v, out int nv))
                    {
                        nv = pb.verts.Count;
                        pb.remap[v] = nv;
                        pb.verts.Add(bind[o].MultiplyPoint3x4(verts[v]));
                        pb.normals.Add(normals.Length > 0 ? bind[o].MultiplyVector(normals[v]).normalized : Vector3.up);
                        pb.uv.Add(uv.Length > 0 ? uv[v] : Vector2.zero);
                    }
                    pb.tris[sub].Add(nv);
                }
            }
        }

        var pieces = new List<Piece>();
        foreach (KeyValuePair<int, PieceBuilder> kv in builders)
        {
            Mesh m = new Mesh { name = $"{src.name}_{bones[kv.Key].name}" };
            m.SetVertices(kv.Value.verts);
            m.SetNormals(kv.Value.normals);
            m.SetUVs(0, kv.Value.uv);
            m.subMeshCount = src.subMeshCount;
            for (int sub = 0; sub < src.subMeshCount; sub++) m.SetTriangles(kv.Value.tris[sub], sub);
            m.RecalculateBounds();
            pieces.Add(new Piece { bone = kv.Key, mesh = m });
        }
        Piece[] result = pieces.ToArray();
        PieceCache[src] = result;
        return result;
    }

    private class PieceBuilder
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] tris;
        public readonly Dictionary<int, int> remap = new Dictionary<int, int>();

        public PieceBuilder(int subMeshes)
        {
            tris = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) tris[i] = new List<int>();
        }
    }

    // ---------- Рэгдолл ----------

    private void BuildRagdoll(Vector3 hitPoint, Vector3 dir)
    {
        animator.enabled = false;
        SkinnedMeshRenderer body = MainMesh();
        Dictionary<Transform, Bounds> boxes = BodyBounds(body);
        Collider[] player = PlayerColliders();
        var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
        foreach (var spec in Bodies)
        {
            Transform bone = animator.GetBoneTransform(spec.bone);
            if (bone == null || !boxes.TryGetValue(bone, out Bounds b)) continue;
            Collider col;
            if (spec.bone == HumanBodyBones.Head)
            {
                SphereCollider s = bone.gameObject.AddComponent<SphereCollider>();
                s.center = b.center;
                s.radius = Mathf.Min(b.extents.x, Mathf.Min(b.extents.y, b.extents.z)) * 1.1f;
                col = s;
            }
            else
            {
                BoxCollider box = bone.gameObject.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size * 0.9f;
                col = box;
            }
            Ignore(col, player);
            own.Add(col);

            Rigidbody rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = ragdollMass * spec.mass;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 2f;
            bodies[spec.bone] = rb;
            ragdoll.Add(rb);

            if (spec.parent != HumanBodyBones.LastBone && bodies.TryGetValue(spec.parent, out Rigidbody parentBody))
            {
                CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBody;
                joint.axis = bone.InverseTransformDirection(transform.right);
                joint.swingAxis = bone.InverseTransformDirection(transform.forward);
                joint.lowTwistLimit = new SoftJointLimit { limit = -spec.twist };
                joint.highTwistLimit = new SoftJointLimit { limit = spec.twist };
                joint.swing1Limit = new SoftJointLimit { limit = spec.swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = spec.swing2 };
                joint.enableProjection = true;
                joint.enablePreprocessing = false;
            }
        }
        // щит, лук, колчан -- коробки на своих костях, чтобы не уходили в землю
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr == body || !smr.enabled || System.Array.IndexOf(skipRenderers, smr.name) >= 0) continue;
            foreach (Piece p in Pieces(smr))
            {
                Transform bone = smr.bones[p.bone];
                if (bone == null || bone.lossyScale.x < 0.1f || bone.GetComponentInParent<Rigidbody>() == null) continue;
                GameObject holder = new GameObject(smr.name + "Collider");
                holder.layer = gameObject.layer;
                holder.transform.SetParent(bone, false);
                BoxCollider box = holder.AddComponent<BoxCollider>();
                box.center = p.mesh.bounds.center;
                box.size = p.mesh.bounds.size;
                Ignore(box, player);
                own.Add(box);
            }
        }

        Rigidbody struck = null;
        float best = float.MaxValue;
        foreach (Rigidbody rb in ragdoll)
        {
            float d = (rb.worldCenterOfMass - hitPoint).sqrMagnitude;
            if (d < best)
            {
                best = d;
                struck = rb;
            }
        }
        if (struck != null) struck.AddForceAtPosition(dir * ragdollImpulse, hitPoint, ForceMode.Impulse);
    }

    // Габариты тела рэгдолла в координатах его кости: вершины костей, которые оно несёт (кисть -- с предплечьем)
    private Dictionary<Transform, Bounds> BodyBounds(SkinnedMeshRenderer smr)
    {
        var result = new Dictionary<Transform, Bounds>();
        if (smr == null) return result;
        var bodyBones = new HashSet<Transform>();
        foreach (var spec in Bodies)
        {
            Transform t = animator.GetBoneTransform(spec.bone);
            if (t != null) bodyBones.Add(t);
        }
        Transform[] bones = smr.bones;
        int[] carrier = new int[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Transform t = bones[i];
            while (t != null && !bodyBones.Contains(t)) t = t.parent;
            carrier[i] = t != null ? System.Array.IndexOf(bones, t) : -1;
        }
        Mesh mesh = smr.sharedMesh;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bind = mesh.bindposes;
        Vector3[] verts = mesh.vertices;
        for (int v = 0; v < verts.Length; v++)
        {
            int c = carrier[weights[v].boneIndex0];
            if (c < 0) continue;
            Vector3 p = bind[c].MultiplyPoint3x4(verts[v]);
            if (result.TryGetValue(bones[c], out Bounds b))
            {
                b.Encapsulate(p);
                result[bones[c]] = b;
            }
            else
            {
                result[bones[c]] = new Bounds(p, Vector3.zero);
            }
        }
        return result;
    }

    private SkinnedMeshRenderer MainMesh()
    {
        SkinnedMeshRenderer main = null;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (main == null || smr.sharedMesh.vertexCount > main.sharedMesh.vertexCount) main = smr;
        return main;
    }

    // ---------- Общее ----------

    private static Collider[] PlayerColliders()
    {
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
        return player != null ? player.GetComponentsInChildren<Collider>() : new Collider[0];
    }

    private static void Ignore(Collider col, Collider[] others)
    {
        foreach (Collider o in others) Physics.IgnoreCollision(col, o);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
