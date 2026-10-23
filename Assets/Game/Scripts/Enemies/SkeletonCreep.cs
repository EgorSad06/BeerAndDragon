using System.Collections.Generic;
using UnityEngine;

// Скелет-крип и скелет-лучник: модели Assets/Game/Assets/enemies/Skeleton и SkeletonArcher
// (их собирают tools/blender/skeleton_psx.py и skeleton_archer.py).
//  - оружие: в модели на кости WeaponSocket.R лежат все одноручные из пака (Weapon_Arming_Sword, Weapon_Hatchet...),
//    включается одно -- заданное в weapon или случайное;
//  - анимации через Animator (контроллер <модель>.controller): Speed -- ходьба, Attack -- удар (у лучника --
//    натянуть и выстрелить; в момент выстрела -- событие ArrowReleased), Die -- смерть;
//  - смерть по Damageable: проигрывается анимация, коллайдеры выключаются, тело удаляет Damageable (destroyDelay).
[RequireComponent(typeof(Animator))]
public class SkeletonCreep : MonoBehaviour
{
    public const string SocketName = "WeaponSocket.R";
    public const string WeaponPrefix = "Weapon_";

    [Tooltip("Оружие без префикса Weapon_ (Arming_Sword, Hatchet, Spiked_Club...). Пусто -- случайное.")]
    public string weapon = "";

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int DieId = Animator.StringToHash("Die");

    private Animator animator;
    private Damageable health;
    private readonly List<GameObject> weapons = new List<GameObject>();

    // Лучник отпустил тетиву (событие анимации Shoot) -- здесь появится летящая стрела
    public event System.Action ArrowReleased;

    public string CurrentWeapon { get; private set; }
    public bool IsDead { get; private set; }

    public IEnumerable<string> WeaponNames
    {
        get { foreach (GameObject w in weapons) yield return w.name.Substring(WeaponPrefix.Length); }
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        health = GetComponent<Damageable>();
        if (health != null) health.Died += OnDied;
        Transform socket = FindDeep(transform, SocketName);
        if (socket != null)
            foreach (Transform child in socket)
                if (child.name.StartsWith(WeaponPrefix)) weapons.Add(child.gameObject);
        SetWeapon(weapon);
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
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

    // Вызывается событием анимации Shoot
    private void OnArrowRelease()
    {
        ArrowReleased?.Invoke();
    }

    private void OnDied()
    {
        IsDead = true;
        animator.SetFloat(SpeedId, 0f);
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
