using System.Collections.Generic;
using UnityEngine;

// Меч ближнего боя. Анимация процедурная (без аниматора): пивот с моделью
// крутится по трём ключевым позам start -> mid -> end, удары чередуются справа/слева.
// Удар в три фазы: замах (меч уходит назад) -> удар (дуга, шлейф от клинка, свист) -> возврат в стойку
// с перехлёстом. Камеру толкает в сторону удара; при попадании по врагу -- стоп-кадр и толчок сильнее.
//
// Фишки:
//  - удар бьёт всех IDamageable в конусе перед камерой, каждого один раз за взмах;
//  - Rigidbody отлетают;
//  - "пого": если бьёшь вниз в воздухе и попал во что-то -- тебя подбрасывает вверх;
//  - зажатый удар (держишь attackKey дольше chargeTime) -- тяжёлый удар x2 урона и отброса.
//
// Иерархия:
//   WeaponHolder
//     Sword (Sword)
//       Pivot        <- swingPivot
//         Model
public class Sword : MonoBehaviour
{
    [Header("Input")]
    public KeyCode attackKey = KeyCode.Mouse0;

    [Header("Attack")]
    public float damage = 50f;
    public float attackRange = 2.6f;        // дальность от камеры
    public float attackRadius = 1.1f;       // толщина "взмаха"
    [Range(0f, 180f)] public float attackAngle = 70f; // конус от прицела
    public float attackCooldown = 0.38f;
    public float knockbackForce = 10f;
    public LayerMask hitMask = ~0;

    [Header("Charged attack (держать кнопку)")]
    public float chargeTime = 0.45f;        // сколько держать для тяжёлого удара
    public float chargedMultiplier = 2f;
    public Vector3 chargePoseEuler = new Vector3(-25f, 20f, -40f); // замах назад
    private float holdTimer;
    private bool holding;

    [Header("Pogo (удар вниз в воздухе)")]
    public float pogoVelocity = 11f;        // 0 -- выключено
    [Range(-1f, 0f)] public float pogoLookDownDot = -0.55f; // насколько вниз надо смотреть

    [Header("Hold pose (в покое): слева, клинок вертикально, плашмя к экрану")]
    public Vector3 holdPosition = new Vector3(-0.5f, -0.64f, 0.85f);   // относительно камеры; x < 0 -- меч слева
    public Vector3 holdEuler = Vector3.zero;                           // клинок по +Y, плоскость -- к экрану

    [Header("Swing animation")]
    public Transform swingPivot;
    public float swingDuration = 0.22f;
    [Range(0f, 1f)] public float hitMoment = 0.35f; // в какой доле взмаха проверяем попадание
    public AnimationCurve swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float returnSpeed = 12f;
    // Позы для удара справа налево; для обратного удара y/z зеркалятся
    public Vector3 startEuler = new Vector3(-10f, -35f, -75f);
    public Vector3 midEuler = new Vector3(70f, 0f, -10f);
    public Vector3 endEuler = new Vector3(40f, 45f, 70f);
    public Vector3 swingPosOffset = new Vector3(-0.15f, 0.05f, 0.15f); // куда сдвигается пивот к середине взмаха

    [Header("Feel (сочность удара)")]
    public float windupTime = 0.08f;                                   // замах перед ударом
    public Vector3 windupExtraEuler = new Vector3(-12f, -10f, -12f);   // насколько дальше start-позы уходит меч
    public float recoveryTime = 0.22f;                                 // возврат в стойку (с перехлёстом)
    public Vector2 swingKick = new Vector2(-0.5f, 1.4f);              // толчок камеры при ударе: pitch, yaw в сторону удара
    public Vector2 hitKick = new Vector2(1.2f, 0.9f);                 // толчок при попадании
    public float hitStopTime = 0.06f;                                 // стоп-кадр при попадании по врагу, сек (реальных)
    [Range(0f, 1f)] public float hitStopScale = 0.05f;

    [Header("Refs (найдутся сами)")]
    public Transform aimOrigin;
    public Transform owner;
    public Rigidbody playerBody;
    public KnightMovement movement;

    [Header("SFX / VFX (можно оставить пустым)")]
    public AudioSource audioSource;
    public AudioClip swingSound;
    public AudioClip hitSound;
    public GameObject hitEffectPrefab;
    public float hitEffectLifetime = 1f;

    private float nextAttackTime;
    private bool windingUp, recovering;
    private float phaseTimer;
    private Quaternion phaseFromRot;
    private Vector3 phaseFromPos;
    private bool swinging;
    private bool hitDone;
    private float swingTimer;
    private bool mirrored;
    private float currentMultiplier = 1f;

    private Vector3 pivotRestPos;
    private Quaternion pivotRestRot;
    private readonly Collider[] overlapBuffer = new Collider[32];
    private readonly HashSet<object> hitThisSwing = new HashSet<object>();
    private CameraPOV cameraPov;
    private SwordTrail trail;
    private AudioClip[] swingClips;
    private bool inHitStop;

    public bool IsSwinging => swinging;
    public bool IsCharging => holding;
    public float SwingProgress => swinging ? Mathf.Clamp01(swingTimer / swingDuration) : 0f;
    public bool SwingMirrored => mirrored;

    void Awake()
    {
        if (owner == null) owner = transform.root;
        if (aimOrigin == null)
        {
            Camera cam = GetComponentInParent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam != null) aimOrigin = cam.transform;
        }
        if (playerBody == null && owner != null) playerBody = owner.GetComponent<Rigidbody>();
        if (movement == null && owner != null) movement = owner.GetComponent<KnightMovement>();

        transform.localPosition = holdPosition;
        if (swingPivot != null)
        {
            swingPivot.localRotation = Quaternion.Euler(holdEuler);
            pivotRestPos = swingPivot.localPosition;
            pivotRestRot = swingPivot.localRotation;
        }
        mirrored = HeldLeft;   // первый удар -- со стороны, где держим меч

        if (aimOrigin != null) cameraPov = aimOrigin.GetComponent<CameraPOV>();
        if (swingPivot != null) trail = SwordTrail.Attach(transform, swingPivot);
        swingClips = swingSound != null ? new[] { swingSound } : Resources.LoadAll<AudioClip>("Sounds/Player/SwordSwing");
        if (audioSource == null && swingClips.Length > 0) audioSource = gameObject.AddComponent<AudioSource>();
    }

    private bool HeldLeft => holdPosition.x < 0f;

    void OnDisable()
    {
        swinging = windingUp = recovering = false;
        if (trail != null) trail.Emitting = false;
        if (inHitStop) { Time.timeScale = 1f; inHitStop = false; }
        holding = false;
        holdTimer = 0f;
        if (swingPivot != null)
        {
            swingPivot.localPosition = pivotRestPos;
            swingPivot.localRotation = pivotRestRot;
        }
    }

    void Update()
    {
        HandleInput();
        UpdateSwing();
    }

    private void HandleInput()
    {
        if (PlayerInputLock.Locked) { holding = false; return; }
        if (swinging || windingUp) return;

        if (Input.GetKeyDown(attackKey) && Time.time >= nextAttackTime)
        {
            holding = true;
            holdTimer = 0f;
        }

        if (!holding) return;

        holdTimer += Time.deltaTime;
        if (Input.GetKeyUp(attackKey) || !Input.GetKey(attackKey))
        {
            holding = false;
            StartSwing(holdTimer >= chargeTime ? chargedMultiplier : 1f);
        }
    }

    private void StartSwing(float multiplier)
    {
        windingUp = true;
        recovering = false;
        phaseTimer = 0f;
        phaseFromRot = swingPivot != null ? swingPivot.localRotation : Quaternion.identity;
        phaseFromPos = swingPivot != null ? swingPivot.localPosition : Vector3.zero;
        hitDone = false;
        swingTimer = 0f;
        currentMultiplier = multiplier;
        hitThisSwing.Clear();
        nextAttackTime = Time.time + attackCooldown;
        if (swingPivot == null) BeginStrike();
    }

    // Замах закончился -- пошёл сам удар: свист, толчок камеры, шлейф
    private void BeginStrike()
    {
        windingUp = false;
        swinging = true;
        swingTimer = 0f;
        float side = mirrored ? 1f : -1f;   // слева направо -- камеру ведёт вправо
        if (cameraPov != null) cameraPov.AddRecoil(swingKick.x * currentMultiplier, swingKick.y * side * currentMultiplier);
        if (trail != null) trail.Emitting = true;
        if (audioSource != null && swingClips != null && swingClips.Length > 0)
        {
            audioSource.pitch = Random.Range(0.92f, 1.08f) / (currentMultiplier > 1f ? 1.15f : 1f);   // тяжёлый -- ниже
            audioSource.PlayOneShot(swingClips[Random.Range(0, swingClips.Length)], currentMultiplier > 1f ? 1f : 0.75f);
        }
    }

    private Quaternion StartPose => Quaternion.Euler(Mirror(startEuler));
    private Quaternion CockedPose => StartPose * Quaternion.Euler(Mirror(windupExtraEuler));

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.7f, c3 = c1 + 1f;
        x -= 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private void UpdateSwing()
    {
        if (swingPivot == null) return;

        if (windingUp)
        {
            phaseTimer += Time.deltaTime;
            float w = EaseOut(Mathf.Clamp01(phaseTimer / Mathf.Max(0.01f, windupTime)));
            Vector3 back = (mirrored ? new Vector3(-swingPosOffset.x, swingPosOffset.y, swingPosOffset.z) : swingPosOffset) * -0.35f;
            swingPivot.localRotation = Quaternion.Slerp(phaseFromRot, pivotRestRot * CockedPose, w);
            swingPivot.localPosition = Vector3.Lerp(phaseFromPos, pivotRestPos + back, w);
            if (phaseTimer >= windupTime) BeginStrike();
            return;
        }

        if (recovering)
        {
            phaseTimer += Time.deltaTime;
            float r = Mathf.Clamp01(phaseTimer / Mathf.Max(0.01f, recoveryTime));
            float e = EaseOutBack(r);   // проскакивает стойку и возвращается
            swingPivot.localRotation = Quaternion.SlerpUnclamped(phaseFromRot, pivotRestRot, e);
            swingPivot.localPosition = Vector3.LerpUnclamped(phaseFromPos, pivotRestPos, e);
            if (r >= 1f) recovering = false;
            return;
        }

        if (!swinging)
        {
            Quaternion targetRot = pivotRestRot;
            // Замах назад, пока держим кнопку -- видно, что копится тяжёлый удар
            if (holding)
            {
                // меч слева -- замах тоже зеркальный, через левое плечо
                Vector3 charge = HeldLeft ? new Vector3(chargePoseEuler.x, -chargePoseEuler.y, -chargePoseEuler.z) : chargePoseEuler;
                targetRot = pivotRestRot * Quaternion.Euler(Vector3.Lerp(Vector3.zero, charge, Mathf.Clamp01(holdTimer / chargeTime)));
            }

            swingPivot.localRotation = Quaternion.Slerp(swingPivot.localRotation, targetRot, Time.deltaTime * returnSpeed);
            swingPivot.localPosition = Vector3.Lerp(swingPivot.localPosition, pivotRestPos, Time.deltaTime * returnSpeed);
            return;
        }

        swingTimer += Time.deltaTime;
        float t = Mathf.Clamp01(swingTimer / swingDuration);
        float k = swingCurve.Evaluate(t);

        Quaternion a = CockedPose;   // удар начинается из замаха
        Quaternion b = Quaternion.Euler(Mirror(midEuler));
        Quaternion c = Quaternion.Euler(Mirror(endEuler));
        Quaternion rot = k < 0.5f ? Quaternion.Slerp(a, b, k * 2f) : Quaternion.Slerp(b, c, (k - 0.5f) * 2f);
        swingPivot.localRotation = pivotRestRot * rot;

        Vector3 offset = mirrored ? new Vector3(-swingPosOffset.x, swingPosOffset.y, swingPosOffset.z) : swingPosOffset;
        swingPivot.localPosition = pivotRestPos + offset * Mathf.Sin(k * Mathf.PI);

        if (!hitDone && t >= hitMoment)
        {
            hitDone = true;
            DoHit();
        }

        if (t >= 1f)
        {
            swinging = false;
            if (trail != null) trail.Emitting = false;
            recovering = true;
            phaseTimer = 0f;
            phaseFromRot = swingPivot.localRotation;
            phaseFromPos = swingPivot.localPosition;
            mirrored = !mirrored; // следующий удар в другую сторону
        }
    }

    private Vector3 Mirror(Vector3 e) => mirrored ? new Vector3(e.x, -e.y, -e.z) : e;

    private void DoHit()
    {
        if (aimOrigin == null) return;

        Vector3 origin = aimOrigin.position;
        Vector3 forward = aimOrigin.forward;
        Vector3 center = origin + forward * (attackRange * 0.5f);
        float radius = Mathf.Max(attackRadius, attackRange * 0.5f);

        int count = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
        bool hitAnything = false, hitEnemy = false;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (owner != null && col.transform.IsChildOf(owner)) continue;

            Vector3 closest = SupportsClosestPoint(col) ? col.ClosestPoint(origin) : col.bounds.ClosestPoint(origin);
            Vector3 toTarget = closest - origin;
            float dist = toTarget.magnitude;
            if (dist > attackRange) continue;
            if (dist > 0.05f && Vector3.Angle(forward, toTarget) > attackAngle) continue;

            hitAnything = true;

            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable != null && hitThisSwing.Add(damageable))
            {
                hitEnemy = true;
                if (damageable is Damageable target)
                    target.TakeHit(damage * currentMultiplier, closest, forward * knockbackForce * currentMultiplier);
                else
                    damageable.TakeDamage(damage * currentMultiplier);
            }

            Rigidbody rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic && hitThisSwing.Add(rb))
                rb.AddForce((forward + Vector3.up * 0.3f).normalized * knockbackForce * currentMultiplier, ForceMode.Impulse);

            if (hitEffectPrefab != null)
                Destroy(Instantiate(hitEffectPrefab, closest, Quaternion.LookRotation(-forward)), hitEffectLifetime);
        }

        if (hitAnything)
        {
            if (audioSource != null && hitSound != null)
                audioSource.PlayOneShot(hitSound);
            TryPogo(forward);
        }
        if (hitEnemy)
        {
            float side = mirrored ? 1f : -1f;
            if (cameraPov != null) cameraPov.AddRecoil(hitKick.x * currentMultiplier, hitKick.y * side);
            if (hitStopTime > 0f && !inHitStop && Mathf.Approximately(Time.timeScale, 1f))
                StartCoroutine(HitStop(hitStopTime * (currentMultiplier > 1f ? 1.6f : 1f)));
        }
    }

    // Стоп-кадр: на мгновение почти останавливаем время -- удар "вязнет" в цели
    private System.Collections.IEnumerator HitStop(float seconds)
    {
        inHitStop = true;
        Time.timeScale = hitStopScale;
        yield return new WaitForSecondsRealtime(seconds);
        if (inHitStop && Mathf.Approximately(Time.timeScale, hitStopScale)) Time.timeScale = 1f;
        inHitStop = false;
    }

    // ClosestPoint не работает с невыпуклыми MeshCollider и террейном -- для них берём bounds
    private static bool SupportsClosestPoint(Collider col)
    {
        if (col is MeshCollider mc) return mc.convex;
        return col is BoxCollider || col is SphereCollider || col is CapsuleCollider;
    }

    private void TryPogo(Vector3 forward)
    {
        if (pogoVelocity <= 0f || playerBody == null) return;
        if (forward.y > pogoLookDownDot) return;
        if (movement != null && movement.IsGrounded()) return;

        Vector3 v = playerBody.linearVelocity;
        playerBody.linearVelocity = new Vector3(v.x, Mathf.Max(v.y, pogoVelocity), v.z);
    }

    void OnDrawGizmosSelected()
    {
        Transform o = aimOrigin != null ? aimOrigin : transform;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(o.position + o.forward * (attackRange * 0.5f), Mathf.Max(attackRadius, attackRange * 0.5f));
    }
}
