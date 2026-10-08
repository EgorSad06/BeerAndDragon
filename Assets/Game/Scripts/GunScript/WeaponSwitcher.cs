using System.Collections.Generic;
using UnityEngine;

// Вешается на WeaponHolder. Все прямые дочерние объекты -- это оружие (Weapon, Sword, что угодно).
// Какое оружие достать, решает Inventory (кнопки 1..9 / колесо) через Equip(имя объекта).
// При смене оружие "выезжает" снизу.
public class WeaponSwitcher : MonoBehaviour
{
    [Header("Equip animation")]
    public float equipDropDistance = 0.35f;
    public float equipSpeed = 12f;

    private Transform current;
    private readonly Dictionary<Transform, Vector3> restPositions = new Dictionary<Transform, Vector3>();

    public Transform CurrentWeapon => current;
    public string CurrentName => current != null ? current.name : null;

    void Awake()
    {
        foreach (Transform child in transform)
        {
            restPositions[child] = child.localPosition;
            child.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        // Без инвентаря в сцене -- просто достаём первое оружие
        if (current == null && transform.childCount > 0 && FindFirstObjectByType<Inventory>() == null)
            Equip(transform.GetChild(0).name);
    }

    void Update()
    {
        if (current != null && restPositions.TryGetValue(current, out Vector3 rest))
            current.localPosition = Vector3.Lerp(current.localPosition, rest, Time.deltaTime * equipSpeed);
    }

    // null/пустое имя -- убрать оружие. Возвращает true, если такое оружие есть и оно в руках.
    public bool Equip(string weaponName)
    {
        Transform target = string.IsNullOrEmpty(weaponName) ? null : transform.Find(weaponName);
        if (target == current) return target != null;

        foreach (Transform child in transform)
            child.gameObject.SetActive(child == target);

        current = target;
        if (current != null && restPositions.TryGetValue(current, out Vector3 rest))
            current.localPosition = rest + Vector3.down * equipDropDistance;

        return current != null;
    }
}
