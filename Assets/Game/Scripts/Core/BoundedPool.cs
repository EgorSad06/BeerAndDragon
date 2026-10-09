using System;
using System.Collections.Generic;

// Пул объектов с жёстким лимитом. Первый модуль направления «управление памятью в реальном времени»
// (docs/system-programming.md, модуль SP-1).
//
// Контракт:
//  - Get() отдаёт свободный объект; если свободных нет и создано меньше capacity -- создаёт новый;
//    если лимит исчерпан -- действует по OverflowPolicy (ReuseOldest: забирает самый старый выданный,
//    Fail: возвращает null). Больше capacity объектов не существует никогда.
//  - Release(x) возвращает объект в пул. Повторный возврат или чужой объект -- InvalidOperationException
//    (такая ошибка в игре означает «один объект используется дважды», её надо ловить сразу).
//  - После Prewarm/прогрева Get и Release не выделяют память в куче (нет LINQ, замыканий, роста коллекций).
//  - onGet вызывается при каждой выдаче, onRelease -- при каждом возврате (в т.ч. принудительном при ReuseOldest).
//  - Не потокобезопасен: только главный поток Unity.
public sealed class BoundedPool<T> where T : class
{
    public enum OverflowPolicy { ReuseOldest, Fail }

    private readonly Func<T> create;
    private readonly Action<T> onGet;
    private readonly Action<T> onRelease;
    private readonly OverflowPolicy overflow;

    private readonly Stack<T> free;
    private readonly Dictionary<T, long> active;   // объект -> номер выдачи (для поиска самого старого)
    private readonly HashSet<T> owned;             // все объекты, созданные этим пулом
    private long stamp;

    public int Capacity { get; }
    public int CountActive => active.Count;
    public int CountInactive => free.Count;
    public int CountAll => owned.Count;
    public int CreatedTotal { get; private set; }
    public int Recycled { get; private set; }      // сколько раз сработал ReuseOldest

    public BoundedPool(Func<T> create, int capacity, OverflowPolicy overflow = OverflowPolicy.ReuseOldest,
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

    // Создаёт объекты заранее (например, на загрузке уровня), чтобы в бою не было аллокаций.
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
        else if (overflow == OverflowPolicy.ReuseOldest && active.Count > 0)
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

    public void Release(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!owned.Contains(item)) throw new InvalidOperationException("Объект создан не этим пулом");
        if (!active.Remove(item)) throw new InvalidOperationException("Объект уже возвращён в пул");

        onRelease?.Invoke(item);
        free.Push(item);
    }

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
