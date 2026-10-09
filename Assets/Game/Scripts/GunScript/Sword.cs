using System.Collections.Generic;
using UnityEngine;

// Меч ближнего боя. Анимация процедурная (без аниматора): пивот с моделью
// крутится по трём ключевым позам start -> mid -> end, удары чередуются справа/слева.
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
    private bool swinging;
    private bool hitDone;
    private float swingTimer;
    private bool mirrored;
    private float currentMultiplier = 1f;

    private Vector3 pivotRestPos;
    private Quaternion pivotRestRot;
    private readonly Collider[] overlapBuffer = new Collider[32];
    private readonly HashSet<object> hitThisSwing = new HashSet<object>();

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

        if (swingPivot != null)
        {
            pivotRestPos = swingPivot.localPosition;
            pivotRestRot = swingPivot.localRotation;
        }
    }

    void OnDisable()
    {
        swinging = false;
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
        if (swinging) return;

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
        swinging = true;
        hitDone = false;
        swingTimer = 0f;
        currentMultiplier = multiplier;
        hitThisSwing.Clear();
        nextAttackTime = Time.time + attackCooldown;

        if (audioSource != null && swingSound != null)
            audioSource.PlayOneShot(swingSound, multiplier > 1f ? 1f : 0.8f);
    }

    private void UpdateSwing()
    {
        if (swingPivot == null) return;

        if (!swinging)
        {
            Quaternion targetRot = pivotRestRot;
            // Замах назад, пока держим кнопку -- видно, что копится тяжёлый удар
            if (holding)
                targetRot = pivotRestRot * Quaternion.Euler(Vector3.Lerp(Vector3.zero, chargePoseEuler, Mathf.Clamp01(holdTimer / chargeTime)));

            swingPivot.localRotation = Quaternion.Slerp(swingPivot.localRotation, targetRot, Time.deltaTime * returnSpeed);
            swingPivot.localPosition = Vector3.Lerp(swingPivot.localPosition, pivotRestPos, Time.deltaTime * returnSpeed);
            return;
        }

        swingTimer += Time.deltaTime;
        float t = Mathf.Clamp01(swingTimer / swingDuration);
        float k = swingCurve.Evaluate(t);

        Quaternion a = Quaternion.Euler(Mirror(startEuler));
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
        bool hitAnything = false;

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
                damageable.TakeDamage(damage * currentMultiplier);

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
