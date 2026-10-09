using UnityEngine;

// Коробка патронов. Пополняет запас всем Weapon игрока с таким же ammoType
// (даже тем, что сейчас не в руках). Если запас полный -- коробка остаётся лежать.
public class AmmoPickup : MonoBehaviour
{
    public string ammoType = "shells";
    public int amount = 8;
    public string displayName = "патронов";

    [Header("Visual")]
    public Transform visual;
    public float spinSpeed = 60f;
    public float bobHeight = 0.08f;
    public float bobSpeed = 2f;

    public AudioClip pickupSound;

    private Vector3 visualBase;
    private float phase;

    void Start()
    {
        foreach (Collider c in GetComponents<Collider>()) c.isTrigger = true;
        if (visual != null) visualBase = visual.localPosition;
        phase = Random.value * 10f;
    }

    void Update()
    {
        if (visual == null) return;
        visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        visual.localPosition = visualBase + Vector3.up * (Mathf.Sin((Time.time + phase) * bobSpeed) * bobHeight);
    }

    void OnTriggerEnter(Collider other)
    {
        Inventory inv = other.GetComponentInParent<Inventory>();
        Transform root = inv != null ? inv.transform : other.transform.root;
        if (root.GetComponent<KnightMovement>() == null && inv == null) return; // не игрок

        int taken = 0;
        foreach (Weapon w in root.GetComponentsInChildren<Weapon>(true))
        {
            if (w.ammoType != ammoType) continue;
            taken += w.AddAmmo(amount - taken);
            if (taken >= amount) break;
        }
        if (taken <= 0) return;

        amount -= taken;
        HudMessages.Post($"+{taken} {displayName}");
        if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        if (amount <= 0) Destroy(gameObject);
    }
}
