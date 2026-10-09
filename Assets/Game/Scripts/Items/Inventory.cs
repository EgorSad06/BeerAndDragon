using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventorySlot
{
    public ItemDefinition item;
    public int count;

    public bool IsEmpty => item == null || count <= 0;
    public void Clear() { item = null; count = 0; }
}

[Serializable]
public struct ItemStack
{
    public ItemDefinition item;
    public int count;
}

// Инвентарь игрока. Слоты [0..hotbarSize) -- хотбар сверху экрана (кнопки 1..9),
// остальные -- рюкзак, виден только в открытом инвентаре (Tab).
//
// Кнопка на слоте с оружием -- достать это оружие.
// Кнопка на слоте с расходником -- сразу использовать (оружие остаётся в руках).
public class Inventory : MonoBehaviour
{
    public int hotbarSize = 9;
    public int backpackSize = 18;
    public List<ItemStack> startingItems = new List<ItemStack>();

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.Tab;
    public bool scrollSwitchesWeapons = true;

    [Header("Refs (найдутся сами)")]
    public WeaponSwitcher weaponSwitcher;
    public PlayerHealth health;
    public PlayerStatusEffects effects;
    public ItemUser itemUser;
    public AudioSource audioSource;

    public InventorySlot[] Slots { get; private set; }
    public int SelectedHotbar { get; private set; } = -1;
    public bool IsOpen { get; private set; }
    public int HotbarSize => hotbarSize;

    public event Action Changed;
    public event Action<bool> OpenChanged;

    void Awake()
    {
        Slots = new InventorySlot[hotbarSize + backpackSize];
        for (int i = 0; i < Slots.Length; i++) Slots[i] = new InventorySlot();

        if (weaponSwitcher == null) weaponSwitcher = GetComponentInChildren<WeaponSwitcher>(true);
        if (health == null) health = GetComponent<PlayerHealth>();
        if (effects == null) effects = GetComponent<PlayerStatusEffects>();
        if (itemUser == null) itemUser = GetComponent<ItemUser>();
    }

    // Сейчас курим/пьём -- оружие и другие предметы не трогаем
    public bool IsBusy => itemUser != null && itemUser.IsUsing;

    void Start()
    {
        foreach (ItemStack s in startingItems)
            if (s.item != null) Add(s.item, Mathf.Max(1, s.count), silent: true);

        RefreshSelection();
    }

    void Update()
    {
        if (health != null && health.IsDead) return;

        if (Input.GetKeyDown(toggleKey) || (IsOpen && Input.GetKeyDown(KeyCode.Escape)))
            SetOpen(!IsOpen);

        if (IsOpen) return;

        for (int i = 0; i < Mathf.Min(hotbarSize, 9); i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                SelectHotbar(i);

        if (scrollSwitchesWeapons)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0.1f) CycleWeapon(-1);
            else if (scroll < -0.1f) CycleWeapon(1);
        }
    }

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        PlayerInputLock.Locked = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        OpenChanged?.Invoke(open);
    }

    // Кладёт предметы: сначала докидывает в неполные стопки, потом в пустые слоты (хотбар первым).
    // Возвращает, сколько НЕ влезло.
    public int Add(ItemDefinition item, int count, bool silent = false)
    {
        if (item == null || count <= 0) return count;
        int left = count;
        int max = Mathf.Max(1, item.maxStack);

        for (int i = 0; i < Slots.Length && left > 0; i++)
        {
            InventorySlot s = Slots[i];
            if (s.item != item || s.count >= max) continue;
            int put = Mathf.Min(max - s.count, left);
            s.count += put;
            left -= put;
        }
        for (int i = 0; i < Slots.Length && left > 0; i++)
        {
            InventorySlot s = Slots[i];
            if (!s.IsEmpty) continue;
            int put = Mathf.Min(max, left);
            s.item = item;
            s.count = put;
            left -= put;
        }

        int added = count - left;
        if (added > 0)
        {
            GameLog.Info("Inventory", $"Добавлено {added} x {item.name} (не влезло: {left})");
            if (!silent) HudMessages.Post($"+{added} {item.displayName}");
            if (item.kind == ItemDefinition.ItemKind.Weapon && SelectedHotbar < 0)
                RefreshSelection();
            Changed?.Invoke();
        }
        else if (!silent)
        {
            HudMessages.Post("Инвентарь забит");
        }
        return left;
    }

    public void SelectHotbar(int index)
    {
        if (index < 0 || index >= hotbarSize) return;
        InventorySlot s = Slots[index];
        if (s.IsEmpty) return;

        if (s.item.kind == ItemDefinition.ItemKind.Weapon)
        {
            if (!PlayerInputLock.WeaponsBlocked && !IsBusy) EquipSlot(index);
        }
        else
            Use(index);
    }

    private void EquipSlot(int index)
    {
        if (weaponSwitcher == null) return;
        if (weaponSwitcher.Equip(Slots[index].item.weaponObjectName))
        {
            SelectedHotbar = index;
            Changed?.Invoke();
        }
    }

    private void CycleWeapon(int dir)
    {
        if (IsBusy) return;
        if (PlayerInputLock.WeaponsBlocked) return;
        int start = SelectedHotbar < 0 ? (dir > 0 ? -1 : 0) : SelectedHotbar;
        for (int step = 1; step <= hotbarSize; step++)
        {
            int i = ((start + dir * step) % hotbarSize + hotbarSize) % hotbarSize;
            InventorySlot s = Slots[i];
            if (!s.IsEmpty && s.item.kind == ItemDefinition.ItemKind.Weapon)
            {
                EquipSlot(i);
                return;
            }
        }
    }

    // Использовать расходник из слота. false -- не использовали (например, HP и так полное).
    public bool Use(int index)
    {
        if (index < 0 || index >= Slots.Length) return false;
        InventorySlot s = Slots[index];
        if (s.IsEmpty || s.item.kind != ItemDefinition.ItemKind.Consumable) return false;

        ItemDefinition item = s.item;
        if (IsBusy) return false;
        if (!CanApply(item, true)) return false;

        // С анимацией: эффект и списание -- в конце, через item.useTime (см. ItemUser)
        if (itemUser != null && item.useStyle != ItemDefinition.UseStyle.Instant && item.useTime > 0f)
            return itemUser.Begin(item, index);

        CompleteUse(item, index);
        return true;
    }

    public bool CanApply(ItemDefinition item, bool showMessage)
    {
        switch (item.effect)
        {
            case ItemDefinition.ConsumableEffect.Heal:
                if (health != null && health.CanHeal) return true;
                if (showMessage) HudMessages.Post("Здоровье и так полное");
                return false;
            case ItemDefinition.ConsumableEffect.SpeedBoost:
                return effects != null;
        }
        return false;
    }

    // Применить эффект и списать один предмет (из preferIndex, а если его туда уже нет -- из любого слота)
    public void CompleteUse(ItemDefinition item, int preferIndex)
    {
        int index = preferIndex >= 0 && preferIndex < Slots.Length && Slots[preferIndex].item == item
            ? preferIndex
            : System.Array.FindIndex(Slots, sl => sl.item == item && sl.count > 0);
        if (index < 0)
        {
            GameLog.Warn("Inventory", $"CompleteUse: предмета {item.name} уже нет в инвентаре");
            return; // предмет куда-то делся, пока курили -- халявы нет
        }
        GameLog.Info("Inventory", $"Использован {item.name} (эффект {item.effect}, {item.amount})");

        switch (item.effect)
        {
            case ItemDefinition.ConsumableEffect.Heal:
                if (health != null) health.HealOverTime(item.amount, item.duration);
                break;
            case ItemDefinition.ConsumableEffect.SpeedBoost:
                if (effects != null) effects.ApplySpeedBoost(item.amount, item.duration);
                break;
        }

        if (audioSource != null && item.useSound != null)
            audioSource.PlayOneShot(item.useSound);

        InventorySlot s = Slots[index];
        s.count--;
        if (s.count <= 0) s.Clear();
        HudMessages.Post($"{item.displayName}: использовано");
        Changed?.Invoke();
    }

    // Поменять местами (или слить одинаковые стопки)
    public void Swap(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= Slots.Length || b >= Slots.Length) return;
        InventorySlot sa = Slots[a], sb = Slots[b];

        if (!sa.IsEmpty && sa.item == sb.item && sa.item.maxStack > 1)
        {
            int put = Mathf.Min(sa.item.maxStack - sb.count, sa.count);
            sb.count += put;
            sa.count -= put;
            if (sa.count <= 0) sa.Clear();
        }
        else
        {
            (sa.item, sb.item) = (sb.item, sa.item);
            (sa.count, sb.count) = (sb.count, sa.count);
        }

        RefreshSelection();
        Changed?.Invoke();
    }

    // После перестановок: выделение следует за оружием в руках. Если оружие ушло
    // из хотбара в рюкзак -- берём первое оружие из хотбара (или убираем всё).
    private void RefreshSelection()
    {
        string current = weaponSwitcher != null ? weaponSwitcher.CurrentName : null;
        int firstWeapon = -1;
        if (PlayerInputLock.WeaponsBlocked) return; // на скейте оружие убрано -- не достаём его обратно

        for (int i = 0; i < hotbarSize; i++)
        {
            InventorySlot s = Slots[i];
            if (s.IsEmpty || s.item.kind != ItemDefinition.ItemKind.Weapon) continue;
            if (firstWeapon < 0) firstWeapon = i;
            if (current != null && s.item.weaponObjectName == current)
            {
                SelectedHotbar = i;
                return;
            }
        }

        SelectedHotbar = -1;
        if (firstWeapon >= 0)
            EquipSlot(firstWeapon);
        else if (weaponSwitcher != null)
            weaponSwitcher.Equip(null);
    }
}
