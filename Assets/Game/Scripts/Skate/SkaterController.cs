using System;
using System.Collections.Generic;
using UnityEngine;

// Катание на скейте в духе Tony Hawk's Pro Skater. Висит на игроке (рядом с KnightMovment)
// и пока игрок на доске -- полностью управляет его Rigidbody.
//
// Управление (на доске):
//   W -- толчок, S -- тормоз, A/D -- поворот (в воздухе -- спин)
//   Space -- олли (держи дольше -- прыжок выше; с перил/мэньюала -- тоже работает)
//   ЛКМ + направление -- флип (Kickflip / Impossible / Pop Shuvit / Heelflip / Varial)
//   ПКМ (держать) + направление -- грэб (Melon / Nosegrab / Tailgrab / Indy / Stalefish)
//   E в воздухе у перил/края -- грайнд, A/D держат баланс
//   Ctrl на земле -- мэньюал, W/S держат баланс
//   E на земле -- слезть
//
// Комбо: каждый трюк добавляет очки и +1 к множителю. Приземлился и проехал чуть-чуть
// без мэньюала -- комбо засчитано. Бэйл (не докрутил флип, приземлился боком,
// врезался в стену, потерял баланс) -- комбо сгорает, а доска улетает.
[RequireComponent(typeof(Rigidbody))]
public class SkaterController : MonoBehaviour
{
    public enum State { Off, Ground, Air, Grind }

    [Header("Keys")]
    public KeyCode mountKey = KeyCode.E;
    public KeyCode grindKey = KeyCode.E;
    public KeyCode ollieKey = KeyCode.Space;
    public KeyCode manualKey = KeyCode.LeftControl;
    public KeyCode flipKey = KeyCode.Mouse0;
    public KeyCode grabKey = KeyCode.Mouse1;

    [Header("Mount")]
    public float mountDistance = 2.2f;
    public float remountDelay = 0.8f;

    [Header("Ground")]
    public float pushImpulse = 5f;
    public float pushInterval = 0.32f;
    public float maxPushSpeed = 16f;        // толчками быстрее не разгонишься
    public float maxSpeed = 30f;            // с горки -- можно до этого
    public float rollingFriction = 0.04f;
    public float brakeDecel = 18f;
    public float turnSpeed = 170f;          // град/сек на малой скорости
    public float highSpeedTurnFactor = 0.7f; // на макс. скорости поворот мягче
    public float steerResponse = 9f;        // сглаживание руля
    public float slopeGravityScale = 1f;
    public float groundStick = 0.6f;
    public float wallSlideKeep = 0.85f;     // сколько скорости остаётся, когда чиркнул боком по стене

    [Header("Ollie")]
    public float ollieMinVelocity = 6f;
    public float ollieMaxVelocity = 9.5f;
    public float ollieChargeTime = 0.45f;

    [Header("Air")]
    public float airSpinSpeed = 420f;       // град/сек
    public float landAngleTolerance = 80f;  // насколько можно приземлиться "не по ходу"
    public float groundProbeRadius = 0.3f;
    public float groundCheckExtra = 0.5f;
    public float flipLandGrace = 0.12f;     // столько секунд до конца флипа уже можно приземляться
    [Range(0f, 1f)] public float flipBailProgress = 0.45f; // флип докручен меньше чем на столько -- бэйл, иначе докручивается сам
    public float sidewaysBailSpeed = 9f;    // боком падаем только на такой скорости, медленнее -- просто тормозим

    [Header("Grind")]
    public float grindSnapDistance = 1.6f;
    public float minGrindSpeed = 8f;
    public float grindBuffer = 0.25f;       // нажал E чуть раньше, чем долетел до перил -- всё равно сработает
    public float boardThickness = 0.16f;

    [Header("Balance (грайнд / мэньюал)")]
    public float balanceDrift = 0.35f;
    public float balanceDriftGrowth = 0.2f; // чем дольше держишь -- тем сложнее
    public float balanceControl = 3.2f;

    [Header("Bail")]
    public float wallBailSpeed = 20f;
    public float bailDamage = 5f;

    [Header("Camera (вид от третьего лица на доске)")]
    public bool thirdPerson = true;
    public float camDistance = 4.5f;
    public float camHeight = 0.9f;
    public float camFollowDelay = 0.6f;     // через сколько секунд без мыши камера встаёт за спину
    public float camFollowSpeed = 4f;

    [Header("Refs (найдутся сами)")]
    public KnightMovment movement;
    public Movment look;
    public Transform model;
    public WeaponSwitcher weaponSwitcher;
    public Inventory inventory;
    public PlayerStatusEffects effects;
    public PlayerHealth health;
    public List<Behaviour> disableWhileRiding = new List<Behaviour>();

    // --- состояние, читается из SkateHUD ---
    public State CurrentState { get; private set; } = State.Off;
    public bool IsRiding => CurrentState != State.Off;
    public Skateboard Board { get; private set; }
    public Skateboard NearbyBoard { get; private set; }
    public float Speed { get; private set; }
    public bool InManual { get; private set; }
    public bool BalanceActive => CurrentState == State.Grind || InManual;
    public float Balance { get; private set; }
    public float RideTime => IsRiding ? Time.time - mountTime : 0f;
    public float LastPushTime { get; private set; } = -10f;
    public bool IsChargingOllie => ollieChargeStart >= 0f;
    public bool IsGrabbing => grabbing;
    public bool IsFlipping => Time.time < flipEndTime;
    public float FlipProgress => flipDuration > 0f ? Mathf.Clamp01((Time.time - flipStartTime) / flipDuration) : 1f;

    // Где стоят ноги: центр верха деки и её поворот БЕЗ вращения флипа (для IK ног в KnightAnimator)
    public Vector3 FootFramePosition { get; private set; }
    public Quaternion FootFrameRotation { get; private set; } = Quaternion.identity;
    public Vector3 BoardGroundPosition { get; private set; }
    public float DeckLift => IsRiding ? boardThickness : 0f;   // на сколько KnightAnimator поднимает рыцаря

    public int TotalScore { get; private set; }
    public IReadOnlyList<string> ComboTricks => comboTricks;
    public int ComboBase => comboBase;
    public int ComboMultiplier => Mathf.Max(1, comboTricks.Count);
    public string LiveTrick { get; private set; }   // то, что делается прямо сейчас (грайнд/грэб/мэньюал)

    public event Action<int> ComboLanded;    // сколько очков засчитали
    public event Action<string> BailedEvent;
    public event Action<string> TrickAdded;

    private Rigidbody rb;
    private float halfHeight = 1f;
    private float feetOffset = 1f;     // от transform.position до подошвы капсулы (капсула может быть смещена)

    private Vector3 heading = Vector3.forward;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 visualUp = Vector3.up;
    private Vector3 lastGroundForward = Vector3.forward;
    private Vector3 lastVelocity;

    private float steer, throttle;
    private float steerSmoothed;
    private float flipStartTime, flipDuration;
    private Collider[] ownColliders;
    private PhysicsMaterial[] savedMaterials;
    private PhysicsMaterial frictionless;
    private bool pushQueued;
    private float nextPushTime;
    private float ollieChargeStart = -1f;
    private float groundIgnoreUntil;
    private float mountTime;
    private float remountAllowedTime;
    private float landTime = -10f;
    private float lastGrindPress = -10f;
    private float lastMouseTime;

    private float spinAccum;
    private float flipEndTime;
    private Vector3 procEuler;
    private float procStart, procDuration;

    private bool grabbing;
    private string grabName;
    private float grabStart;
    private Vector3 grabTilt;

    private GrindRail rail;
    private GrindRail ignoreRail;
    private float ignoreRailUntil;
    private float railT;
    private float railDirSign;
    private float grindSpeed;
    private string grindName;
    private float grindStart;
    private float boardYawOffset;

    private string manualName;
    private float manualStart;
    private float manualPitch;
    private float balanceTimer;

    private readonly List<string> comboTricks = new List<string>();
    private int comboBase;

    private Vector3 camLocalPos;
    private Vector3 modelLocalPos;
    private Quaternion modelLocalRot;
    private Vector3 modelLocalScale;
    private string weaponBeforeRide;

    private readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (movement == null) movement = GetComponent<KnightMovment>();
        if (look == null) look = GetComponentInChildren<Movment>();
        if (model == null && movement != null) model = movement.model;
        if (weaponSwitcher == null) weaponSwitcher = GetComponentInChildren<WeaponSwitcher>(true);
        if (inventory == null) inventory = GetComponent<Inventory>();
        if (effects == null) effects = GetComponent<PlayerStatusEffects>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (movement != null) halfHeight = movement.playerHeight * 0.5f;
        feetOffset = halfHeight;
        Collider body = movement != null && movement.capsule != null ? movement.capsule : GetComponentInChildren<CapsuleCollider>();
        if (body != null) feetOffset = transform.position.y - body.bounds.min.y;
        if (look != null) camLocalPos = look.transform.localPosition;

        ownColliders = GetComponents<Collider>();
        savedMaterials = new PhysicsMaterial[ownColliders.Length];
        frictionless = new PhysicsMaterial("SkateFrictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        if (disableWhileRiding.Count == 0)
        {
            // Всё, что само крутит скорость игрока, на доске должно молчать
            if (movement != null) disableWhileRiding.Add(movement);
            foreach (WallClimb w in GetComponents<WallClimb>()) disableWhileRiding.Add(w);
            foreach (WallRun w in GetComponents<WallRun>()) disableWhileRiding.Add(w);
        }
    }

    void Start()
    {
        if (FindFirstObjectByType<SkateHUD>() == null)
            new GameObject("SkateHUD").AddComponent<SkateHUD>();
    }

    private float SpeedMult => effects != null ? effects.SpeedMultiplier : 1f;

    // ================= Update: ввод =================

    void Update()
    {
        if (health != null && health.IsDead)
        {
            if (IsRiding) Dismount(false);
            return;
        }

        bool locked = PlayerInputLock.Locked;
        steer = locked ? 0f : Input.GetAxisRaw("Horizontal");
        throttle = locked ? 0f : Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f) lastMouseTime = Time.time;

        if (!IsRiding)
        {
            FindNearbyBoard();
            if (!locked && NearbyBoard != null && Input.GetKeyDown(mountKey) && Time.time >= remountAllowedTime)
                Mount(NearbyBoard);
            return;
        }
        NearbyBoard = null;
        if (locked) return;

        // Олли: держим -- копим, отпускаем -- прыгаем
        if (Input.GetKeyDown(ollieKey) && CurrentState != State.Air) ollieChargeStart = Time.time;
        if (Input.GetKeyUp(ollieKey) && ollieChargeStart >= 0f)
        {
            float charge = Mathf.Clamp01((Time.time - ollieChargeStart) / ollieChargeTime);
            ollieChargeStart = -1f;
            if (CurrentState != State.Air) Ollie(charge);
        }

        switch (CurrentState)
        {
            case State.Ground:
                if (Input.GetKey(KeyCode.W) && !InManual && Time.time >= nextPushTime && ollieChargeStart < 0f)
                {
                    pushQueued = true;
                    nextPushTime = Time.time + pushInterval;
                }
                if (Input.GetKeyDown(manualKey))
                {
                    if (InManual) EndManual(true);
                    else StartManual();
                }
                if (Input.GetKeyDown(mountKey) && !InManual)
                {
                    Dismount(false);
                    return;
                }
                UpdateBalance(InManual ? throttle : 0f);
                TryBankCombo();
                break;

            case State.Air:
                if (Input.GetKeyDown(grindKey)) lastGrindPress = Time.time;
                if (Input.GetKeyDown(flipKey)) TryFlip();
                UpdateGrab();
                break;

            case State.Grind:
                UpdateBalance(steer);
                break;
        }
    }

    // ================= FixedUpdate: физика =================

    void FixedUpdate()
    {
        if (!IsRiding) return;
        lastVelocity = rb.linearVelocity;

        switch (CurrentState)
        {
            case State.Ground: GroundMove(); break;
            case State.Air: AirMove(); break;
            case State.Grind: GrindMove(); break;
        }
    }

    private void GroundMove()
    {
        if (!GroundCheck(out RaycastHit hit))
        {
            EnterAir(lastGroundForward * Speed);
            if (InManual) EndManual(false); // съехал с края в мэньюале -- он закончился, комбо идёт дальше
            return;
        }
        groundNormal = hit.normal;
        float dt = Time.fixedDeltaTime;

        steerSmoothed = Mathf.MoveTowards(steerSmoothed, steer, steerResponse * dt);
        float speed01 = Mathf.Clamp01(Speed / Mathf.Max(1f, maxPushSpeed * SpeedMult));
        float turn = steerSmoothed * turnSpeed * Mathf.Lerp(1f, highSpeedTurnFactor, speed01) * (InManual ? 0.6f : 1f) * dt;
        heading = Quaternion.AngleAxis(turn, Vector3.up) * heading;

        Vector3 fwd = Vector3.ProjectOnPlane(heading, groundNormal).normalized;

        Speed += Vector3.Dot(Physics.gravity, fwd) * slopeGravityScale * dt;
        if (pushQueued)
        {
            pushQueued = false;
            float cap = maxPushSpeed * SpeedMult;
            if (Speed < cap)
            {
                Speed = Mathf.Min(Speed + pushImpulse * SpeedMult, cap);
                LastPushTime = Time.time;
            }
        }
        if (throttle < -0.1f && !InManual) Speed = Mathf.MoveTowards(Speed, 0f, brakeDecel * dt);
        Speed -= Speed * rollingFriction * dt;

        // Заехал на горку и покатился назад -- едем "фейки"
        if (Speed < 0f)
        {
            heading = -heading;
            fwd = -fwd;
            Speed = -Speed;
        }
        Speed = Mathf.Min(Speed, maxSpeed * SpeedMult);

        lastGroundForward = fwd;
        rb.linearVelocity = fwd * Speed - groundNormal * groundStick;
    }

    private void AirMove()
    {
        float spin = steer * airSpinSpeed * Time.fixedDeltaTime;
        spinAccum += spin;
        heading = Quaternion.AngleAxis(spin, Vector3.up) * heading;

        if (Input.GetKey(grindKey) || Time.time - lastGrindPress < grindBuffer)
            if (TryStartGrind()) return;

        if (Time.time > groundIgnoreUntil && rb.linearVelocity.y <= 0.5f && GroundCheck(out RaycastHit hit))
            Land(hit);
    }

    private void GrindMove()
    {
        if (rail == null) { ExitGrind(false); return; }

        railT += railDirSign * grindSpeed * Time.fixedDeltaTime / Mathf.Max(0.01f, rail.Length);
        if (railT < 0f || railT > 1f)
        {
            ExitGrind(false);
            return;
        }

        Vector3 target = rail.PointAt(railT) + Vector3.up * (feetOffset + boardThickness);
        rb.linearVelocity = (target - rb.position) / Time.fixedDeltaTime;
    }

    // ================= Посадка / сход =================

    private void FindNearbyBoard()
    {
        NearbyBoard = null;
        float best = mountDistance;
        Vector3 feet = transform.position - Vector3.up * feetOffset;
        foreach (Skateboard b in Skateboard.All)
        {
            if (b == null || b.IsRidden) continue;
            float d = Vector3.Distance(feet, b.transform.position);
            if (d < best)
            {
                best = d;
                NearbyBoard = b;
            }
        }
    }

    public void Mount(Skateboard board)
    {
        if (board == null || IsRiding) return;
        if (inventory != null && inventory.IsOpen) return;

        Board = board;
        board.SetRidden(true);
        if (board.deckHeight > 0.01f) boardThickness = board.deckHeight;

        foreach (Behaviour b in disableWhileRiding) if (b != null) b.enabled = false;
        rb.linearDamping = 0f;
        rb.useGravity = true;

        Vector3 flat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 camFlat = look != null ? Vector3.ProjectOnPlane(look.transform.forward, Vector3.up) : transform.forward;
        heading = (flat.magnitude > 1f ? flat : camFlat).normalized;
        if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
        Speed = flat.magnitude;          // разбежался и запрыгнул -- скорость сохраняется
        lastGroundForward = heading;

        if (weaponSwitcher != null)
        {
            weaponBeforeRide = weaponSwitcher.CurrentName;
            weaponSwitcher.Equip(null);
        }
        PlayerInputLock.WeaponsBlocked = true;

        // Без трения капсула не тормозит о пол -- скорость не "съедается" физикой
        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] == null || ownColliders[i].isTrigger) continue;
            savedMaterials[i] = ownColliders[i].sharedMaterial;
            ownColliders[i].sharedMaterial = frictionless;
        }

        if (model != null)
        {
            modelLocalPos = model.localPosition;
            modelLocalRot = model.localRotation;
            modelLocalScale = model.localScale;
        }

        mountTime = Time.time;
        GameLog.Info("Skate", $"Встал на скейт, скорость {Speed:0.0}");
        ResetTrickState();
        CurrentState = GroundCheck(out _) ? State.Ground : State.Air;
    }

    public void Dismount(bool bailed)
    {
        if (!IsRiding) return;

        if (CurrentState == State.Grind) rb.useGravity = true;
        if (!bailed) BankCombo();

        Skateboard board = Board;
        Board = null;
        CurrentState = State.Off;
        InManual = false;
        LiveTrick = null;

        if (board != null)
        {
            board.SetRidden(false);
            if (bailed)
                board.Kick(lastVelocity * 0.8f + Vector3.up * 3f);
            else
            {
                // Доска остаётся под ногами и чуть впереди
                board.transform.position = transform.position - Vector3.up * (feetOffset - 0.05f) + heading * 0.6f;
                board.Body.linearVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up) * 0.5f;
            }
        }

        rb.useGravity = true;
        if (bailed) rb.linearVelocity = lastVelocity * 0.4f + Vector3.up * 3f;

        foreach (Behaviour b in disableWhileRiding) if (b != null) b.enabled = true;
        for (int i = 0; i < ownColliders.Length; i++)
            if (ownColliders[i] != null && !ownColliders[i].isTrigger)
                ownColliders[i].sharedMaterial = savedMaterials[i];

        if (look != null) look.transform.localPosition = camLocalPos;
        if (model != null)
        {
            model.localPosition = modelLocalPos;
            model.localRotation = modelLocalRot;
            model.localScale = modelLocalScale;
        }

        PlayerInputLock.WeaponsBlocked = false;
        if (weaponSwitcher != null && !string.IsNullOrEmpty(weaponBeforeRide))
            weaponSwitcher.Equip(weaponBeforeRide);

        remountAllowedTime = Time.time + remountDelay;
    }

    private void Bail(string reason)
    {
        if (!IsRiding) return;
        GameLog.Info("Skate", "Бэйл: " + reason);
        LoseCombo();
        BailedEvent?.Invoke(reason);
        Dismount(true);
        if (health != null && bailDamage > 0f) health.TakeDamage(bailDamage);
    }

    // ================= Прыжки / приземление =================

    private void Ollie(float charge)
    {
        if (CurrentState == State.Grind) ExitGrind(false);
        if (InManual) EndManual(false);

        float up = Mathf.Lerp(ollieMinVelocity, ollieMaxVelocity, charge);
        Vector3 v = CurrentState == State.Ground ? lastGroundForward * Speed : rb.linearVelocity;
        v.y = Mathf.Max(v.y, 0f) + up;
        EnterAir(v);
        if (Board != null) Board.PlayClip(Board.ollieClip);
    }

    private void EnterAir(Vector3 velocity)
    {
        rb.linearVelocity = velocity;
        rb.useGravity = true;
        CurrentState = State.Air;
        spinAccum = 0f;
        groundIgnoreUntil = Time.time + 0.15f;
    }

    private void Land(RaycastHit hit)
    {
        EndGrab();

        if (FlipUnfinished())
        {
            Bail("Не докрутил флип");
            return;
        }
        FinishFlipNow();

        Vector3 flat = Vector3.ProjectOnPlane(rb.linearVelocity, hit.normal);
        Vector3 flatXZ = Vector3.ProjectOnPlane(flat, Vector3.up);
        Vector3 headXZ = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;

        if (flatXZ.magnitude > 1f)
        {
            float angle = Vector3.Angle(headXZ, flatXZ);
            if (angle > landAngleTolerance && angle < 180f - landAngleTolerance)
            {
                if (flatXZ.magnitude > sidewaysBailSpeed * SpeedMult)
                {
                    Bail("Приземлился боком");
                    return;
                }
                rb.linearVelocity *= 0.5f; // медленно -- просто "поймал" доску и потерял скорость
                flat *= 0.5f;
            }
            heading = flatXZ.normalized;   // фейки/нормально -- неважно, едем по скорости
        }

        float spinDeg = Mathf.Abs(spinAccum);
        if (spinDeg >= 150f)
        {
            int deg = Mathf.RoundToInt(spinDeg / 180f) * 180;
            AddTrick($"{(spinAccum > 0 ? "FS" : "BS")} {deg}", deg / 180 * 150);
        }

        Speed = flat.magnitude;
        groundNormal = hit.normal;
        lastGroundForward = Vector3.ProjectOnPlane(heading, groundNormal).normalized;
        CurrentState = State.Ground;
        landTime = Time.time;
        procDuration = 0f;
        if (Board != null) Board.PlayClip(Board.idleClip);
    }

    private bool GroundCheck(out RaycastHit best)
    {
        best = default;
        float dist = feetOffset - groundProbeRadius + groundCheckExtra;
        int n = Physics.SphereCastNonAlloc(rb.position, groundProbeRadius, Vector3.down, hits, dist,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.distance <= 0f && h.point == Vector3.zero) continue; // уже внутри коллайдера
            if (h.transform.IsChildOf(transform)) continue;
            if (Board != null && h.transform.IsChildOf(Board.transform)) continue;
            if (h.normal.y < 0.45f) continue;                            // стена, а не пол
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = h;
            }
        }
        return bestDist < float.MaxValue;
    }

    void OnCollisionEnter(Collision c)
    {
        if (CurrentState != State.Ground && CurrentState != State.Air) return;

        Vector3 flat = Vector3.ProjectOnPlane(lastVelocity, Vector3.up);
        float flatSpeed = flat.magnitude;
        if (flatSpeed < 1f) return;

        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint cp = c.GetContact(i);
            if (cp.normal.y > 0.45f) continue; // пол/рампа
            float headOn = Vector3.Dot(-cp.normal, flat / flatSpeed);
            if (headOn < 0.6f)
            {
                SlideAlongWall(cp.normal); // чиркнул боком -- едем вдоль стены
                continue;
            }

            if (headOn > 0.85f && flatSpeed > wallBailSpeed * SpeedMult)
            {
                Bail("Поцеловал стену");
                return;
            }
            // Бонк: отскакиваем от стены
            Vector3 n = Vector3.ProjectOnPlane(cp.normal, Vector3.up).normalized;
            heading = Vector3.Reflect(heading, n).normalized;
            Speed *= 0.35f;
            return;
        }
    }

    void OnCollisionStay(Collision c)
    {
        if (CurrentState != State.Ground) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint cp = c.GetContact(i);
            if (cp.normal.y > 0.45f) continue;
            if (Vector3.Dot(heading, -cp.normal) > 0f) SlideAlongWall(cp.normal);
        }
    }

    private void SlideAlongWall(Vector3 normal)
    {
        Vector3 n = Vector3.ProjectOnPlane(normal, Vector3.up);
        if (n.sqrMagnitude < 0.01f) return;
        Vector3 along = Vector3.ProjectOnPlane(heading, n.normalized);
        if (along.sqrMagnitude < 0.01f) return;
        heading = along.normalized;
        Speed *= wallSlideKeep;
    }

    // ================= Трюки =================

    private void ResetTrickState()
    {
        spinAccum = 0f;
        flipEndTime = 0f;
        procDuration = 0f;
        grabbing = false;
        rail = null;
        InManual = false;
        LiveTrick = null;
        Balance = 0f;
        boardYawOffset = 0f;
        manualPitch = 0f;
        ollieChargeStart = -1f;
    }

    private Vector2 DirInput() => new Vector2(steer, throttle);

    private void TryFlip()
    {
        if (Time.time < flipEndTime || grabbing) return;
        Vector2 d = DirInput();

        string name;
        int points;
        AnimationClip clip = null;
        Vector3 euler;
        if (d.y > 0.5f) { name = "Impossible"; points = 150; euler = new Vector3(360f, 0f, 0f); }
        else if (d.y < -0.5f) { name = "Pop Shuvit"; points = 100; euler = new Vector3(0f, 180f, 0f); clip = Board != null ? Board.shuvitClip : null; }
        else if (d.x < -0.5f) { name = "Heelflip"; points = 100; euler = new Vector3(0f, 0f, -360f); }
        else if (d.x > 0.5f) { name = "Varial Kickflip"; points = 200; euler = new Vector3(0f, 180f, 360f); }
        else { name = "Kickflip"; points = 100; euler = new Vector3(0f, 0f, 360f); clip = Board != null ? Board.kickflipClip : null; }

        float duration = 0.45f;
        if (clip != null && Board != null)
        {
            duration = Mathf.Max(0.2f, Board.PlayClip(clip));
            procDuration = 0f;
        }
        else
        {
            procEuler = euler;
            procStart = Time.time;
            procDuration = duration;
        }
        flipEndTime = Time.time + duration;
        flipStartTime = Time.time;
        flipDuration = duration;
        AddTrick(name, points);
    }

    private void UpdateGrab()
    {
        bool held = Input.GetKey(grabKey);
        if (held && !grabbing && Time.time >= flipEndTime)
        {
            Vector2 d = DirInput();
            if (d.y > 0.5f) { grabName = "Nosegrab"; grabTilt = new Vector3(-25f, 0f, 0f); }
            else if (d.y < -0.5f) { grabName = "Tailgrab"; grabTilt = new Vector3(25f, 0f, 0f); }
            else if (d.x < -0.5f) { grabName = "Indy"; grabTilt = new Vector3(0f, 0f, 30f); }
            else if (d.x > 0.5f) { grabName = "Stalefish"; grabTilt = new Vector3(0f, 0f, -30f); }
            else { grabName = "Melon"; grabTilt = new Vector3(-10f, 0f, 20f); }
            grabbing = true;
            grabStart = Time.time;
        }
        else if (!held && grabbing)
        {
            EndGrab();
        }

        if (grabbing)
            LiveTrick = $"{grabName}  {GrabPoints()}";
    }

    private bool FlipUnfinished() =>
        Time.time < flipEndTime - flipLandGrace && FlipProgress < flipBailProgress;

    // Флип почти докручен -- засчитываем и "ловим" доску ногами
    private void FinishFlipNow()
    {
        if (Time.time >= flipEndTime) return;
        flipEndTime = Time.time;
        procDuration = 0f;
        if (Board != null) Board.PlayClip(Board.idleClip);
    }

    private int GrabPoints() => 200 + Mathf.RoundToInt((Time.time - grabStart) * 300f);

    private void EndGrab()
    {
        if (!grabbing) return;
        grabbing = false;
        AddTrick(grabName, GrabPoints());
        LiveTrick = null;
    }

    private bool TryStartGrind()
    {
        Vector3 feet = rb.position - Vector3.up * feetOffset;
        GrindRail bestRail = null;
        float bestDist = grindSnapDistance;
        float bestT = 0f;

        foreach (GrindRail r in GrindRail.All)
        {
            if (r == null || (r == ignoreRail && Time.time < ignoreRailUntil)) continue;
            Vector3 p = r.ClosestPoint(feet, out float t);
            if (feet.y < p.y - 0.5f) continue; // перила выше ног -- не достаём
            float d = Vector3.Distance(feet, p);
            if (d < bestDist)
            {
                bestDist = d;
                bestRail = r;
                bestT = t;
            }
        }
        if (bestRail == null) return false;

        EndGrab();
        if (FlipUnfinished())
        {
            Bail("Не докрутил флип");
            return true;
        }
        FinishFlipNow();

        rail = bestRail;
        railT = bestT;
        Vector3 v = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        float along = Vector3.Dot(v, rail.Direction);
        if (Mathf.Abs(along) < 0.1f) along = Vector3.Dot(heading, rail.Direction);
        railDirSign = along >= 0f ? 1f : -1f;
        grindSpeed = Mathf.Max(v.magnitude, minGrindSpeed);

        Vector2 d2 = DirInput();
        boardYawOffset = 0f;
        if (d2.y > 0.5f) grindName = "Nosegrind";
        else if (d2.y < -0.5f) grindName = "5-0";
        else if (d2.x < -0.5f) { grindName = "Boardslide"; boardYawOffset = 90f; }
        else if (d2.x > 0.5f) { grindName = "Lipslide"; boardYawOffset = -90f; }
        else grindName = "50-50";

        heading = rail.Direction * railDirSign;
        grindStart = Time.time;
        Balance = UnityEngine.Random.Range(-0.15f, 0.15f);
        balanceTimer = 0f;
        rb.useGravity = false;
        CurrentState = State.Grind;
        spinAccum = 0f;
        return true;
    }

    private int GrindPoints() => 100 + Mathf.RoundToInt((Time.time - grindStart) * 200f);

    private void ExitGrind(bool bail)
    {
        if (CurrentState != State.Grind) return;
        AddTrick(grindName, GrindPoints());
        LiveTrick = null;
        ignoreRail = rail;
        ignoreRailUntil = Time.time + 0.5f;
        Vector3 dir = rail != null ? rail.Direction * railDirSign : heading;
        rail = null;
        boardYawOffset = 0f;
        rb.useGravity = true;
        EnterAir(dir * grindSpeed + Vector3.up * 2f);
    }

    private void StartManual()
    {
        InManual = true;
        manualStart = Time.time;
        manualName = throttle < -0.5f ? "Nose Manual" : "Manual";
        Balance = UnityEngine.Random.Range(-0.1f, 0.1f);
        balanceTimer = 0f;
    }

    private int ManualPoints() => 50 + Mathf.RoundToInt((Time.time - manualStart) * 150f);

    private void EndManual(bool landed)
    {
        if (!InManual) return;
        InManual = false;
        AddTrick(manualName, ManualPoints());
        LiveTrick = null;
        if (landed) landTime = Time.time;
    }

    private void UpdateBalance(float input)
    {
        if (!BalanceActive) return;

        balanceTimer += Time.deltaTime;
        float side = Balance == 0f ? (UnityEngine.Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(Balance);
        float drift = (balanceDrift + balanceTimer * balanceDriftGrowth) * side
                      + (Mathf.PerlinNoise(Time.time * 1.7f, 0.3f) - 0.5f) * 1.5f;
        Balance += (drift + input * balanceControl) * Time.deltaTime;

        LiveTrick = CurrentState == State.Grind ? $"{grindName}  {GrindPoints()}" : $"{manualName}  {ManualPoints()}";

        if (Mathf.Abs(Balance) >= 1f)
        {
            if (CurrentState == State.Grind) rb.useGravity = true;
            Bail("Потерял баланс");
        }
    }

    // ================= Очки =================

    private void AddTrick(string name, int points)
    {
        comboTricks.Add(name);
        comboBase += points;
        TrickAdded?.Invoke(name);
    }

    private void TryBankCombo()
    {
        if (comboTricks.Count == 0 || InManual) return;
        if (Time.time - landTime > 0.35f) BankCombo();
    }

    private void BankCombo()
    {
        if (comboTricks.Count == 0) return;
        int value = comboBase * ComboMultiplier;
        TotalScore += value;
        GameLog.Verbose("Skate", $"Комбо засчитано: {value} ({comboTricks.Count} трюков)");
        comboTricks.Clear();
        comboBase = 0;
        ComboLanded?.Invoke(value);
    }

    private void LoseCombo()
    {
        comboTricks.Clear();
        comboBase = 0;
        LiveTrick = null;
    }

    // ================= Визуал и камера =================

    void LateUpdate()
    {
        if (!IsRiding) return;
        float dt = Time.deltaTime;

        Vector3 targetUp = CurrentState == State.Ground ? groundNormal : Vector3.up;
        visualUp = Vector3.Slerp(visualUp, targetUp, dt * 12f);

        Vector3 fwd = Vector3.ProjectOnPlane(heading, visualUp);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(transform.forward, visualUp);
        Quaternion baseRot = Quaternion.LookRotation(fwd.normalized, visualUp);

        float targetPitch = InManual ? (manualName == "Nose Manual" ? 14f : -14f) : 0f;
        manualPitch = Mathf.Lerp(manualPitch, targetPitch, dt * 12f);

        Quaternion trick = Quaternion.identity;
        if (procDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - procStart) / procDuration);
            float e = t * t * (3f - 2f * t);
            trick = t >= 1f ? Quaternion.identity : Quaternion.Euler(procEuler * e);
        }
        Quaternion grab = grabbing ? Quaternion.Euler(grabTilt) : Quaternion.identity;
        Quaternion boardRot = baseRot * Quaternion.Euler(manualPitch, boardYawOffset, 0f) * grab * trick;

        Vector3 feet = transform.position - Vector3.up * feetOffset;
        if (Board != null)
            Board.transform.SetPositionAndRotation(feet, boardRot);

        Quaternion footRot = baseRot * Quaternion.Euler(manualPitch, boardYawOffset, 0f) * grab;
        FootFrameRotation = footRot;
        FootFramePosition = feet + footRot * Vector3.up * boardThickness;
        BoardGroundPosition = feet;

        // Игрок стоит на доске боком, при заряде олли приседает
        // Игрок стоит на доске боком. Model только поворачиваем по горизонтали: в нём лежит
        // физическая капсула, её нельзя ни наклонять, ни поднимать. Подъём на деку и позы -- в KnightAnimator.
        if (model != null)
        {
            Vector3 flatHeading = Vector3.ProjectOnPlane(heading, Vector3.up);
            if (flatHeading.sqrMagnitude > 0.0001f)
                model.rotation = Quaternion.LookRotation(flatHeading.normalized, Vector3.up) * Quaternion.Euler(0f, 90f + boardYawOffset, 0f);
        }

        UpdateCamera(dt);
    }

    private void UpdateCamera(float dt)
    {
        if (look == null) return;

        // Камера сама встаёт за спину, если мышь не трогают
        if (Time.time - lastMouseTime > camFollowDelay && !PlayerInputLock.Locked)
        {
            Vector3 dir = CurrentState == State.Air
                ? Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up)
                : Vector3.ProjectOnPlane(heading, Vector3.up);
            if (dir.sqrMagnitude > 0.5f)
            {
                float targetYaw = Quaternion.LookRotation(dir).eulerAngles.y;
                look.Yaw = Mathf.LerpAngle(look.Yaw, targetYaw, dt * camFollowSpeed);
            }
        }

        if (!thirdPerson) return;

        Transform cam = look.transform;
        Vector3 pivot = transform.position + Vector3.up * camHeight;
        Vector3 back = -cam.forward;
        float dist = camDistance;
        int n = Physics.SphereCastNonAlloc(pivot, 0.25f, back, hits, camDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.transform.IsChildOf(transform)) continue;
            if (Board != null && h.transform.IsChildOf(Board.transform)) continue;
            if (h.distance > 0f && h.distance < dist) dist = h.distance;
        }
        cam.position = pivot + back * Mathf.Max(0.5f, dist - 0.1f);
    }
}
