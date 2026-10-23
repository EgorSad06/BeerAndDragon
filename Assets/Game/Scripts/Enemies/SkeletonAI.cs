using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ИИ скелетов. Одна логика на три роли (роль и скорости ставит SkeletonCreepBuilder):
//  - Melee (крип): заметил игрока -> бежит к нему, вблизи шаркает шагом -> в досягаемости бьёт (одна из атак);
//    урон -- в момент удара (событие AttackHit);
//  - Shield (щитовик): как крип, но между ударами обходит игрока боком; видит меч игрока: замах -- поднимает щит,
//    начало удара -- иногда парирует и сразу бьёт в ответ; получил удар без щита -- может закрыться;
//  - Archer (лучник): держит дистанцию minRange..maxRange (подходит/отступает), между выстрелами смещается боком;
//    стреляет стрелой SkeletonArrow с упреждением и навесом (событие ArrowReleased); без прямой видимости подходит.
// Получив удар, вздрагивает (не чаще раза в hitReactCooldown, прерывает атаку). В покое крип иногда почёсывается.
// Замечает игрока в sightRange при прямой видимости или когда его ранили; теряет дальше loseRange.
// Ходит CharacterController-ом с гравитацией; если в сцене запечён NavMesh -- по пути в обход препятствий,
// иначе напрямую. Скорости -- как шаг в анимациях (ноги не скользят). Скелеты расталкиваются.
[RequireComponent(typeof(SkeletonCreep), typeof(CharacterController))]
public class SkeletonAI : MonoBehaviour
{
    public enum Role { Melee, Shield, Archer }

    public Role role = Role.Melee;

    [Header("Чутьё")]
    public float sightRange = 16f;
    public float loseRange = 26f;
    public float eyeHeight = 1.6f;

    [Header("Ходьба (м/с -- как шаг в анимациях)")]
    public float walkSpeed = 1.2f;
    public float runSpeed = 0f;             // 0 -- не бегает
    public float runDistance = 6f;          // дальше -- бежит
    public float strafeSpeed = 0f;          // 0 -- не ходит боком
    public float turnSpeed = 300f;          // град/с
    public float personalSpace = 0.9f;      // расталкивание с другими скелетами

    [Header("Ближний бой")]
    public float attackRange = 1.6f;        // с какого расстояния начинать удар
    public float hitReach = 2.1f;           // докуда достаёт удар
    public float hitAngle = 70f;
    public float damage = 12f;
    public float attackCooldown = 1.0f;

    [Header("Реакции")]
    public float hitReactCooldown = 1.2f;
    public Vector2 fidgetEvery = new Vector2(7f, 14f);   // крип в покое почёсывается раз в столько секунд

    [Header("Щит")]
    public float threatRange = 3.5f;        // с какого расстояния следит за мечом игрока
    [Range(0f, 1f)] public float parryChance = 0.35f;
    [Range(0f, 1f)] public float blockOnHitChance = 0.5f;
    public float blockHold = 0.8f;          // сколько держит щит после угрозы

    [Header("Лук")]
    public SkeletonArrow arrowPrefab;
    public float minRange = 5f;
    public float maxRange = 14f;
    public float arrowSpeed = 28f;
    public float arrowDamage = 10f;
    public float shootCooldown = 1.2f;

    private static readonly List<SkeletonAI> All = new List<SkeletonAI>();
    private static PlayerHealth cachedPlayer;
    private static float nextPlayerSearch;

    private SkeletonCreep creep;
    private CharacterController controller;
    private Damageable health;
    private Transform arrowNock;
    private NavMeshPath path;
    private float nextRepath;
    private bool aggro;
    private bool canSee;
    private float nextSightCheck;
    private float busyUntil;
    private float nextAttack;
    private float blockUntil;
    private float nextHitReact;
    private float nextFidget;
    private float strafeSide = 1f;
    private float nextStrafeFlip;
    private bool wasSwinging;
    private float swingRoll;
    private bool parriedThisSwing;

    public bool Aggro => aggro;

    void Awake()
    {
        creep = GetComponent<SkeletonCreep>();
        controller = GetComponent<CharacterController>();
        health = GetComponent<Damageable>();
        path = new NavMeshPath();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == "ArrowNock") arrowNock = t;
        nextFidget = Time.time + Random.Range(fidgetEvery.x, fidgetEvery.y);
    }

    void OnEnable()
    {
        All.Add(this);
        creep.AttackHit += OnAttackHit;
        creep.ArrowReleased += OnArrowReleased;
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        All.Remove(this);
        creep.AttackHit -= OnAttackHit;
        creep.ArrowReleased -= OnArrowReleased;
        if (health != null) health.Damaged -= OnDamaged;
    }

    void Update()
    {
        if (creep.IsDead)
        {
            enabled = false;
            return;
        }
        PlayerHealth player = FindPlayer();
        if (player == null || player.IsDead)
        {
            aggro = false;
            Calm();
            creep.SetBlocking(false);
            return;
        }

        Vector3 target = player.transform.position;
        float dist = Flat(target - transform.position).magnitude;
        if (Time.time >= nextSightCheck)
        {
            nextSightCheck = Time.time + 0.25f;
            canSee = LineOfSight(player);
        }
        if (!aggro && dist < sightRange && canSee) aggro = true;
        if (aggro && dist > loseRange) aggro = false;
        if (!aggro)
        {
            Calm();
            return;
        }

        if (role == Role.Archer) ArcherUpdate(target, dist);
        else MeleeUpdate(player, target, dist);
    }

    // Покой: стоит, крип иногда почёсывается
    private void Calm()
    {
        Stand();
        if (Time.time < nextFidget) return;
        nextFidget = Time.time + Random.Range(fidgetEvery.x, fidgetEvery.y);
        if (role == Role.Melee) creep.Fidget();
    }

    // ---------------------------------------------------------------- ближний бой и щит
    private void MeleeUpdate(PlayerHealth player, Vector3 target, float dist)
    {
        bool busy = Time.time < busyUntil;
        bool shielded = role == Role.Shield && ShieldUpdate(player, dist, busy);
        if (busy || shielded)
        {
            Stand();
            Face(target);
            return;
        }
        if (dist > attackRange)
        {
            MoveTowards(target, dist > runDistance && runSpeed > 0f ? runSpeed : walkSpeed);
            return;
        }
        if (Time.time >= nextAttack && FacingAngle(target) < 30f)
        {
            Stand();
            busyUntil = Time.time + creep.Attack();
            nextAttack = busyUntil + attackCooldown * Random.Range(0.6f, 1.3f);
            return;
        }
        // ждёт удара: щитовик обходит игрока боком, крип стоит лицом к нему
        if (strafeSpeed > 0f && dist > attackRange * 0.6f) Circle(target);
        else
        {
            Stand();
            Face(target);
        }
    }

    // Возвращает true, пока щитовик закрывается или парирует (тогда он не ходит и не бьёт)
    private bool ShieldUpdate(PlayerHealth player, float dist, bool attacking)
    {
        Sword sword = player.GetComponentInChildren<Sword>();
        bool active = sword != null && sword.isActiveAndEnabled;
        bool swinging = active && sword.IsSwinging;
        bool charging = active && sword.IsCharging;
        if (swinging && !wasSwinging)
        {
            swingRoll = Random.value;      // одно решение «парировать или нет» на каждый взмах
            parriedThisSwing = false;
        }
        wasSwinging = swinging;

        SkeletonTactics.ShieldReaction reaction = SkeletonTactics.ReactToThreat(
            charging, swinging, active ? sword.SwingProgress : 0f, active ? sword.hitMoment : 0f,
            dist, threatRange, parryChance, swingRoll);
        if (!attacking && reaction == SkeletonTactics.ShieldReaction.Parry && !parriedThisSwing)
        {
            parriedThisSwing = true;
            creep.SetBlocking(false);
            creep.Parry();
            busyUntil = Time.time + creep.ClipLength("Parry");
            nextAttack = busyUntil;            // сразу после отбива -- ответный удар
            return true;
        }
        if (reaction != SkeletonTactics.ShieldReaction.None) blockUntil = Time.time + blockHold;
        bool block = !attacking && Time.time < blockUntil;
        creep.SetBlocking(block);
        return block || creep.IsParrying;
    }

    private void OnAttackHit()
    {
        PlayerHealth player = FindPlayer();
        if (player == null || player.IsDead) return;
        if (SkeletonTactics.InStrike(transform.position, transform.forward, player.transform.position, hitReach, hitAngle))
            player.TakeDamage(damage);
    }

    private void OnDamaged(float amount)
    {
        aggro = true;                           // ранили издалека -- всё равно идёт мстить
        if (creep.IsBlocking || creep.IsParrying) return;
        if (role == Role.Shield && Random.value < blockOnHitChance) blockUntil = Time.time + 1.5f;
        if (Time.time >= nextHitReact)
        {
            nextHitReact = Time.time + hitReactCooldown;
            busyUntil = Time.time + creep.Hit() * 0.8f;   // удар прерывает атаку
        }
    }

    // ---------------------------------------------------------------- лучник
    private void ArcherUpdate(Vector3 target, float dist)
    {
        if (Time.time < busyUntil)
        {
            Stand();
            Face(target);                       // натягивая лук, доворачивается за игроком
            return;
        }
        SkeletonTactics.RangeMove move = SkeletonTactics.ArcherMove(dist, minRange, maxRange);
        if (move == SkeletonTactics.RangeMove.Advance || !canSee)
        {
            MoveTowards(target, walkSpeed);
            return;
        }
        if (move == SkeletonTactics.RangeMove.Retreat)
        {
            Move(Flat(transform.position - target).normalized * walkSpeed, null);
            return;
        }
        if (Time.time >= nextAttack && FacingAngle(target) < 15f)
        {
            Stand();
            busyUntil = Time.time + creep.Attack();
            nextAttack = busyUntil + shootCooldown * Random.Range(0.7f, 1.4f);
            return;
        }
        if (strafeSpeed > 0f) Circle(target);   // между выстрелами смещается боком
        else
        {
            Stand();
            Face(target);
        }
    }

    private void OnArrowReleased()
    {
        PlayerHealth player = FindPlayer();
        if (arrowPrefab == null || player == null) return;
        Vector3 from = arrowNock != null ? arrowNock.position : transform.position + Vector3.up * 1.5f;
        Vector3 chest = player.transform.position + Vector3.up * 1.1f;
        Rigidbody body = player.GetComponent<Rigidbody>();
        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        Vector3 dir = SkeletonTactics.AimDirection(from, chest, velocity, arrowSpeed, arrowPrefab.gravity);
        SkeletonArrow arrow = Instantiate(arrowPrefab, from, Quaternion.LookRotation(dir));
        arrow.Launch(dir * arrowSpeed, arrowDamage, transform);
    }

    // ---------------------------------------------------------------- движение
    private void MoveTowards(Vector3 target, float speed)
    {
        Vector3 next = target;
        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + 0.5f;
            if (!NavMesh.CalculatePath(transform.position, target, NavMesh.AllAreas, path)) path.ClearCorners();
        }
        if (path.corners.Length > 1)
        {
            next = path.corners[1];
            if (Flat(next - transform.position).magnitude < 0.3f && path.corners.Length > 2) next = path.corners[2];
        }
        Move(Flat(next - transform.position).normalized * speed, null);
    }

    // Обход цели боком (лицом к ней); направление меняется раз в 1.5..3.5 с
    private void Circle(Vector3 target)
    {
        if (Time.time >= nextStrafeFlip)
        {
            nextStrafeFlip = Time.time + Random.Range(1.5f, 3.5f);
            strafeSide = Random.value < 0.5f ? -1f : 1f;
        }
        Vector3 toTarget = Flat(target - transform.position).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget) * strafeSide;
        Move(side * strafeSpeed, target);
    }

    // velocity -- куда идти (м/с); lookAt -- куда смотреть (null -- по ходу)
    private void Move(Vector3 velocity, Vector3? lookAt)
    {
        Vector3 push = Vector3.zero;              // расталкивание с соседями
        foreach (SkeletonAI other in All)
        {
            if (other == this || other.creep.IsDead) continue;
            Vector3 d = Flat(transform.position - other.transform.position);
            if (d.magnitude < personalSpace && d.sqrMagnitude > 0.0001f)
                push += d.normalized * (1f - d.magnitude / personalSpace);
        }
        float speed = velocity.magnitude;
        Vector3 v = speed > 0.0001f ? (velocity / speed + push).normalized * speed : Vector3.zero;
        controller.SimpleMove(v);
        if (lookAt.HasValue) Face(lookAt.Value);
        else if (v.sqrMagnitude > 0.0001f) TurnTo(v);
        // анимация -- по скорости относительно взгляда (шаг вперёд, назад, вбок)
        creep.SetMove(Vector3.Dot(v, transform.forward), Vector3.Dot(v, transform.right));
    }

    private void Stand()
    {
        if (controller.enabled) controller.SimpleMove(Vector3.zero);   // гравитация
        creep.SetMove(0f, 0f);
    }

    private void Face(Vector3 target) => TurnTo(Flat(target - transform.position));

    private void TurnTo(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
    }

    private float FacingAngle(Vector3 target) => Vector3.Angle(Flat(transform.forward), Flat(target - transform.position));

    private bool LineOfSight(PlayerHealth player)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 to = player.transform.position + Vector3.up * 1.2f;
        foreach (RaycastHit h in Physics.RaycastAll(eye, (to - eye).normalized, Vector3.Distance(eye, to), ~0,
                                                     QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.transform.IsChildOf(player.transform)) continue;
            if (h.collider.GetComponentInParent<SkeletonCreep>() != null) continue;   // свои не заслоняют
            return false;
        }
        return true;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static PlayerHealth FindPlayer()
    {
        if (cachedPlayer == null && Time.time >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.time + 1f;
            cachedPlayer = FindFirstObjectByType<PlayerHealth>();
        }
        return cachedPlayer;
    }
}
