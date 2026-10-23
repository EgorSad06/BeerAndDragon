using UnityEngine;

// Стрела скелета-лучника: летит с гравитацией, проверяет попадание лучом от наконечника.
// По IDamageable (игрок) -- урон и исчезает; в стену или пол -- втыкается и исчезает через stuckTime.
// Других скелетов не ранит. Модель -- Arrow.fbx: хвост в начале координат, наконечник по +Z.
public class SkeletonArrow : MonoBehaviour
{
    public float gravity = 9.81f;
    public float length = 0.78f;       // от хвоста до наконечника
    public float lifeTime = 6f;
    public float stuckTime = 8f;
    public LayerMask hitMask = ~0;

    private Vector3 velocity;
    private float damage;
    private Transform owner;
    private bool stuck;
    private float dieAt;

    public void Launch(Vector3 startVelocity, float hitDamage, Transform shooter)
    {
        velocity = startVelocity;
        damage = hitDamage;
        owner = shooter;
        dieAt = Time.time + lifeTime;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    void Update()
    {
        if (Time.time > dieAt)
        {
            Destroy(gameObject);
            return;
        }
        if (stuck) return;

        velocity += Vector3.down * (gravity * Time.deltaTime);
        Vector3 step = velocity * Time.deltaTime;
        Vector3 tip = transform.position + transform.forward * length;
        if (TryHit(tip, step, out RaycastHit hit))
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                target.TakeDamage(damage);
                Destroy(gameObject);
                return;
            }
            // воткнуться: наконечник чуть в поверхности
            transform.position = hit.point + step.normalized * 0.06f - transform.forward * length;
            transform.SetParent(hit.collider.transform, true);
            stuck = true;
            dieAt = Time.time + stuckTime;
            return;
        }
        transform.position += step;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    private bool TryHit(Vector3 tip, Vector3 step, out RaycastHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        foreach (RaycastHit h in Physics.RaycastAll(tip, step.normalized, step.magnitude, hitMask,
                                                     QueryTriggerInteraction.Ignore))
        {
            if (owner != null && h.transform.IsChildOf(owner)) continue;
            if (h.collider.GetComponentInParent<SkeletonCreep>() != null) continue;   // своих не ранит
            if (h.distance < best)
            {
                best = h.distance;
                hit = h;
            }
        }
        return best < float.MaxValue;
    }
}
