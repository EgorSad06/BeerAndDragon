using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ИИ скелетов. Одна логика на три роли (роль ставит SkeletonCreepBuilder):
//  - Melee (крип): заметил игрока -> идёт к нему -> в досягаемости бьёт; урон -- в момент удара (событие AttackHit);
//  - Shield (щитовик): как крип, но видит меч игрока: замах -- поднимает щит, начало удара -- иногда парирует
//    и сразу бьёт в ответ; получил удар без щита -- может закрыться;
//  - Archer (лучник): держит дистанцию minRange..maxRange (подходит/отступает), стреляет стрелой SkeletonArrow
//    с упреждением и навесом (событие ArrowReleased), без прямой видимости подходит.
// Замечает игрока в sightRange при прямой видимости или когда его ранили; теряет дальше loseRange.
// Ходит CharacterController-ом с гравитацией; если в сцене запечён NavMesh -- по пути в обход препятствий,
// иначе напрямую. Скелеты расталкиваются, чтобы не стоять в одной точке.
[RequireComponent(typeof(SkeletonCreep), typeof(CharacterController))]
public class SkeletonAI : MonoBehaviour
{
    public enum Role { Melee, Shield, Archer }

    public Role role = Role.Melee;

    [Header("Чутьё")]
    public float sightRange = 16f;
    public float loseRange = 26f;
    public float eyeHeight = 1.6f;

    [Header("Ходьба")]
    public float walkSpeed = 1.2f;
    public float turnSpeed = 300f;          // град/с
    public float personalSpace = 0.9f;      // расталкивание с другими скелетами

    [Header("Ближний бой")]
    public float attackRange = 1.6f;        // с какого расстояния начинать удар
    public float hitReach = 2.1f;           // докуда достаёт удар
    public float hitAngle = 70f;
    public float damage = 12f;
    public float attackCooldown = 1.0f;

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
            Stand();
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
            Stand();
            return;
        }

        if (role == Role.Archer) ArcherUpdate(player, target, dist);
        else MeleeUpdate(player, target, dist);
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
            MoveTowards(target);
            return;
        }
        Stand();
        Face(target);
        if (Time.time >= nextAttack && FacingAngle(target) < 30f)
        {
            creep.Attack();
            busyUntil = Time.time + creep.ClipLength("Attack");
            nextAttack = busyUntil + attackCooldown * Random.Range(0.6f, 1.3f);
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
        if (role == Role.Shield && !creep.IsBlocking && !creep.IsParrying && Random.value < blockOnHitChance)
            blockUntil = Time.time + 1.5f;
    }

    // ---------------------------------------------------------------- лучник
    private void ArcherUpdate(PlayerHealth player, Vector3 target, float dist)
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
            MoveTowards(target);
            return;
        }
        if (move == SkeletonTactics.RangeMove.Retreat)
        {
            Move(Flat(transform.position - target).normalized);
            return;
        }
        Stand();
        Face(target);
        if (Time.time >= nextAttack && FacingAngle(target) < 15f)
        {
            creep.Attack();
            busyUntil = Time.time + creep.ClipLength("Draw") + creep.ClipLength("Shoot");
            nextAttack = busyUntil + shootCooldown * Random.Range(0.7f, 1.4f);
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
    private void MoveTowards(Vector3 target)
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
        Move(Flat(next - transform.position).normalized);
    }

    private void Move(Vector3 dir)
    {
        Vector3 push = Vector3.zero;              // расталкивание с соседями
        foreach (SkeletonAI other in All)
        {
            if (other == this || other.creep.IsDead) continue;
            Vector3 d = Flat(transform.position - other.transform.position);
            if (d.magnitude < personalSpace && d.sqrMagnitude > 0.0001f)
                push += d.normalized * (1f - d.magnitude / personalSpace);
        }
        Vector3 velocity = (dir + push).normalized * walkSpeed;
        controller.SimpleMove(velocity);
        creep.SetSpeed(walkSpeed);
        if (dir.sqrMagnitude > 0.0001f) TurnTo(dir);
    }

    private void Stand()
    {
        if (controller.enabled) controller.SimpleMove(Vector3.zero);   // гравитация
        creep.SetSpeed(0f);
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
