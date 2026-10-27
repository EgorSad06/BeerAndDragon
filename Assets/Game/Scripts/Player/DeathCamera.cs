using UnityEngine;

// Камера при смерти: голова рыцаря «падает» на землю -- с ускорением вниз, заваливаясь на бок,
// ударяется и пару раз подпрыгивает. Вешает DeathScreen на камеру в момент смерти.
// Работает после всех, кто двигает камеру (CameraPOV, ThirdPersonCamera, скейт), и перебивает их.
// Оружие и руки у камеры прячутся -- лежим на земле с пустыми руками.
[DefaultExecutionOrder(10000)]
public class DeathCamera : MonoBehaviour
{
    public float fallTime = 0.75f;
    public float groundOffset = 0.18f;     // высота глаз над землёй, когда упали
    public float roll = 78f;               // насколько заваливаемся на бок, град
    public float pitch = 6f;               // взгляд чуть вниз

    private Transform player;
    private Vector3 startOffset, endOffset; // от игрока: если тело ещё скользит, камера едет с ним
    private Quaternion startRot, endRot;
    private float startTime;
    private float side;

    public static DeathCamera Begin(Transform cam, Transform player)
    {
        DeathCamera dc = cam.GetComponent<DeathCamera>();
        if (dc == null) dc = cam.gameObject.AddComponent<DeathCamera>();
        dc.Init(player);
        return dc;
    }

    private void Init(Transform owner)
    {
        player = owner;
        startTime = Time.unscaledTime;
        side = Random.value < 0.5f ? -1f : 1f;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        Vector3 start = transform.position;
        startRot = transform.rotation;
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f) flatForward = player != null ? player.forward : Vector3.forward;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        Vector3 end = new Vector3(start.x, GroundY(start) + groundOffset, start.z) - flatForward * 0.25f + right * (0.15f * side);
        float yaw = Quaternion.LookRotation(flatForward).eulerAngles.y + Random.Range(8f, 18f) * side;
        endRot = Quaternion.Euler(pitch, yaw, roll * side);

        Vector3 origin = player != null ? player.position : Vector3.zero;
        startOffset = start - origin;
        endOffset = end - origin;
    }

    // Земля под камерой -- первое, во что упрётся луч вниз, кроме самого игрока
    private float GroundY(Vector3 from)
    {
        float best = float.MaxValue, y = from.y - 1.6f;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (player != null && hit.transform.IsChildOf(player)) continue;
            if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
        }
        return y;
    }

    void LateUpdate()
    {
        float time = Time.unscaledTime - startTime;
        float k = Mathf.Clamp01(time / fallTime);
        float fall = k * k;                                         // как камень -- с ускорением
        float tilt = 1f - (1f - k) * (1f - k) * (1f - k);           // на бок валимся быстрее, чем долетаем

        // удар о землю: затухающие подскоки
        float after = Mathf.Max(0f, time - fallTime);
        float bounce = k < 1f ? 0f : Mathf.Exp(-after * 6f) * Mathf.Abs(Mathf.Sin(after * 16f));

        Vector3 origin = player != null ? player.position : Vector3.zero;
        Vector3 pos = origin + Vector3.Lerp(startOffset, endOffset, fall) + Vector3.up * (bounce * 0.07f);
        Quaternion rot = Quaternion.Slerp(startRot, endRot, tilt) * Quaternion.Euler(-bounce * 4f, 0f, bounce * 5f * side);
        transform.SetPositionAndRotation(pos, rot);
    }
}
