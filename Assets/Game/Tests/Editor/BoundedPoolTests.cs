using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using UIs = UnityEngine.TestTools.Constraints.Is;

// Свойства реализаций IObjectPool<T>, которые НЕ входят в общий контракт (он -- в ObjectPoolContractTests):
// расход памяти. BoundedPool после прогрева не выделяет память; LinkedPool выделяет узел на каждую выдачу.
public class BoundedPoolTests
{
    private class Item { }

    // Сценарий: 100 раундов -- выдать все 16, одна выдача сверх лимита (ReuseOldest), вернуть все
    private static void Rounds(IObjectPool<Item> pool, Item[] held)
    {
        for (int round = 0; round < 100; round++)
        {
            for (int i = 0; i < held.Length; i++) held[i] = pool.Get();
            pool.Get();                                 // переполнение -> ReuseOldest заберёт held[0] и выдаст снова
            for (int i = 0; i < held.Length; i++) pool.Release(held[i]);
        }
    }

    private static IObjectPool<Item> Warmed(PoolKind kind, Item[] held)
    {
        IObjectPool<Item> pool = ObjectPoolFactory.Create(kind, () => new Item(), held.Length);
        pool.Prewarm(held.Length);
        Rounds(pool, held);   // прогрев JIT и внутренних коллекций
        return pool;
    }

    [Test]
    public void SteadyState_GetRelease_DoesNotAllocate()
    {
        var held = new Item[16];
        IObjectPool<Item> pool = Warmed(PoolKind.Bounded, held);

        // Констрейнт Unity Test Framework: падает, если внутри делегата была аллокация в управляемой куче.
        Assert.That(() => Rounds(pool, held), UIs.Not.AllocatingGCMemory());
        Assert.AreEqual(0, pool.CountActive);
        Assert.AreEqual(16, pool.CreatedTotal);
    }

    [Test]
    public void LinkedPool_AllocatesOnGet_DocumentedCost()
    {
        var held = new Item[16];
        IObjectPool<Item> pool = Warmed(PoolKind.Linked, held);

        // Цена O(1)-переиспользования: узел LinkedListNode на каждую выдачу (см. ADR-002)
        Assert.That(() => Rounds(pool, held), UIs.AllocatingGCMemory());
        Assert.AreEqual(0, pool.CountActive);
        Assert.AreEqual(16, pool.CreatedTotal);
    }
}
