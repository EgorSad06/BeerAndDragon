using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Портрет рыцаря в левом нижнем углу (как в Warcraft / Dota). Создаёт GameHUD -- в сцене ничего не нужно.
//  - живая 3D-голова: копия модели рыцаря (без скриптов) далеко под картой на слое Portrait, своя камера рендерит её
//    в маленькую текстуру; голова дышит, оглядывается, дёргается от ударов;
//  - рамка и картинки -- Resources/UI/Portrait (PortraitFrame, PortraitBlood, PortraitSkull, KnightHelmet_0..4).
// Эффекты по здоровью копятся (на 70% есть всё от 90% и 80%):
//  90% грязь на шлеме, 80% потёртости, 70% вмятина, 60% кровь в окне портрета, 50% крови больше,
//  40% рыцаря покачивает, 30% кровь на шлеме, 20% окно трясёт в такт сердцу и портрет пульсирует,
//  10% рыцарь троится, 0% -- всё пропадает, появляется череп. При смене уровня портрет встряхивает.
public class PlayerPortrait : MonoBehaviour
{
    public const string LayerName = "Portrait";
    private const string Res = "UI/Portrait/";

    // Окно в картинке рамки (пиксели, от левого нижнего угла) и масштаб рамки на экране (канвас 1920x1080)
    private static readonly Rect WindowPx = new Rect(49f, 48f, 399f, 411f);
    private const float FrameScale = 0.5f;
    private const int RtWidth = 116, RtHeight = 120;

    public float RightEdge { get; private set; }

    private PlayerHealth health;
    private int level = -1;
    private float hit, hitSide = 1f, shake, deathTime = -1f;

    // UI
    private RectTransform content;
    private RawImage face, ghostA, ghostB;
    private Image flash, skull, skullBack;
    private readonly List<(Image img, int level, float alpha)> blood = new List<(Image, int, float)>();

    // 3D
    private GameObject rig;
    private Camera cam;
    private RenderTexture rt;
    private SkinnedMeshRenderer smr;
    private Mesh cleanMesh, dentedMesh;
    private Material helmetMat;
    private Texture2D[] helmetStages;
    private Transform headBone, bodyBone;
    private Quaternion headRest, bodyRest;
    private Vector3 bodyRestPos;
    private readonly List<Object> owned = new List<Object>();

    public static PlayerPortrait Create(RectTransform canvas, PlayerHealth health)
    {
        Sprite frameSprite = Resources.Load<Sprite>(Res + "PortraitFrame");
        Vector2 frameSize = frameSprite != null ? frameSprite.rect.size * FrameScale : new Vector2(278f, 282f);

        RectTransform root = NewRect("Portrait", canvas);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = new Vector2(14f, 10f);
        root.sizeDelta = frameSize;

        PlayerPortrait p = root.gameObject.AddComponent<PlayerPortrait>();
        p.health = health;
        p.RightEdge = root.anchoredPosition.x + frameSize.x;
        p.BuildUI(root, frameSprite);
        p.BuildRig();
        if (health != null) health.Damaged += p.OnDamaged;
        return p;
    }

    void OnDestroy()
    {
        if (health != null) health.Damaged -= OnDamaged;
        if (rig != null) Destroy(rig);
        if (cam != null) Destroy(cam.gameObject);
        foreach (Object o in owned) if (o != null) Destroy(o);
    }

    // ---------------- UI ----------------

    private void BuildUI(RectTransform root, Sprite frameSprite)
    {
        RectTransform window = NewRect("Window", root);
        window.anchorMin = window.anchorMax = window.pivot = Vector2.zero;
        window.anchoredPosition = WindowPx.position * FrameScale;
        window.sizeDelta = WindowPx.size * FrameScale;
        window.gameObject.AddComponent<RectMask2D>();

        Image back = NewImage("Back", window, new Color(0.05f, 0.04f, 0.04f));
        Stretch(back.rectTransform);

        content = NewRect("Content", window);
        Stretch(content);
        ghostA = NewRaw("GhostA", content, new Color(1f, 0.55f, 0.55f, 0.4f));
        ghostB = NewRaw("GhostB", content, new Color(0.6f, 0.6f, 1f, 0.4f));
        face = NewRaw("Face", content, Color.white);

        // Кровь в окне: (центр в долях окна, размер в долях, поворот, уровень, непрозрачность)
        Sprite bloodSprite = Resources.Load<Sprite>(Res + "PortraitBlood");
        if (bloodSprite != null)
        {
            var splats = new (Vector2 at, float size, float rot, int lvl, float a)[]
            {
                (new Vector2(0.12f, 0.1f), 0.55f, 20f, 4, 0.9f),
                (new Vector2(0.9f, 0.82f), 0.35f, -130f, 4, 0.85f),
                (new Vector2(0.85f, 0.12f), 0.6f, 75f, 5, 0.9f),
                (new Vector2(0.08f, 0.78f), 0.45f, -60f, 5, 0.85f),
                (new Vector2(0.5f, 0.02f), 0.5f, 170f, 5, 0.8f),
            };
            foreach (var s in splats)
            {
                Image img = NewImage("Blood", window, Color.white);
                img.sprite = bloodSprite;
                img.preserveAspect = true;
                RectTransform r = img.rectTransform;
                r.anchorMin = r.anchorMax = s.at;
                r.sizeDelta = Vector2.one * s.size * window.sizeDelta.y;
                r.localRotation = Quaternion.Euler(0f, 0f, s.rot);
                blood.Add((img, s.lvl, s.a));
            }
        }

        flash = NewImage("HitFlash", window, new Color(0.8f, 0f, 0f, 0f));
        Stretch(flash.rectTransform);

        skullBack = NewImage("SkullBack", window, Color.black);
        Stretch(skullBack.rectTransform);
        skull = NewImage("Skull", window, Color.white);
        skull.sprite = Resources.Load<Sprite>(Res + "PortraitSkull");
        skull.preserveAspect = true;
        Stretch(skull.rectTransform, 10f);
        skullBack.gameObject.SetActive(false);
        skull.gameObject.SetActive(false);

        Image frame = NewImage("Frame", root, Color.white);
        frame.sprite = frameSprite;
        Stretch(frame.rectTransform);
        if (frameSprite == null) frame.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    }

    // ---------------- 3D-голова ----------------

    private void BuildRig()
    {
        int layer = LayerMask.NameToLayer(LayerName);
        KnightAnimator source = FindFirstObjectByType<KnightAnimator>();
        if (layer < 0 || source == null)
        {
            face.enabled = ghostA.enabled = ghostB.enabled = false;
            return;
        }

        // Копия модели рыцаря без скриптов: создаём под выключенным объектом, чтобы не сработали их Awake
        rig = new GameObject("PortraitRig");
        rig.SetActive(false);
        rig.transform.position = new Vector3(0f, -1000f, 0f);
        GameObject copy = Instantiate(source.gameObject, rig.transform);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Rigidbody b in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(b);
        foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        foreach (Animator a in copy.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        smr = copy.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || !smr.sharedMesh.isReadable)
        {
            Destroy(rig);
            face.enabled = ghostA.enabled = ghostB.enabled = false;
            return;
        }
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        ResetToBindPose();
        BuildMeshes();
        rig.SetActive(true);

        headBone = FindBone("Head");
        bodyBone = FindBone("Body");
        if (headBone != null) headRest = headBone.localRotation;
        if (bodyBone != null) { bodyRest = bodyBone.localRotation; bodyRestPos = bodyBone.localPosition; }

        // Камера портрета и свой свет; остальные камеры и свет слой Portrait не видят
        int mask = 1 << layer;
        foreach (Camera other in Camera.allCameras) other.cullingMask &= ~mask;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.cullingMask &= ~mask;

        rt = new RenderTexture(RtWidth, RtHeight, 16) { filterMode = FilterMode.Point, name = "PortraitRT" };
        owned.Add(rt);
        cam = new GameObject("PortraitCamera").AddComponent<Camera>();
        cam.transform.SetParent(rig.transform, false);
        cam.cullingMask = mask;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.045f, 0.04f);
        cam.fieldOfView = 21f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 10f;
        cam.targetTexture = rt;
        Transform root = copy.transform;
        Vector3 focus = (headBone != null ? headBone.position : root.position + Vector3.up * 1.6f) + root.up * 0.14f;
        cam.transform.position = focus + root.forward * 1.0f + root.up * 0.08f;   // голова целиком, снизу -- плечи
        cam.transform.LookAt(focus, root.up);
        face.texture = ghostA.texture = ghostB.texture = rt;

        // Тёмный задник за спиной: пустой фон дымка PS1 (RetroColor) считает небом и красит в цвет заката
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(backdrop.GetComponent<Collider>());
        backdrop.name = "Backdrop";
        backdrop.layer = layer;
        backdrop.transform.SetParent(rig.transform, false);
        backdrop.transform.position = focus - root.forward * 0.8f;
        backdrop.transform.rotation = Quaternion.LookRotation(-root.forward, root.up);   // лицом к камере
        backdrop.transform.localScale = Vector3.one * 2f;
        Material backMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        backMat.SetColor("_BaseColor", new Color(0.07f, 0.05f, 0.045f));
        owned.Add(backMat);
        Renderer br = backdrop.GetComponent<Renderer>();
        br.sharedMaterial = backMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        AddLight("Key", rig.transform, LightType.Directional, new Color(1f, 0.85f, 0.68f), 1.9f, mask,
                 Quaternion.LookRotation(-root.forward * 0.8f - root.right * 0.5f - root.up * 0.45f));
        AddLight("Rim", rig.transform, LightType.Directional, new Color(0.45f, 0.55f, 1f), 0.8f, mask,
                 Quaternion.LookRotation(root.forward * 0.7f + root.right * 0.6f - root.up * 0.2f));
    }

    private void AddLight(string name, Transform parent, LightType type, Color color, float intensity, int mask, Quaternion rot)
    {
        Light l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.rotation = rot;
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.cullingMask = mask;
        l.shadows = LightShadows.None;
    }

    private Transform FindBone(string name)
    {
        foreach (Transform b in smr.bones) if (b != null && b.name == name) return b;
        return null;
    }

    // Поза покоя из bindposes: копия могла сняться посреди анимации
    private void ResetToBindPose()
    {
        Transform[] bones = smr.bones;
        Matrix4x4[] bind = smr.sharedMesh.bindposes;
        var order = new List<int>();
        for (int i = 0; i < bones.Length && i < bind.Length; i++) if (bones[i] != null) order.Add(i);
        order.Sort((a, b) => Depth(bones[a]).CompareTo(Depth(bones[b])));
        foreach (int i in order)
        {
            Matrix4x4 m = smr.transform.localToWorldMatrix * bind[i].inverse;
            bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        for (; t != null; t = t.parent) d++;
        return d;
    }

    // Шлем -- отдельный материал со стадиями повреждений. UV шлема у модели зеркальные (обе половины на одних
    // пикселях), поэтому правая половина уходит в свой угол текстуры (сдвиг как в tools/blender/helmet_damage.py).
    private static readonly Vector2 RightHalfShift = new Vector2(-0.38f, -0.55f);
    private const float DentDepth = 0.35f;

    private void BuildMeshes()
    {
        Mesh src = smr.sharedMesh;
        Transform[] bones = smr.bones;
        Matrix4x4[] bind = src.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        int head = Bone("Head"), armL = Bone("UpperArm.l"), armR = Bone("UpperArm.r"), body = Bone("Body");
        if (head < 0 || armL < 0 || armR < 0) return;
        Vector3 BonePos(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);

        var verts = new List<Vector3>(); src.GetVertices(verts);
        var normals = new List<Vector3>(); src.GetNormals(normals);
        var uvs = new List<Vector2>(); src.GetUVs(0, uvs);
        var weights = new List<BoneWeight>(src.boneWeights);
        bool IsHead(int v) => DominantBone(weights[v]) == head;
        Vector3 posL = BonePos(armL), posR = BonePos(armR);

        var bodyTris = new List<int>[src.subMeshCount];
        var helmTris = new List<int>();
        var rightCopy = new Dictionary<int, int>();
        for (int s = 0; s < src.subMeshCount; s++)
        {
            bodyTris[s] = new List<int>();
            int[] tris = src.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (!(IsHead(a) && IsHead(b) && IsHead(c))) { bodyTris[s].AddRange(new[] { a, b, c }); continue; }
                Vector3 centroid = (verts[a] + verts[b] + verts[c]) / 3f;
                bool right = (centroid - posR).sqrMagnitude < (centroid - posL).sqrMagnitude;
                foreach (int v in new[] { a, b, c })
                {
                    if (!right) { helmTris.Add(v); continue; }
                    if (!rightCopy.TryGetValue(v, out int nv))
                    {
                        nv = verts.Count;
                        verts.Add(verts[v]);
                        normals.Add(normals.Count > v ? normals[v] : Vector3.up);
                        uvs.Add(uvs[v] + RightHalfShift);
                        weights.Add(weights[v]);
                        rightCopy[v] = nv;
                    }
                    helmTris.Add(nv);
                }
            }
        }

        cleanMesh = new Mesh { name = "KnightPortrait" };
        cleanMesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        cleanMesh.SetVertices(verts);
        cleanMesh.SetNormals(normals);
        cleanMesh.SetUVs(0, uvs);
        cleanMesh.boneWeights = weights.ToArray();
        cleanMesh.bindposes = bind;
        cleanMesh.subMeshCount = src.subMeshCount + 1;
        for (int s = 0; s < src.subMeshCount; s++) cleanMesh.SetTriangles(bodyTris[s], s);
        cleanMesh.SetTriangles(helmTris, src.subMeshCount);
        cleanMesh.RecalculateBounds();
        owned.Add(cleanMesh);

        // Вмятина: вершина шлема слева-спереди-сверху (та же, что у текстуры) вдавлена к центру головы
        dentedMesh = Instantiate(cleanMesh);
        dentedMesh.name = "KnightPortraitDented";
        owned.Add(dentedMesh);
        Vector3 up = (BonePos(head) - BonePos(body >= 0 ? body : head)).normalized;
        if (up.sqrMagnitude < 0.5f) up = Vector3.up;
        Vector3 left = (posL - posR).normalized;
        Vector3 forward = Vector3.Cross(-left, up).normalized;
        var headVerts = new List<int>();
        for (int v = 0; v < weights.Count; v++) if (DominantBone(weights[v]) == head) headVerts.Add(v);
        Vector3 min = Vector3.one * 1e9f, max = -min;
        foreach (int v in headVerts)
        {
            Vector3 q = new Vector3(Vector3.Dot(verts[v], left), Vector3.Dot(verts[v], forward), Vector3.Dot(verts[v], up));
            min = Vector3.Min(min, q);
            max = Vector3.Max(max, q);
        }
        Vector3 c3 = (min + max) / 2f, half = (max - min) / 2f;
        Vector3 center = left * c3.x + forward * c3.y + up * c3.z;
        Vector3 want = center + left * (half.x * 0.8f) + forward * (half.y * 0.75f) + up * (half.z * 0.45f);
        int best = headVerts[0];
        foreach (int v in headVerts) if ((verts[v] - want).sqrMagnitude < (verts[best] - want).sqrMagnitude) best = v;
        Vector3 dentAt = verts[best];
        var dented = new List<Vector3>(verts);
        for (int v = 0; v < dented.Count; v++)
            if ((dented[v] - dentAt).sqrMagnitude < 1e-8f) dented[v] += (center - dentAt) * DentDepth;
        dentedMesh.SetVertices(dented);
        dentedMesh.RecalculateNormals();

        smr.sharedMesh = cleanMesh;
        smr.updateWhenOffscreen = true;
        var mats = new List<Material>(smr.sharedMaterials);
        while (mats.Count < src.subMeshCount) mats.Add(mats.Count > 0 ? mats[0] : null);
        Material baseMat = mats.Count > 0 && mats[0] != null ? mats[0] : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        helmetMat = new Material(baseMat) { name = "KnightHelmetPortrait" };
        owned.Add(helmetMat);
        mats.Add(helmetMat);
        smr.sharedMaterials = mats.ToArray();

        helmetStages = new Texture2D[5];
        for (int i = 0; i < 5; i++) helmetStages[i] = Resources.Load<Texture2D>(Res + "KnightHelmet_" + i);
    }

    private static int DominantBone(BoneWeight w)
    {
        int i = w.boneIndex0;
        float best = w.weight0;
        if (w.weight1 > best) { best = w.weight1; i = w.boneIndex1; }
        if (w.weight2 > best) { best = w.weight2; i = w.boneIndex2; }
        if (w.weight3 > best) i = w.boneIndex3;
        return i;
    }

    // ---------------- Уровни и анимация ----------------

    // 0 -- больше 90%, 1 -- до 90%, ..., 9 -- до 10%, 10 -- мёртв
    public static int LevelFor(float health, float maxHealth)
    {
        if (health <= 0f) return 10;
        float pct = health / Mathf.Max(1f, maxHealth) * 100f;
        return Mathf.Clamp(Mathf.FloorToInt((100f - pct + 0.001f) / 10f), 0, 9);
    }

    public int Level => level;

    private void OnDamaged(float amount)
    {
        hit = 1f;
        hitSide = Random.value < 0.5f ? -1f : 1f;
        Color c = flash.color;
        c.a = Mathf.Clamp(0.35f + amount * 0.01f, 0f, 0.65f);
        flash.color = c;
    }

    void LateUpdate()
    {
        int lvl = health != null ? LevelFor(health.Health, health.maxHealth) : 0;
        if (lvl != level)
        {
            if (level >= 0) shake = 1f;
            level = lvl;
            ApplyLevel();
        }

        float dt = Time.deltaTime, t = Time.time;
        hit = Mathf.MoveTowards(hit, 0f, dt * 2.5f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 2f);
        Color fc = flash.color;
        fc.a = Mathf.MoveTowards(fc.a, 0f, dt * 1.5f);
        flash.color = fc;

        if (level >= 10)
        {
            float k = Mathf.Clamp01((Time.time - deathTime) / 0.6f);
            skull.color = new Color(1f, 1f, 1f, k);
            skullBack.color = new Color(0f, 0f, 0f, Mathf.Clamp01(k * 2f));
            return;
        }

        AnimateHead(t);

        // Сердцебиение (20% и ниже): удар-удар, окно трясёт, портрет пульсирует; на 10% сердце чаще
        float beat = 0f;
        if (level >= 8)
        {
            float period = level >= 9 ? 0.55f : 0.75f;
            float ph = Mathf.Repeat(t, period) / period;
            beat = Mathf.Max(0f, 1f - Mathf.Abs(ph - 0.06f) / 0.1f) + 0.6f * Mathf.Max(0f, 1f - Mathf.Abs(ph - 0.32f) / 0.1f);
        }
        float shakePx = shake * 9f + beat * 4f + hit * 3f;
        content.anchoredPosition = Random.insideUnitCircle * shakePx;
        content.localScale = Vector3.one * (1f + 0.08f * beat);

        // Троится (10%)
        if (level >= 9)
        {
            float w = 10f + 4f * Mathf.Sin(t * 2.7f);
            ghostA.rectTransform.anchoredPosition = new Vector2(-w, 2f * Mathf.Sin(t * 3.3f));
            ghostB.rectTransform.anchoredPosition = new Vector2(w, -2f * Mathf.Sin(t * 2.1f));
        }
    }

    private void ApplyLevel()
    {
        bool dead = level >= 10;
        if (dead && deathTime < 0f) deathTime = Time.time;
        if (!dead) deathTime = -1f;

        content.gameObject.SetActive(!dead);
        skull.gameObject.SetActive(dead);
        skullBack.gameObject.SetActive(dead);
        flash.gameObject.SetActive(!dead);
        if (cam != null) cam.enabled = !dead;
        foreach (var b in blood)
        {
            b.img.gameObject.SetActive(!dead && level >= b.level);
            b.img.color = new Color(1f, 1f, 1f, b.alpha);
        }
        bool triple = level >= 9 && !dead && face.enabled;
        ghostA.gameObject.SetActive(triple);
        ghostB.gameObject.SetActive(triple);
        face.color = new Color(1f, 1f, 1f, triple ? 0.55f : 1f);   // три полупрозрачных рыцаря поверх друг друга

        if (smr != null && cleanMesh != null) smr.sharedMesh = level >= 3 ? dentedMesh : cleanMesh;
        if (helmetMat != null)
        {
            int stage = level >= 7 ? 4 : Mathf.Min(level, 3);
            Texture2D tex = helmetStages[stage];
            if (tex != null)
            {
                helmetMat.mainTexture = tex;
                if (helmetMat.HasProperty("_BaseMap")) helmetMat.SetTexture("_BaseMap", tex);
            }
        }
    }

    // Голова дышит и оглядывается; от удара дёргается назад; с 40% рыцаря покачивает и голова клонится
    private void AnimateHead(float t)
    {
        if (rig == null) return;
        Vector3 up = rig.transform.up, fwd = rig.transform.forward, right = rig.transform.right;
        bool sway = level >= 6;

        if (bodyBone != null)
        {
            bodyBone.localRotation = bodyRest;
            bodyBone.localPosition = bodyRestPos;
            float roll = sway ? Mathf.Sin(t * 1.1f) * 7f : 0f;
            float pitch = sway ? 4f + Mathf.Sin(t * 0.55f) * 3f : 0f;
            bodyBone.rotation = Quaternion.AngleAxis(roll, fwd) * Quaternion.AngleAxis(pitch, right) * bodyBone.rotation;
            bodyBone.position += up * (Mathf.Sin(t * 1.6f) * 0.004f);
        }
        if (headBone != null)
        {
            headBone.localRotation = headRest;
            float yaw = Mathf.Sin(t * 0.37f) * 10f + (Mathf.PerlinNoise(t * 0.25f, 0.3f) - 0.5f) * 16f;
            float pitch = Mathf.Sin(t * 1.6f) * 1.5f - hit * 16f + (sway ? 9f + Mathf.Sin(t * 0.8f) * 4f : 0f);
            float roll = hit * 10f * hitSide + (sway ? Mathf.Sin(t * 1.1f + 0.6f) * 6f : 0f);
            headBone.rotation = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right) *
                                Quaternion.AngleAxis(roll, fwd) * headBone.rotation;
        }
    }

    // ---------------- helpers ----------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        Image img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static RawImage NewRaw(string name, Transform parent, Color color)
    {
        RawImage img = NewRect(name, parent).gameObject.AddComponent<RawImage>();
        img.color = color;
        img.raycastTarget = false;
        Stretch(img.rectTransform);
        return img;
    }

    private static void Stretch(RectTransform r, float inset = 0f)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.one * inset;
        r.offsetMax = -Vector2.one * inset;
    }
}
