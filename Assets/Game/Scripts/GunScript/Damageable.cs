using UnityEngine;

// Простое здоровье для врагов/бочек/манекенов. Моргает красным при уроне,
// при смерти либо разваливается (отключает kinematic), либо удаляется.
public class Damageable : MonoBehaviour, IDamageable
{
    public float maxHealth = 100f;
    public bool destroyOnDeath = true;
    public float destroyDelay = 0f;
    public GameObject deathEffectPrefab;

    [Header("Hit flash")]
    public Color flashColor = Color.red;
    public float flashTime = 0.08f;

    // Доля урона, которая проходит (щит в блоке -- меньше 1, парирование -- 0)
    [HideInInspector] public float damageMultiplier = 1f;

    public float Health { get; private set; }
    public bool IsDead => Health <= 0f;

    // Последнее попадание (TakeHit): точка и толчок; ноль -- урон пришёл без направления (TakeDamage)
    public Vector3 LastHitPoint { get; private set; }
    public Vector3 LastHitImpulse { get; private set; }

    // Вызывается при каждом попадании (урон до множителя) -- например, чтобы щит дрогнул от удара
    public event System.Action<float> Damaged;

    // Вызывается один раз при смерти (до удаления) -- например, чтобы враг проиграл анимацию смерти
    public event System.Action Died;

    private Renderer[] renderers;
    private MaterialPropertyBlock mpb;
    private float flashTimer;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Health = maxHealth;
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    public void TakeDamage(float amount)
    {
        TakeHit(amount, transform.position, Vector3.zero);
    }

    // Урон с местом и направлением удара -- по ним, например, разлетаются кости скелета
    public void TakeHit(float amount, Vector3 point, Vector3 impulse)
    {
        if (IsDead) return;
        LastHitPoint = point;
        LastHitImpulse = impulse;

        Damaged?.Invoke(amount);
        amount *= damageMultiplier;
        if (amount <= 0f) return;
        Health -= amount;
        flashTimer = flashTime;
        SetFlash(true);

        if (Health <= 0f)
            Die();
    }

    void Update()
    {
        if (flashTimer <= 0f) return;
        flashTimer -= Time.deltaTime;
        if (flashTimer <= 0f) SetFlash(false);
    }

    private void SetFlash(bool on)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (on)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, flashColor);
                mpb.SetColor(ColorId, flashColor);
                r.SetPropertyBlock(mpb);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }

    private void Die()
    {
        Died?.Invoke();
        if (deathEffectPrefab != null)
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);

        if (destroyOnDeath)
        {
            Destroy(gameObject, destroyDelay);
            return;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
    }
}
