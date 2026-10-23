using System.Collections.Generic;
using UnityEngine;

// Скелеты-враги: крип, лучник и щитовик -- модели Assets/Game/Assets/enemies/Skeleton, SkeletonArcher, SkeletonShield
// (их собирают tools/blender/skeleton_psx.py, skeleton_archer.py, skeleton_shield.py).
//  - оружие: в модели на кости WeaponSocket.R лежат все одноручные из пака (Weapon_Arming_Sword, Weapon_Hatchet...),
//    включается одно -- заданное в weapon или случайное;
//  - анимации через Animator (контроллер <модель>.controller): Speed -- ходьба, Attack -- удар (у лучника --
//    натянуть и выстрелить; в момент выстрела -- событие ArrowReleased), Die -- смерть;
//  - щит (у щитовика -- объект Shield): раскраска из shieldPaints (заданная или случайная); SetBlocking -- поднять щит
//    (проходит blockedDamage урона, удар по щиту -- анимация BlockHit), Parry -- отбив (parryTime с начала урон 0);
//  - события анимаций: AttackHit (удар достал), ArrowReleased (стрела выпущена) -- их слушает SkeletonAI;
//  - смерть по Damageable: проигрывается анимация, коллайдеры выключаются, тело удаляет Damageable (destroyDelay).
[RequireComponent(typeof(Animator))]
public class SkeletonCreep : MonoBehaviour
{
    public const string SocketName = "WeaponSocket.R";
    public const string WeaponPrefix = "Weapon_";

    [Tooltip("Оружие без префикса Weapon_ (Arming_Sword, Hatchet, Spiked_Club...). Пусто -- случайное.")]
    public string weapon = "";

    [Header("Щит (щитовик)")]
    [Tooltip("Раскраски щита (SKS_Paint1..8). Заполняет SkeletonCreepBuilder.")]
    public Material[] shieldPaints = new Material[0];
    [Tooltip("Номер раскраски (0..7). -1 -- случайная.")]
    public int shieldPaint = -1;
    [Range(0f, 1f)] public float blockedDamage = 0.2f;
    public float parryTime = 0.35f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int DieId = Animator.StringToHash("Die");
    private static readonly int BlockingId = Animator.StringToHash("Blocking");
    private static readonly int ParryId = Animator.StringToHash("Parry");
    private static readonly int BlockHitId = Animator.StringToHash("BlockHit");

    private Animator animator;
    private Damageable health;
    private readonly List<GameObject> weapons = new List<GameObject>();
    private Renderer shield;
    private float parryUntil = -1f;

    // Лучник отпустил тетиву (событие анимации Shoot) -- здесь появится летящая стрела
    public event System.Action ArrowReleased;
    // Удар оружием дошёл до цели (событие анимации Attack) -- здесь наносится урон
    public event System.Action AttackHit;

    public string CurrentWeapon { get; private set; }
    public bool IsDead { get; private set; }
    public bool HasShield => shield != null;
    public bool IsBlocking { get; private set; }
    public bool IsParrying => Time.time < parryUntil;

    public IEnumerable<string> WeaponNames
    {
        get { foreach (GameObject w in weapons) yield return w.name.Substring(WeaponPrefix.Length); }
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        health = GetComponent<Damageable>();
        if (health != null)
        {
            health.Died += OnDied;
            health.Damaged += OnDamaged;
        }
        Transform socket = FindDeep(transform, SocketName);
        if (socket != null)
            foreach (Transform child in socket)
                if (child.name.StartsWith(WeaponPrefix)) weapons.Add(child.gameObject);
        SetWeapon(weapon);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))   // меш Shield (есть и кость Shield)
            if (r.name == "Shield") shield = r;
        SetShieldPaint(shieldPaint);
    }

    void Update()
    {
        if (health != null && HasShield)
            health.damageMultiplier = Time.time < parryUntil ? 0f : IsBlocking ? blockedDamage : 1f;
    }

    void OnDestroy()
    {
        if (health == null) return;
        health.Died -= OnDied;
        health.Damaged -= OnDamaged;
    }

    // Раскраска щита по номеру; вне диапазона -- случайная
    public void SetShieldPaint(int index)
    {
        if (shield == null || shieldPaints.Length == 0) return;
        if (index < 0 || index >= shieldPaints.Length) index = Random.Range(0, shieldPaints.Length);
        Material[] mats = shield.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].name.StartsWith("SKS_Paint")) mats[i] = shieldPaints[index];
        shield.sharedMaterials = mats;
        shieldPaint = index;
    }

    // Поднять/опустить щит
    public void SetBlocking(bool on)
    {
        if (!HasShield || IsDead) return;
        IsBlocking = on;
        animator.SetBool(BlockingId, on);
    }

    // Отбив щитом: первые parryTime секунд урон не проходит
    public void Parry()
    {
        if (!HasShield || IsDead) return;
        parryUntil = Time.time + parryTime;
        animator.SetTrigger(ParryId);
    }

    private void OnDamaged(float amount)
    {
        if (IsBlocking && Time.time >= parryUntil) animator.SetTrigger(BlockHitId);
    }

    // Включает оружие по имени; неизвестное имя или пустая строка -- случайное
    public void SetWeapon(string weaponName)
    {
        if (weapons.Count == 0) return;
        GameObject chosen = weapons.Find(w => w.name == WeaponPrefix + weaponName)
                            ?? weapons[Random.Range(0, weapons.Count)];
        foreach (GameObject w in weapons) w.SetActive(w == chosen);
        CurrentWeapon = chosen.name.Substring(WeaponPrefix.Length);
    }

    // Скорость движения в м/с: больше 0.1 -- анимация ходьбы
    public void SetSpeed(float metersPerSecond)
    {
        if (!IsDead) animator.SetFloat(SpeedId, metersPerSecond);
    }

    public void Attack()
    {
        if (!IsDead) animator.SetTrigger(AttackId);
    }

    // Длина клипа контроллера (сколько займёт атака), 0 -- нет такого
    public float ClipLength(string clipName)
    {
        foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
            if (c.name == clipName) return c.length;
        return 0f;
    }

    // Вызывается событием анимации Shoot
    private void OnArrowRelease()
    {
        ArrowReleased?.Invoke();
    }

    // Вызывается событием анимации Attack
    private void OnAttackHit()
    {
        if (!IsDead) AttackHit?.Invoke();
    }

    private void OnDied()
    {
        IsDead = true;
        IsBlocking = false;
        if (health != null) health.damageMultiplier = 1f;
        animator.SetFloat(SpeedId, 0f);
        if (HasShield) animator.SetBool(BlockingId, false);
        animator.SetTrigger(DieId);
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
