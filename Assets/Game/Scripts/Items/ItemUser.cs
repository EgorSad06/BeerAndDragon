using System.Collections.Generic;
using UnityEngine;

// Анимация использования расходников (вешается на Player).
//  Smoke (сигареты): подносим ко рту -> чирк зажигалкой, искры, огонёк загорается ->
//                    затяжки (smokeDrags штук): огонёк разгорается, табак трещит, голова чуть вверх;
//                    после каждой -- отводим сигарету, дым изо рта и двумя струйками из носа ->
//                    большой выдох облаком -> эффект -> щелчком отправляем окурок в полёт.
//                    Звуки синтезированы: Resources/Sounds/Player/Smoke (tools/audio/smoke_sounds.py).
//  Drink (энергетик): подносим, запрокидываем и пьём залпом -> эффект -> пустая банка летит прочь.
// Эффект и списание предмета -- через item.useTime секунд после нажатия (по умолчанию 1 сек).
// Пока идёт анимация, оружие убрано; от 1-го лица предмет висит перед камерой,
// от 3-го -- в руке рыцаря (KnightAnimator подносит руку ко рту).
public class ItemUser : MonoBehaviour
{
    [Header("Refs (найдутся сами)")]
    public Inventory inventory;
    public PlayerHealth health;
    public WeaponSwitcher weaponSwitcher;
    public CameraPOV look;
    public SkaterController skater;
    public ThirdPersonCamera thirdPersonCamera;
    public KnightAnimator knight;

    [Header("Materials (их создаёт Setup; если пусто -- сделаются в рантайме)")]
    public Material smokeMaterial;
    public Material emberMaterial;

    [Header("Sounds (пусто -- загрузится из Resources/Sounds/Player)")]
    public AudioClip drinkSound;
    [Range(0f, 1f)] public float drinkVolume = 0.8f;
    public AudioClip lighterSound, inhaleSound, exhaleSound, exhaleLongSound;
    [Range(0f, 1f)] public float smokeVolume = 0.7f;

    [Header("Курение")]
    [Range(1, 4)] public int smokeDrags = 2;        // затяжек до большого выдоха
    public float dragHeadTilt = 0.8f;               // насколько голова уходит вверх на затяжке, град

    [Header("Timing")]
    public float putAwayTime = 0.35f;       // после эффекта -- опускаем руку и выбрасываем

    [Header("Первое лицо: позы предмета относительно камеры")]
    public Vector3 restPos = new Vector3(0.2f, -0.55f, 0.3f);
    public Vector3 cigMouthPos = new Vector3(0.015f, -0.035f, 0.12f);   // у самых губ, чуть ниже прицела
    public Vector3 cigMouthEuler = new Vector3(8f, 55f, 0f);            // торчит вбок -- видно в кадре
    public Vector3 cigExhalePos = new Vector3(0.14f, -0.09f, 0.25f);
    public Vector3 cigExhaleEuler = new Vector3(15f, 40f, 0f);
    public Vector3 canMouthPos = new Vector3(0.02f, -0.06f, 0.13f);
    public float canSipTilt = -70f;
    public float canChugTilt = -125f;

    public bool IsUsing => item != null;
    public ItemDefinition CurrentItem => item;
    public bool IsDrinking => item != null && item.useStyle == ItemDefinition.UseStyle.Drink;
    public float MouthWeight { get; private set; }  // 0 -- рука внизу, 1 -- у рта
    public float DrinkTilt { get; private set; }    // 0..1 насколько запрокинули банку

    private ItemDefinition item;
    private int smokeEvents;                     // какие моменты курения уже случились (биты)
    private int slotIndex;
    private float startTime;
    private bool effectDone;
    private string savedWeapon;

    private GameObject propFP, propTP;
    private Transform emberFP, emberTP;
    private ParticleSystem wispFP, wispTP;
    private readonly List<Material> runtimeMats = new List<Material>();
    private AudioSource audioSource;

    void Awake()
    {
        if (drinkSound == null) drinkSound = Resources.Load<AudioClip>("Sounds/Player/drink_slurp");
        if (lighterSound == null) lighterSound = Resources.Load<AudioClip>("Sounds/Player/Smoke/lighter");
        if (inhaleSound == null) inhaleSound = Resources.Load<AudioClip>("Sounds/Player/Smoke/inhale");
        if (exhaleSound == null) exhaleSound = Resources.Load<AudioClip>("Sounds/Player/Smoke/exhale");
        if (exhaleLongSound == null) exhaleLongSound = Resources.Load<AudioClip>("Sounds/Player/Smoke/exhale_long");
        if (inventory == null) inventory = GetComponent<Inventory>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (weaponSwitcher == null) weaponSwitcher = GetComponentInChildren<WeaponSwitcher>(true);
        if (look == null) look = GetComponentInChildren<CameraPOV>();
        if (skater == null) skater = GetComponent<SkaterController>();
        if (thirdPersonCamera == null) thirdPersonCamera = GetComponent<ThirdPersonCamera>();
        if (knight == null) knight = GetComponentInChildren<KnightAnimator>();
    }

    void OnDestroy()
    {
        foreach (Material m in runtimeMats) if (m != null) Destroy(m);
    }

    private bool ThirdPerson =>
        (skater != null && skater.IsRiding && skater.thirdPerson) ||
        (thirdPersonCamera != null && thirdPersonCamera.thirdPerson);

    // ================= Старт / конец =================

    public bool Begin(ItemDefinition what, int index)
    {
        if (IsUsing || what == null || look == null) return false;

        item = what;
        slotIndex = index;
        startTime = Time.time;
        effectDone = false;
        smokeEvents = 0;
        MouthWeight = 0f;
        DrinkTilt = 0f;

        // Оружие убираем на время анимации
        savedWeapon = weaponSwitcher != null ? weaponSwitcher.CurrentName : null;
        if (savedWeapon != null) weaponSwitcher.Equip(null);

        if (item.heldPrefab != null)
        {
            propFP = Instantiate(item.heldPrefab, look.transform);
            propFP.transform.localPosition = restPos;
            propFP.transform.localRotation = Quaternion.identity;
            SetNoShadows(propFP);

            propTP = Instantiate(item.heldPrefab);
            if (item.useStyle == ItemDefinition.UseStyle.Smoke)
            {
                emberFP = MakeEmber(propFP.transform);
                emberTP = MakeEmber(propTP.transform);
                wispFP = MakeSmoke(emberFP, false);
                wispTP = MakeSmoke(emberTP, false);
            }
        }
        UpdateVisibility();
        if (item.useStyle == ItemDefinition.UseStyle.Drink) PlayDrink();
        return true;
    }

    // Глотки энергетика -- звук идёт, пока банка у рта
    private void PlayDrink() => Play(drinkSound, drinkVolume);

    private void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }
        audioSource.pitch = Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(clip, volume);
    }

    // Событие курения срабатывает один раз за сигарету
    private bool Once(int bit)
    {
        if ((smokeEvents & (1 << bit)) != 0) return false;
        smokeEvents |= 1 << bit;
        return true;
    }

    private void Finish(bool thrown)
    {
        if (thrown && propFP != null)
        {
            GameObject source = ThirdPerson ? propTP : propFP;
            if (source != null) Throw(source);
        }
        if (propFP != null) Destroy(propFP);
        if (propTP != null) Destroy(propTP);
        propFP = propTP = null;
        emberFP = emberTP = null;
        wispFP = wispTP = null;

        // Достаём оружие обратно (если за это время не сели на скейт и не достали другое)
        bool riding = skater != null && skater.IsRiding;
        if (savedWeapon != null && weaponSwitcher != null && weaponSwitcher.CurrentName == null && !riding)
            weaponSwitcher.Equip(savedWeapon);

        item = null;
        MouthWeight = 0f;
        DrinkTilt = 0f;
    }

    // ================= Анимация =================

    void LateUpdate()
    {
        if (!IsUsing) return;

        if (health != null && health.IsDead)
        {
            Finish(false); // умер с сигаретой в зубах -- предмет не тратится
            return;
        }

        float t = Time.time - startTime;
        float use = Mathf.Max(0.2f, item.useTime);

        if (!effectDone && t >= use)
        {
            effectDone = true;
            if (item.useStyle == ItemDefinition.UseStyle.Smoke) Exhale();
            if (inventory != null) inventory.CompleteUse(item, slotIndex);
        }
        if (t >= use + putAwayTime)
        {
            Finish(true);
            return;
        }

        if (item.useStyle == ItemDefinition.UseStyle.Drink) AnimateDrink(t, use);
        else AnimateSmoke(t, use);

        UpdateVisibility();
        UpdateThirdPersonProp();
    }

    // Доли времени use: поднести 0..0.12, прикурить 0.12..0.2, затяжки 0.2..0.9, держим у груди 0.9..1
    private const float SmokeRaise = 0.12f, SmokeLit = 0.2f, SmokeDragsEnd = 0.9f;

    private void AnimateSmoke(float t, float use)
    {
        float u = t / use;
        Quaternion mouthRot = Quaternion.Euler(cigMouthEuler), awayRot = Quaternion.Euler(cigExhaleEuler);
        Vector3 pos;
        Quaternion rot;
        float glow;

        if (u < SmokeRaise)
        {
            float k = Ease(u / SmokeRaise);
            pos = Vector3.Lerp(restPos, cigMouthPos, k);
            rot = Quaternion.Slerp(awayRot, mouthRot, k);
            MouthWeight = k;
            glow = 0f;                                   // ещё не горит
        }
        else if (u < SmokeLit)
        {
            if (Once(0))                                 // чирк зажигалкой
            {
                Play(lighterSound, smokeVolume);
                Sparks(ThirdPerson ? emberTP : emberFP);
            }
            float k = (u - SmokeRaise) / (SmokeLit - SmokeRaise);
            pos = cigMouthPos;
            rot = mouthRot;
            MouthWeight = 1f;
            glow = Mathf.Lerp(0.2f, 0.7f, k) + Mathf.Sin(k * 40f) * 0.15f;   // занимается, мерцая
        }
        else if (u < SmokeDragsEnd)
        {
            // Затяжка: у рта (огонёк разгорается) -> отвести -> выдох изо рта и носа -> обратно ко рту
            int drags = Mathf.Max(1, smokeDrags);
            float span = (SmokeDragsEnd - SmokeLit) / drags;
            int d = Mathf.Min(drags - 1, (int)((u - SmokeLit) / span));
            float k = (u - SmokeLit - d * span) / span;
            bool last = d == drags - 1;
            if (k < 0.5f)
            {
                if (Once(1 + d * 2))
                {
                    Play(inhaleSound, smokeVolume * 0.8f);
                    if (look != null && !ThirdPerson) look.AddRecoil(dragHeadTilt, 0f);
                }
                float q = k / 0.5f;
                pos = cigMouthPos + new Vector3(0f, 0f, -0.012f * Mathf.Sin(q * Mathf.PI));
                rot = mouthRot;
                MouthWeight = 1f;
                glow = Mathf.Lerp(0.45f, 1f, q);
            }
            else
            {
                if (!last && Once(2 + d * 2))               // последнюю затяжку выдыхаем большим облаком
                {
                    Play(exhaleSound, smokeVolume);
                    Puff();
                }
                float q = (k - 0.5f) / 0.5f;
                float away = last ? Ease(Mathf.Clamp01(q * 1.6f)) : Mathf.Sin(q * Mathf.PI);   // отвели и вернули
                pos = Vector3.Lerp(cigMouthPos, cigExhalePos, away);
                rot = Quaternion.Slerp(mouthRot, awayRot, away);
                MouthWeight = 1f - away * 0.6f;
                glow = Mathf.Lerp(1f, 0.5f, q);
            }
        }
        else if (u < 1f)
        {
            pos = cigExhalePos;
            rot = awayRot;
            MouthWeight = 0.4f;
            glow = 0.5f;
        }
        else
        {
            float k = Ease((t - use) / putAwayTime);
            pos = Vector3.Lerp(cigExhalePos, restPos, k);
            rot = awayRot;
            MouthWeight = 0.4f * (1f - k);
            glow = 0.5f;
        }

        if (propFP != null) propFP.transform.SetLocalPositionAndRotation(pos, rot);
        SetGlow(emberFP, glow);
        SetGlow(emberTP, glow);
    }

    private void AnimateDrink(float t, float use)
    {
        float raise = use * 0.25f, chugEnd = use * 0.9f;
        Vector3 pos;
        float tilt;

        if (t < raise)
        {
            float k = Ease(t / raise);
            pos = Vector3.Lerp(restPos, canMouthPos, k);
            tilt = Mathf.Lerp(0f, canSipTilt, k);
            MouthWeight = k;
        }
        else if (t < chugEnd)
        {
            float k = (t - raise) / (chugEnd - raise);
            pos = canMouthPos + Vector3.up * (Mathf.Sin(k * Mathf.PI * 6f) * 0.004f); // глотки
            tilt = Mathf.Lerp(canSipTilt, canChugTilt, Ease(k));
            MouthWeight = 1f;
        }
        else if (t < use)
        {
            pos = canMouthPos;
            tilt = canChugTilt;
            MouthWeight = 1f;
        }
        else
        {
            float k = Ease((t - use) / putAwayTime);
            pos = Vector3.Lerp(canMouthPos, restPos + new Vector3(0.1f, 0.2f, 0.1f), k);
            tilt = Mathf.Lerp(canChugTilt, -20f, k);
            MouthWeight = 1f - k;
        }

        DrinkTilt = Mathf.InverseLerp(0f, canChugTilt, tilt);
        if (propFP != null) propFP.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(tilt, 0f, 0f));
    }

    private static float Ease(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ================= Третье лицо =================

    private void UpdateVisibility()
    {
        bool tp = ThirdPerson;
        if (propFP != null && propFP.activeSelf == tp) propFP.SetActive(!tp);
        if (propTP != null && propTP.activeSelf != tp) propTP.SetActive(tp);
    }

    private void UpdateThirdPersonProp()
    {
        if (propTP == null || !propTP.activeSelf) return;
        Transform hand = knight != null ? knight.HandBone : null;
        Transform charRoot = knight != null ? knight.transform : transform;
        Vector3 at = hand != null ? hand.position : transform.position + Vector3.up * 0.5f;
        Vector3 fwd = charRoot.forward, up = charRoot.up;

        Quaternion rot = item.useStyle == ItemDefinition.UseStyle.Drink
            ? Quaternion.LookRotation(fwd, up) * Quaternion.Euler(Mathf.Lerp(0f, canChugTilt, DrinkTilt) * MouthWeight, 0f, 0f)
            : Quaternion.LookRotation(fwd, up) * Quaternion.Euler(10f, 0f, 0f);
        propTP.transform.SetPositionAndRotation(at + up * 0.05f, rot);
    }

    // ================= Эффекты =================

    // Рот и направление «вперёд» для дыма: голова рыцаря от 3-го лица или камера от 1-го
    private void MouthPoint(out Vector3 pos, out Vector3 dir, out Vector3 up)
    {
        if (ThirdPerson && knight != null && knight.HeadBone != null)
        {
            pos = knight.HeadBone.position + knight.transform.forward * 0.25f;
            dir = knight.transform.forward;
            up = knight.transform.up;
        }
        else
        {
            Transform cam = look.transform;
            pos = cam.position + cam.forward * 0.3f - cam.up * 0.08f;
            dir = cam.forward;
            up = cam.up;
        }
    }

    // Выдох после затяжки: небольшое облачко изо рта и две тонкие струйки из носа вниз
    private void Puff()
    {
        MouthPoint(out Vector3 pos, out Vector3 dir, out Vector3 up);
        Vector3 side = Vector3.Cross(up, dir).normalized;
        SmokeAt(pos, dir, 0.5f);
        foreach (float s in new[] { -1f, 1f })
            SmokeAt(pos + up * 0.05f + side * (0.012f * s), (dir - up * 0.9f + side * (0.15f * s)).normalized, 0.25f);
    }

    private void SmokeAt(Vector3 pos, Vector3 dir, float size)
    {
        GameObject go = new GameObject("PuffSmoke");
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        MakeSmoke(go.transform, true, size);
        Destroy(go, 4f);
    }

    // Искры от зажигалки у кончика сигареты
    private void Sparks(Transform at)
    {
        if (at == null) return;
        ParticleSystem ps = MakeSmoke(at, true, 0.15f);
        var main = ps.main;
        main.startColor = new Color(1f, 0.65f, 0.2f, 1f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.gravityModifier = 0.6f;
        var col = ps.colorOverLifetime;
        col.enabled = false;
        Destroy(ps.gameObject, 1f);
    }

    private void Exhale()
    {
        Play(exhaleLongSound, smokeVolume);
        MouthPoint(out Vector3 pos, out Vector3 dir, out _);
        SmokeAt(pos, dir, 1f);
    }

    private void Throw(GameObject source)
    {
        GameObject junk = Instantiate(item.heldPrefab, source.transform.position, source.transform.rotation);
        junk.name = item.useStyle == ItemDefinition.UseStyle.Drink ? "EmptyCan" : "CigaretteButt";

        Bounds local = LocalBounds(junk);
        BoxCollider box = junk.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = Vector3.Max(local.size, Vector3.one * 0.015f);
        Rigidbody rb = junk.AddComponent<Rigidbody>();
        rb.mass = 0.1f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        foreach (Collider c in GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(box, c);

        Transform view = look != null ? look.transform : transform;
        Vector3 v = view.forward * 5f + view.right * 1.5f + Vector3.up * 2.5f;
        Rigidbody player = GetComponent<Rigidbody>();
        if (player != null) v += player.linearVelocity;
        rb.linearVelocity = v;
        rb.angularVelocity = Random.insideUnitSphere * 15f;
        Destroy(junk, 10f);
    }

    private static Bounds LocalBounds(GameObject go)
    {
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    private static void SetNoShadows(GameObject go)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Огонёк сигареты: маленький кубик на конце (точка Tip в префабе)
    private Transform MakeEmber(Transform prop)
    {
        Transform tip = prop.Find("Tip");
        GameObject e = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(e.GetComponent<Collider>());
        e.name = "Ember";
        e.transform.SetParent(tip != null ? tip : prop, false);
        e.transform.localScale = Vector3.one * 0.008f;
        Renderer r = e.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (emberMaterial == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null)
            {
                emberMaterial = new Material(s);
                runtimeMats.Add(emberMaterial);
            }
        }
        if (emberMaterial != null) r.sharedMaterial = emberMaterial;
        return e.transform;
    }

    private static void SetGlow(Transform ember, float glow)
    {
        if (ember == null) return;
        float flicker = 1f + Mathf.Sin(Time.time * 40f) * 0.1f * glow;
        ember.localScale = Vector3.one * (0.006f + 0.006f * glow) * flicker;
        Renderer r = ember.GetComponent<Renderer>();
        if (r != null)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", Color.Lerp(new Color(0.5f, 0.1f, 0.02f), new Color(1f, 0.6f, 0.15f), glow));
            r.SetPropertyBlock(mpb);
        }
    }

    // Мягкое круглое пятно для частиц дыма
    public static Texture2D MakeSoftCircle(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    // URP Particles/Unlit по умолчанию непрозрачный -- переводим в обычную альфа-прозрачность
    public static void SetupTransparent(Material m)
    {
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // burst = облако при выдохе, иначе -- тонкий дымок с кончика сигареты
    // scale -- масштаб облака (1 -- большой выдох): меньше частиц, мельче и медленнее
    private ParticleSystem MakeSmoke(Transform parent, bool burst, float scale = 1f)
    {
        GameObject go = new GameObject(burst ? "Puff" : "Wisp");
        go.transform.SetParent(parent, false);
        if (!burst) go.transform.rotation = Quaternion.LookRotation(Vector3.up);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = burst ? 0.5f : 1f;
        main.loop = !burst;
        main.startLifetime = burst ? new ParticleSystem.MinMaxCurve(1.2f, 2.2f) : new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        float sp = Mathf.Lerp(0.6f, 1f, scale);
        main.startSpeed = burst ? new ParticleSystem.MinMaxCurve(0.6f * sp, 1.6f * sp) : new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startSize = burst ? new ParticleSystem.MinMaxCurve(0.12f * scale, 0.3f * scale) : new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new Color(0.85f, 0.85f, 0.85f, burst ? 0.55f : 0.35f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.03f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.rateOverTime = burst ? 0f : 14f;
        if (burst) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(4, Mathf.RoundToInt(28 * scale))) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = burst ? 18f : 5f;
        shape.radius = burst ? 0.03f : 0.003f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        if (smokeMaterial == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s != null)
            {
                smokeMaterial = new Material(s);
                Texture2D tex = MakeSoftCircle(64);
                smokeMaterial.SetTexture("_BaseMap", tex);
                SetupTransparent(smokeMaterial);
                runtimeMats.Add(smokeMaterial);
            }
        }
        pr.sharedMaterial = smokeMaterial;
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ps.Play();
        return ps;
    }
}
