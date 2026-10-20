using System;
using System.Collections.Generic;

/// <summary>
/// Реализация <see cref="IObjectPool{T}"/> без выделений памяти после прогрева (модуль SP-1).
/// Свободные объекты — в стеке, выданные — в словаре «объект → номер выдачи»; самый старый ищется
/// линейным проходом по выданным, поэтому <see cref="Get"/> при переполнении стоит O(Capacity).
/// </summary>
/// <remarks>
/// Все коллекции создаются сразу ёмкостью <see cref="Capacity"/> и не растут, поэтому после
/// <see cref="Prewarm"/> вызовы <see cref="Get"/> / <see cref="Release"/> не выделяют память в управляемой куче
/// (проверено тестом <c>BoundedPoolTests.SteadyState_GetRelease_DoesNotAllocate</c>).
/// Поведение — по контракту <see cref="IObjectPool{T}"/>.
/// </remarks>
public sealed class BoundedPool<T> : IObjectPool<T> where T : class
{
    private readonly Func<T> create;
    private readonly Action<T> onGet;
    private readonly Action<T> onRelease;
    private readonly PoolOverflow overflow;

    private readonly Stack<T> free;
    private readonly Dictionary<T, long> active;   // объект -> номер выдачи (для поиска самого старого)
    private readonly HashSet<T> owned;             // все объекты, созданные этим пулом
    private long stamp;

    /// <inheritdoc/>
    public int Capacity { get; }
    /// <inheritdoc/>
    public int CountActive => active.Count;
    /// <inheritdoc/>
    public int CountInactive => free.Count;
    /// <inheritdoc/>
    public int CountAll => owned.Count;
    /// <inheritdoc/>
    public int CreatedTotal { get; private set; }
    /// <inheritdoc/>
    public int Recycled { get; private set; }      // сколько раз сработал ReuseOldest

    /// <summary>Создать пул. Объекты не создаются, пока не вызван <see cref="Get"/> или <see cref="Prewarm"/>.</summary>
    /// <param name="create">Фабрика объекта; не должна возвращать <c>null</c>.</param>
    /// <param name="capacity">Лимит объектов, &gt; 0.</param>
    /// <param name="overflow">Что делать при исчерпанном лимите.</param>
    /// <param name="onGet">Вызывается для каждого выдаваемого объекта (необязательно).</param>
    /// <param name="onRelease">Вызывается для каждого возвращаемого и забираемого объекта (необязательно).</param>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> равен <c>null</c> (проверяется первым).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> &lt;= 0.</exception>
    public BoundedPool(Func<T> create, int capacity, PoolOverflow overflow = PoolOverflow.ReuseOldest,
        Action<T> onGet = null, Action<T> onRelease = null)
    {
        if (create == null) throw new ArgumentNullException(nameof(create));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity должен быть > 0");

        this.create = create;
        this.onGet = onGet;
        this.onRelease = onRelease;
        this.overflow = overflow;
        Capacity = capacity;

        // Коллекции сразу нужного размера -- дальше они не растут и не аллоцируют.
        free = new Stack<T>(capacity);
        active = new Dictionary<T, long>(capacity);
        owned = new HashSet<T>(capacity);
    }

    /// <inheritdoc/>
    public void Prewarm(int count)
    {
        count = Math.Min(count, Capacity);
        while (owned.Count < count)
        {
            T item = CreateItem();
            onRelease?.Invoke(item);
            free.Push(item);
        }
    }

    /// <inheritdoc/>
    public T Get()
    {
        T item;
        if (free.Count > 0)
        {
            item = free.Pop();
        }
        else if (owned.Count < Capacity)
        {
            item = CreateItem();
        }
        else if (overflow == PoolOverflow.ReuseOldest && active.Count > 0)
        {
            item = Oldest();
            active.Remove(item);
            onRelease?.Invoke(item);
            Recycled++;
        }
        else
        {
            return null;
        }

        active[item] = ++stamp;
        onGet?.Invoke(item);
        return item;
    }

    /// <inheritdoc/>
    public void Release(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!owned.Contains(item)) throw new InvalidOperationException("Объект создан не этим пулом");
        if (!active.Remove(item)) throw new InvalidOperationException("Объект уже возвращён в пул");

        onRelease?.Invoke(item);
        free.Push(item);
    }

    /// <inheritdoc/>
    public bool IsActive(T item) => item != null && active.ContainsKey(item);

    private T CreateItem()
    {
        T item = create();
        if (item == null) throw new InvalidOperationException("create() вернул null");
        owned.Add(item);
        CreatedTotal++;
        return item;
    }

    // Линейный поиск: capacity пулов в игре -- десятки/сотни, а так нет ни аллокаций, ни лишней структуры.
    private T Oldest()
    {
        T oldest = null;
        long best = long.MaxValue;
        foreach (KeyValuePair<T, long> pair in active)   // foreach по Dictionary -- struct-энумератор, без аллокаций
        {
            if (pair.Value < best) { best = pair.Value; oldest = pair.Key; }
        }
        return oldest;
    }
}
