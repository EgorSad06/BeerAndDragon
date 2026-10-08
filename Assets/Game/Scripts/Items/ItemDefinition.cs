using UnityEngine;

// Описание предмета (ассет). Создаётся через ПКМ > Create > BeerAndDragon > Item
// или автоматически через Tools > BeerAndDragon > Setup Player.
[CreateAssetMenu(menuName = "BeerAndDragon/Item", fileName = "Item")]
public class ItemDefinition : ScriptableObject
{
    public enum ItemKind { Weapon, Consumable }
    public enum ConsumableEffect { Heal, SpeedBoost }
    public enum UseStyle { Instant, Smoke, Drink }

    public string displayName = "Item";
    [TextArea] public string description;
    public Sprite icon;
    public ItemKind kind = ItemKind.Consumable;
    public int maxStack = 1;

    [Header("Weapon")]
    public string weaponObjectName;         // имя дочернего объекта в WeaponHolder ("Shotgun", "Sword")

    [Header("Consumable")]
    public ConsumableEffect effect = ConsumableEffect.Heal;
    public float amount = 25f;              // Heal: сколько HP; SpeedBoost: множитель скорости
    public float duration = 0f;             // Heal: за сколько секунд (0 = сразу); SpeedBoost: длительность
    public AudioClip useSound;

    [Header("Use animation")]
    public UseStyle useStyle = UseStyle.Instant;
    public float useTime = 1f;              // через сколько секунд после нажатия срабатывает эффект
    public GameObject heldPrefab;           // что держим в руке (сигарета, банка)
}
