using UnityEngine;

// V -- переключить вид от 1-го / 3-го лица (на скейте камера и так от 3-го).
// В 3-м лице модельки оружия у камеры прячутся, стрельба и удары идут по центру экрана.
// Вешается на Player.
[DefaultExecutionOrder(150)]
public class ThirdPersonCamera : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.V;
    public bool thirdPerson = false;
    public float distance = 3.5f;
    public float height = 0.9f;
    public float shoulderOffset = 0.6f;     // камера чуть правее -- рыцарь не закрывает прицел

    public Movment look;                    // найдётся сам
    public SkaterController skater;         // найдётся сам
    public Transform weaponHolder;          // найдётся сам

    private Vector3 camLocalPos;
    private bool viewModelVisible = true;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        if (look == null) look = GetComponentInChildren<Movment>();
        if (skater == null) skater = GetComponent<SkaterController>();
        if (weaponHolder == null)
        {
            WeaponSwitcher ws = GetComponentInChildren<WeaponSwitcher>(true);
            if (ws != null) weaponHolder = ws.transform;
        }
        if (look != null) camLocalPos = look.transform.localPosition;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey) && !PlayerInputLock.Locked)
        {
            thirdPerson = !thirdPerson;
            if (!thirdPerson && look != null && !(skater != null && skater.IsRiding))
                look.transform.localPosition = camLocalPos;
        }
    }

    void LateUpdate()
    {
        bool riding = skater != null && skater.IsRiding;
        SetViewModelVisible(!thirdPerson || riding);
        if (!thirdPerson || riding || look == null) return; // на скейте камерой рулит SkaterController

        Transform cam = look.transform;
        Vector3 pivot = transform.position + Vector3.up * height + cam.right * shoulderOffset;
        Vector3 back = -cam.forward;
        float dist = distance;
        int n = Physics.SphereCastNonAlloc(transform.position + Vector3.up * height, 0.25f, (pivot + back * distance - (transform.position + Vector3.up * height)).normalized,
            hits, distance + shoulderOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].transform.IsChildOf(transform)) continue;
            if (hits[i].distance > 0f && hits[i].distance < dist) dist = hits[i].distance;
        }
        cam.position = pivot + back * Mathf.Max(0.4f, dist - 0.1f);
    }

    private void SetViewModelVisible(bool visible)
    {
        if (weaponHolder == null || visible == viewModelVisible) return;
        viewModelVisible = visible;
        foreach (Renderer r in weaponHolder.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
    }
}
