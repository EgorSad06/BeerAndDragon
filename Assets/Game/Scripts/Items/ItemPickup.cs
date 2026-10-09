using UnityEngine;

// Предмет, валяющийся на уровне. Крутится, покачивается; когда игрок касается -- уходит в инвентарь.
// Коллайдер на этом объекте должен быть триггером (выставляется сам).
public class ItemPickup : MonoBehaviour
{
    public ItemDefinition item;
    public int count = 1;

    [Header("Visual")]
    public Transform visual;
    public float spinSpeed = 90f;
    public float bobHeight = 0.12f;
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
        if (inv == null || item == null) return;

        int left = inv.Add(item, count);
        if (left == count) return; // не влезло ничего

        count = left;
        if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        if (count <= 0) Destroy(gameObject);
    }
}
