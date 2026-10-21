using System;

/// <summary>Исход нажатия «выстрел» для <see cref="AmmoMagazine.TryFire"/>.</summary>
public enum FireResult
{
    /// <summary>Выстрел произошёл, из магазина ушёл 1 патрон.</summary>
    Fired,
    /// <summary>Магазин пуст: выстрела нет, счётчики не меняются (щелчок — FR-WPN-03.1).</summary>
    Empty,
    /// <summary>Идёт перезарядка: выстрела нет, состояние не меняется.</summary>
    Blocked
}

/// <summary>
/// Магазин и запас патронов оружия — правила FR-WPN-01, 03, 03.1, 04, 07 (спецификация 1.0, раздел 5.2).
/// Чистый C# без Unity: время передаётся параметром <c>now</c> в секундах, поэтому правила проверяются
/// тестами без сцены и без ожидания. <see cref="Weapon"/> передаёт сюда <c>Time.time</c>.
/// </summary>
/// <remarks>
/// Перезарядка: <see cref="TryStartReload"/> запоминает момент окончания, <see cref="Tick"/> переносит патроны,
/// когда этот момент наступил (граница включается: при <c>now == start + ReloadTime</c> патроны уже в магазине).
/// В режиме «по одному» (помпа) каждый патрон заряжается через <see cref="ReloadTime"/>.
/// Стартовый запас может быть больше <see cref="MaxReserve"/> — тогда коробки не подбираются, пока запас не станет меньше.
/// </remarks>
public sealed class AmmoMagazine
{
    private float nextLoadAt;

    /// <summary>Вместимость магазина (≥ 1).</summary>
    public int MagazineSize { get; }
    /// <summary>Предел запаса при подборе коробок (≥ 0).</summary>
    public int MaxReserve { get; }
    /// <summary>Время перезарядки (или одного патрона в режиме «по одному»), секунды, ≥ 0.</summary>
    public float ReloadTime { get; }
    /// <summary>Бесконечный запас: перезарядка не тратит запас, коробки не подбираются.</summary>
    public bool InfiniteReserve { get; }
    /// <summary>Заряжать по одному патрону (помпа).</summary>
    public bool OneByOne { get; }

    /// <summary>Патронов в магазине.</summary>
    public int Magazine { get; private set; }
    /// <summary>Патронов в запасе.</summary>
    public int Reserve { get; private set; }
    /// <summary>Идёт перезарядка.</summary>
    public bool IsReloading { get; private set; }

    /// <summary>Создаёт полный магазин и заданный запас.</summary>
    /// <param name="magazineSize">Вместимость магазина, ≥ 1.</param>
    /// <param name="reserve">Стартовый запас, ≥ 0.</param>
    /// <param name="maxReserve">Предел запаса при подборе, ≥ 0.</param>
    /// <param name="reloadTime">Время перезарядки, секунды, ≥ 0.</param>
    /// <param name="infiniteReserve">Бесконечный запас.</param>
    /// <param name="oneByOne">Заряжать по одному патрону.</param>
    /// <exception cref="ArgumentOutOfRangeException">Параметр вне допустимого диапазона.</exception>
    public AmmoMagazine(int magazineSize, int reserve, int maxReserve, float reloadTime,
                        bool infiniteReserve = false, bool oneByOne = false)
    {
        if (magazineSize < 1) throw new ArgumentOutOfRangeException(nameof(magazineSize), magazineSize, "магазин должен вмещать хотя бы 1 патрон");
        if (reserve < 0) throw new ArgumentOutOfRangeException(nameof(reserve), reserve, "запас не может быть отрицательным");
        if (maxReserve < 0) throw new ArgumentOutOfRangeException(nameof(maxReserve), maxReserve, "предел запаса не может быть отрицательным");
        if (!(reloadTime >= 0f)) throw new ArgumentOutOfRangeException(nameof(reloadTime), reloadTime, "время перезарядки должно быть ≥ 0");

        MagazineSize = magazineSize;
        MaxReserve = maxReserve;
        ReloadTime = reloadTime;
        InfiniteReserve = infiniteReserve;
        OneByOne = oneByOne;
        Magazine = magazineSize;
        Reserve = reserve;
    }

    /// <summary>Нажатие «выстрел» (FR-WPN-01, 03, 03.1).</summary>
    /// <returns><see cref="FireResult.Fired"/> — магазин уменьшен на 1; иначе состояние не меняется.</returns>
    public FireResult TryFire()
    {
        if (IsReloading) return FireResult.Blocked;
        if (Magazine <= 0) return FireResult.Empty;
        Magazine--;
        return FireResult.Fired;
    }

    /// <summary>Нажатие «перезарядка» или автоперезарядка после опустошения магазина (FR-WPN-03, 03.1).</summary>
    /// <param name="now">Текущее время, секунды.</param>
    /// <returns><c>true</c>, если перезарядка началась; <c>false</c> — уже идёт, магазин полон или запас пуст.</returns>
    public bool TryStartReload(float now)
    {
        if (IsReloading || Magazine >= MagazineSize) return false;
        if (!InfiniteReserve && Reserve <= 0) return false;
        IsReloading = true;
        nextLoadAt = now + ReloadTime;
        return true;
    }

    /// <summary>Продвигает перезарядку ко времени <paramref name="now"/>.</summary>
    /// <param name="now">Текущее время, секунды.</param>
    /// <returns>Сколько патронов перенесено в магазин за этот вызов.</returns>
    public int Tick(float now)
    {
        int loaded = 0;
        while (IsReloading && now >= nextLoadAt)
        {
            int toLoad = OneByOne ? 1 : MagazineSize - Magazine;
            if (!InfiniteReserve) toLoad = Math.Min(toLoad, Reserve);
            Magazine += toLoad;
            if (!InfiniteReserve) Reserve -= toLoad;
            loaded += toLoad;

            bool done = !OneByOne || Magazine >= MagazineSize || (!InfiniteReserve && Reserve <= 0);
            if (done) IsReloading = false;
            else nextLoadAt += ReloadTime;
        }
        return loaded;
    }

    /// <summary>Прерывает перезарядку (смена оружия — FR-WPN-07, выстрел во время зарядки помпы).
    /// Уже перенесённые патроны остаются в магазине.</summary>
    public void CancelReload()
    {
        IsReloading = false;
    }

    /// <summary>Подбор коробки (FR-WPN-04): добавляет в запас сколько влезает до <see cref="MaxReserve"/>.</summary>
    /// <param name="amount">Патронов в коробке.</param>
    /// <returns>Сколько патронов взято (остаток остаётся в коробке); 0 при <paramref name="amount"/> ≤ 0,
    /// полном запасе или бесконечном запасе.</returns>
    public int AddAmmo(int amount)
    {
        if (InfiniteReserve || amount <= 0) return 0;
        int take = Math.Min(amount, Math.Max(0, MaxReserve - Reserve));
        Reserve += take;
        return take;
    }
}
