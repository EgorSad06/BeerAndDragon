using System;
using System.Collections.Generic;

/// <summary>
/// Вторая реализация <see cref="IObjectPool{T}"/>: порядок выдачи хранится в двусвязном списке,
/// поэтому переиспользование самого старого объекта — O(1) (первый узел списка), а не линейный поиск.
/// </summary>
/// <remarks>
/// Цена: каждый <see cref="Get"/> добавляет в список новый узел <c>LinkedListNode&lt;T&gt;</c> — выделение памяти
/// в управляемой куче на каждую выдачу (в отличие от <see cref="BoundedPool{T}"/>). Результаты и ошибки — те же,
/// что требует контракт <see cref="IObjectPool{T}"/>; проверяется общим набором тестов <c>ObjectPoolContractTests</c>.
/// </remarks>
public sealed class LinkedPool<T> : IObjectPool<T> where T : class
{
    private readonly Func<T> create;
    private readonly Action<T> onGet;
    private readonly Action<T> onRelease;
    private readonly PoolOverflow overflow;

    private readonly Stack<T> free;
    private readonly LinkedList<T> order = new LinkedList<T>();                // выданные: первый -- самый старый
    private readonly Dictionary<T, LinkedListNode<T>> nodes;                  // объект -> его узел в order
    private readonly HashSet<T> owned;

    /// <inheritdoc/>
    public int Capacity { get; }
    /// <inheritdoc/>
    public int CountActive => order.Count;
    /// <inheritdoc/>
    public int CountInactive => free.Count;
    /// <inheritdoc/>
    public int CountAll => owned.Count;
    /// <inheritdoc/>
    public int CreatedTotal { get; private set; }
    /// <inheritdoc/>
    public int Recycled { get; private set; }

    /// <summary>Создать пул; параметры и ошибки — как у <see cref="BoundedPool{T}"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> равен <c>null</c> (проверяется первым).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> &lt;= 0.</exception>
    public LinkedPool(Func<T> create, int capacity, PoolOverflow overflow = PoolOverflow.ReuseOldest,
        Action<T> onGet = null, Action<T> onRelease = null)
    {
        if (create == null) throw new ArgumentNullException(nameof(create));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity должен быть > 0");

        this.create = create;
        this.onGet = onGet;
        this.onRelease = onRelease;
        this.overflow = overflow;
        Capacity = capacity;
        free = new Stack<T>(capacity);
        nodes = new Dictionary<T, LinkedListNode<T>>(capacity);
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
        else if (overflow == PoolOverflow.ReuseOldest && order.Count > 0)
        {
            LinkedListNode<T> oldest = order.First;
            item = oldest.Value;
            order.RemoveFirst();
            nodes.Remove(item);
            onRelease?.Invoke(item);
            Recycled++;
        }
        else
        {
            return null;
        }

        nodes[item] = order.AddLast(item);   // новый узел -- выделение памяти на каждую выдачу
        onGet?.Invoke(item);
        return item;
    }

    /// <inheritdoc/>
    public void Release(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!owned.Contains(item)) throw new InvalidOperationException("Объект создан не этим пулом");
        if (!nodes.TryGetValue(item, out LinkedListNode<T> node)) throw new InvalidOperationException("Объект уже возвращён в пул");

        order.Remove(node);
        nodes.Remove(item);
        onRelease?.Invoke(item);
        free.Push(item);
    }

    /// <inheritdoc/>
    public bool IsActive(T item) => item != null && nodes.ContainsKey(item);

    private T CreateItem()
    {
        T item = create();
        if (item == null) throw new InvalidOperationException("create() вернул null");
        owned.Add(item);
        CreatedTotal++;
        return item;
    }
}
