using UnityEngine;

// Колючки/лава/кактус: пока игрок касается -- получает урон каждые tickInterval секунд.
// Работает и с триггером, и с обычным коллайдером.
public class HurtZone : MonoBehaviour
{
    public float damagePerTick = 10f;
    public float tickInterval = 0.5f;

    private float nextTick;

    void OnTriggerStay(Collider other) => TryHurt(other);
    void OnCollisionStay(Collision collision) => TryHurt(collision.collider);

    private void TryHurt(Collider other)
    {
        if (Time.time < nextTick) return;
        PlayerHealth hp = other.GetComponentInParent<PlayerHealth>();
        if (hp == null) return;

        hp.TakeDamage(damagePerTick);
        nextTick = Time.time + tickInterval;
    }
}
