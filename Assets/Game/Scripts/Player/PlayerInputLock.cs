using UnityEngine;

// Глобальный флаг "игрок сейчас в меню/инвентаре/мёртв": оружие не стреляет, камера не крутится.
public static class PlayerInputLock
{
    public static bool Locked;

    // Оружие доставать нельзя (например, игрок на скейте), расходники -- можно
    public static bool WeaponsBlocked;

    // Сбрасываем при старте игры (на случай выключенного Domain Reload)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Locked = false;
        WeaponsBlocked = false;
    }
}
