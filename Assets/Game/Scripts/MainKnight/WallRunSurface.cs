using UnityEngine;

// Автоматический бег по стене со специальным слоем (wallRunLayer).
//
// Игрок касается такой стены в воздухе -> запускается бег БЕЗ участия игрока:
// управление и отцепление недоступны, пока бег не завершится сам.
//
// Траектория в пространстве -- строго прямая линия от точки захвата до точки
// отцепления (персонаж становится кинематическим, физика не может её исказить).
// "Дугой-гиперболой" здесь является только СКОРОСТЬ вдоль этой прямой во времени:
// прогресс (0..1) прогоняется через кривую на основе гиперболического тангенса
// (tanh) -- плавный разгон, плато, плавное торможение. Сама линия при этом
// остаётся без отклонений.
//
// Точка отцепления всегда чуть раньше физического конца стены (на endMargin),
// чтобы бег выглядел аккуратно и не срывал персонажа с самого края.
[RequireComponent(typeof(Rigidbody))]
public class WallRun : MonoBehaviour
{
    [Header("Wall detection")]
    public LayerMask wallRunLayer;           // слой стен с автобегом
    public Transform orientation;
    public float wallCheckDistance = 0.8f;   // дистанция боковых рейкастов для обнаружения стены

    [Header("Path measurement")]
    public float wallProbeStep = 0.5f;       // шаг прощупывания стены вдоль направления бега
    public float maxWallRunSearchDistance = 40f; // предел поиска конца стены
    public float endMargin = 1f;             // насколько раньше реального конца стены отцепляемся
    public float minRunDistance = 1.5f;      // если доступный путь короче -- бег не запускается

    [Header("Run motion")]
    public float runDuration = 1.2f;         // сколько времени в секундах занимает весь пробег
    public float accelSharpness = 3f;        // "крутизна" гиперболической кривой разгона/торможения
    public float wallHugDistance = 0.3f;     // небольшой отступ от стены наружу, чтобы не клипать в неё
    public float exitSpeed = 8f;             // скорость, с которой персонаж продолжает движение после отцепления

    [Header("Retrigger")]
    public float reTriggerCooldown = 0.4f;

    [Header("Refs")]
    public KnightMovment movement;
    public WallClimb wallClimb;              // необязательно -- на время бега тоже отключается

    private Rigidbody rb;
    private bool isRunning;
    private float runTimer;
    private float cooldownTimer;

    private Vector3 startPoint;
    private Vector3 endPoint;
    private Vector3 tangentDir;
    private Vector3 wallNormal;

    public bool IsRunning => isRunning;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (isRunning)
            return; // во время бега вход запрещён -- всё решает FixedUpdate

        if (cooldownTimer > 0f)
            return;

        bool grounded = movement != null && movement.IsGrounded();
        if (grounded)
            return;

        if (CheckWall(out RaycastHit hit))
        {
            TryStartRun(hit);
        }
    }

    void FixedUpdate()
    {
        if (!isRunning)
            return;

        runTimer += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(runTimer / runDuration);
        float easedT = HyperbolicEase(t, accelSharpness);

        // строго линейная интерполяция в пространстве -- без отклонений от прямой
        Vector3 pos = Vector3.Lerp(startPoint, endPoint, easedT) + wallNormal * wallHugDistance;
        rb.MovePosition(pos);

        if (t >= 1f)
        {
            EndRun();
        }
    }

    private bool CheckWall(out RaycastHit hit)
    {
        Vector3 origin = transform.position;
        Vector3 fwd = orientation != null ? orientation.forward : transform.forward;
        Vector3 right = orientation != null ? orientation.right : transform.right;

        Vector3[] directions = { fwd, right, -right };

        foreach (var dir in directions)
        {
            if (Physics.Raycast(origin, dir, out hit, wallCheckDistance, wallRunLayer))
                return true;
        }

        hit = default;
        return false;
    }

    private void TryStartRun(RaycastHit hit)
    {
        Vector3 normal = hit.normal;

        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 tangent = flatVel.sqrMagnitude > 0.1f
            ? Vector3.ProjectOnPlane(flatVel, normal).normalized
            : Vector3.ProjectOnPlane(orientation != null ? orientation.forward : transform.forward, normal).normalized;

        if (tangent.sqrMagnitude < 0.0001f)
            return; // направление вдоль стены не определилось -- не запускаем

        Vector3 origin = transform.position;
        float wallLength = MeasureWallLength(origin, tangent, normal);
        float runDistance = Mathf.Max(0f, wallLength - endMargin);

        if (runDistance < minRunDistance)
            return; // стена слишком короткая для аккуратного автобега

        StartRun(origin, tangent, normal, runDistance);
    }

    // Прощупывает стену шагами вдоль tangent, пока рейкаст в сторону -normal
    // продолжает попадать в wallRunLayer. Возвращает пройденную дистанцию до
    // того места, где стена заканчивается.
    private float MeasureWallLength(Vector3 origin, Vector3 tangent, Vector3 normal)
    {
        float distance = 0f;

        while (distance < maxWallRunSearchDistance)
        {
            Vector3 probeOrigin = origin + tangent * (distance + wallProbeStep) + normal * wallCheckDistance;

            if (!Physics.Raycast(probeOrigin, -normal, out RaycastHit probeHit, wallCheckDistance * 2f, wallRunLayer))
                break;

            distance += wallProbeStep;
        }

        return distance;
    }

    private void StartRun(Vector3 origin, Vector3 tangent, Vector3 normal, float runDistance)
    {
        isRunning = true;
        runTimer = 0f;

        startPoint = origin;
        tangentDir = tangent;
        wallNormal = normal;
        endPoint = startPoint + tangentDir * runDistance;

        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true; // гарантирует, что путь останется идеально прямым

        if (movement != null)
            movement.enabled = false;

        if (wallClimb != null)
            wallClimb.enabled = false;
    }

    private void EndRun()
    {
        isRunning = false;
        cooldownTimer = reTriggerCooldown;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = tangentDir * exitSpeed;

        if (movement != null)
            movement.enabled = true;

        if (wallClimb != null)
            wallClimb.enabled = true;
    }

    // Гиперболический (tanh) easing: плавный разгон в начале, плато в середине,
    // плавное торможение к концу. Монотонно растёт от 0 до 1.
    private float HyperbolicEase(float t, float sharpness)
    {
        if (sharpness <= 0.0001f)
            return t;

        float raw = (float)System.Math.Tanh(sharpness * (2f * t - 1f));
        float minV = (float)System.Math.Tanh(-sharpness);
        float maxV = (float)System.Math.Tanh(sharpness);
        return (raw - minV) / (maxV - minV);
    }
}
