using System;

/// <summary>Какую реализацию <see cref="IObjectPool{T}"/> создаёт <see cref="ObjectPoolFactory"/>.</summary>
public enum PoolKind
{
    /// <summary><see cref="BoundedPool{T}"/>: без выделений памяти после прогрева.</summary>
    Bounded,
    /// <summary><see cref="LinkedPool{T}"/>: переиспользование за O(1), узел списка на каждую выдачу.</summary>
    Linked,
}

/// <summary>
/// Точка сборки пулов: единственное место, которое знает конкретные реализации <see cref="IObjectPool{T}"/>.
/// Потребители (например, BulletHoles) получают готовый пул через конструктор и зависят
/// только от интерфейса. В игре реализацию выбирает параметр конфигурации <c>decalPool</c>.
/// </summary>
public static class ObjectPoolFactory
{
    /// <summary>Создать пул выбранной реализации; параметры и ошибки — как у конструкторов реализаций.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Неизвестное значение <paramref name="kind"/>.</exception>
    public static IObjectPool<T> Create<T>(PoolKind kind, Func<T> create, int capacity,
        PoolOverflow overflow = PoolOverflow.ReuseOldest, Action<T> onGet = null, Action<T> onRelease = null) where T : class
    {
        switch (kind)
        {
            case PoolKind.Bounded: return new BoundedPool<T>(create, capacity, overflow, onGet, onRelease);
            case PoolKind.Linked: return new LinkedPool<T>(create, capacity, overflow, onGet, onRelease);
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "неизвестная реализация пула");
        }
    }

    /// <summary>Разобрать имя реализации из конфигурации: <c>"bounded"</c> или <c>"linked"</c> (без учёта регистра).</summary>
    /// <returns><c>true</c>, если имя известно.</returns>
    public static bool TryParse(string name, out PoolKind kind)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "bounded": kind = PoolKind.Bounded; return true;
            case "linked": kind = PoolKind.Linked; return true;
            default: kind = PoolKind.Bounded; return false;
        }
    }
}
