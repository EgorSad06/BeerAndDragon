using UnityEngine;

/// <summary>
/// Дырки от пуль: ставит декаль в точку попадания и убирает её по истечении времени жизни.
/// Потребитель пула: получает <see cref="IObjectPool{T}"/> через конструктор и не знает, какая реализация
/// за ним стоит (её выбирает точка сборки — Weapon.SetupBulletHoles по параметру конфигурации <c>decalPool</c>).
/// </summary>
/// <remarks>
/// Пул должен быть создан с <see cref="PoolOverflow.ReuseOldest"/> и использоваться только этим объектом:
/// тогда порядок выдачи пула совпадает с порядком постановки декалей, и при переполнении пул забирает
/// ту же декаль, что стоит первой в очереди на удаление. Очередь — кольцевой буфер на <c>Capacity</c> элементов,
/// без выделений памяти.
/// </remarks>
public sealed class BulletHoles
{
    private readonly IObjectPool<GameObject> pool;
    private readonly float offset;
    private readonly float lifetime;
    private readonly bool flipNormal;

    private readonly GameObject[] ring;   // поставленные декали в порядке постановки
    private readonly float[] expireAt;
    private int head, count;

    /// <summary>Сколько декалей сейчас стоит в мире.</summary>
    public int Active => pool.CountActive;

    /// <summary>Пул, через который работает потребитель (для диагностики).</summary>
    public IObjectPool<GameObject> Pool => pool;

    /// <param name="pool">Пул декалей с политикой <see cref="PoolOverflow.ReuseOldest"/>.</param>
    /// <param name="offset">Сдвиг декали от поверхности вдоль нормали, м (против мерцания).</param>
    /// <param name="lifetime">Время жизни декали, с; 0 — пока пул не заберёт её под новую.</param>
    /// <param name="flipNormal">Развернуть декаль (у некоторых плоскостей лицевая сторона смотрит назад).</param>
    public BulletHoles(IObjectPool<GameObject> pool, float offset, float lifetime, bool flipNormal)
    {
        this.pool = pool;
        this.offset = offset;
        this.lifetime = lifetime;
        this.flipNormal = flipNormal;
        ring = new GameObject[pool.Capacity];
        expireAt = new float[pool.Capacity];
    }

    /// <summary>Поставить декаль в точку <paramref name="point"/> поверхности с нормалью <paramref name="normal"/>.</summary>
    /// <param name="spinDegrees">Поворот декали вокруг нормали, градусы (разнообразие дырок).</param>
    /// <param name="now">Текущее время, с (<c>Time.time</c>).</param>
    /// <returns>Поставленная декаль; <c>null</c>, если пул ничего не выдал.</returns>
    public GameObject Place(Vector3 point, Vector3 normal, float spinDegrees, float now)
    {
        GameObject decal = pool.Get();
        if (decal == null) return null;

        // Пул переполнен и забрал самую старую декаль -- она же первая в очереди
        if (count == ring.Length)
        {
            head = (head + 1) % ring.Length;
            count--;
        }
        int tail = (head + count) % ring.Length;
        ring[tail] = decal;
        expireAt[tail] = lifetime > 0f ? now + lifetime : float.PositiveInfinity;
        count++;

        Vector3 facing = flipNormal ? -normal : normal;
        decal.transform.SetPositionAndRotation(point + normal * offset,
            Quaternion.LookRotation(facing) * Quaternion.Euler(0f, 0f, spinDegrees));
        return decal;
    }

    /// <summary>Вернуть в пул декали, у которых истекло время жизни. Вызывать раз в кадр.</summary>
    public void Tick(float now)
    {
        while (count > 0 && expireAt[head] <= now)
        {
            pool.Release(ring[head]);
            ring[head] = null;
            head = (head + 1) % ring.Length;
            count--;
        }
    }
}
