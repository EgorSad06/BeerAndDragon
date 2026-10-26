using UnityEngine;

// Шаги игрока: пока рыцарь идёт по земле, через каждые stepDistance метров -- случайный шаг по траве
// (Resources/Sounds/Player/step_grass_*). Чем быстрее бежим, тем чаще шаги; в воздухе, в подкате и на скейте тихо.
// Добавляется сам из KnightMovement, в сцене настраивать ничего не нужно.
[RequireComponent(typeof(KnightMovement))]
public class PlayerFootsteps : MonoBehaviour
{
    public AudioClip[] steps;                   // пусто -- загрузятся из Resources
    public float stepDistance = 1.7f;           // метров между шагами
    public float minSpeed = 1.0f;               // медленнее -- стоим
    [Range(0f, 1f)] public float volume = 0.55f;

    private KnightMovement movement;
    private SkaterController skater;
    private Rigidbody rb;
    private AudioSource source;
    private float travelled;
    private int last = -1;

    void Awake()
    {
        movement = GetComponent<KnightMovement>();
        skater = GetComponent<SkaterController>();
        rb = GetComponent<Rigidbody>();
        if (steps == null || steps.Length == 0)
            steps = Resources.LoadAll<AudioClip>("Sounds/Player/Steps");
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
    }

    void Update()
    {
        if (rb == null || steps.Length == 0) return;
        bool riding = skater != null && skater.IsRiding;
        bool walking = movement.IsGrounded() && !riding && movement.state != KnightMovement.MovementState.sliding;
        Vector3 v = rb.linearVelocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        if (!walking || speed < minSpeed)
        {
            travelled = stepDistance * 0.6f;    // первый шаг -- почти сразу после старта
            return;
        }
        travelled += speed * Time.deltaTime;
        if (travelled < stepDistance) return;
        travelled = 0f;
        Step();
    }

    private void Step()
    {
        int i = Random.Range(0, steps.Length);
        if (steps.Length > 1 && i == last) i = (i + 1) % steps.Length;   // без одинаковых подряд
        last = i;
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(steps[i], volume * Random.Range(0.85f, 1f));
    }
}
