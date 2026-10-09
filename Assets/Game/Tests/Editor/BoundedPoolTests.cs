using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using UIs = UnityEngine.TestTools.Constraints.Is;

// Проверки контракта модуля SP-1 (docs/system-programming.md). Id требований -- в docs/requirements.md.
public class BoundedPoolTests
{
    private class Item { public bool alive; }

    private static BoundedPool<Item> MakePool(int capacity, BoundedPool<Item>.OverflowPolicy policy = BoundedPool<Item>.OverflowPolicy.ReuseOldest)
    {
        return new BoundedPool<Item>(() => new Item(), capacity, policy, i => i.alive = true, i => i.alive = false);
    }

    [Test]
    public void Get_CreatesUpToCapacity_ThenReusesReleased()
    {
        var pool = MakePool(3);
        Item a = pool.Get();
        pool.Get();
        pool.Release(a);
        Item c = pool.Get();

        Assert.AreSame(a, c, "освобождённый объект должен выдаваться повторно");
        Assert.AreEqual(2, pool.CreatedTotal);
        Assert.AreEqual(2, pool.CountActive);
        Assert.AreEqual(0, pool.CountInactive);
    }

    [Test]
    public void Overflow_ReuseOldest_NeverExceedsCapacity()
    {
        var pool = MakePool(2);
        Item first = pool.Get();
        Item second = pool.Get();
        Item third = pool.Get();

        Assert.AreSame(first, third, "при переполнении забирается самый старый");
        Assert.AreEqual(2, pool.CountAll);
        Assert.AreEqual(1, pool.Recycled);
        Assert.IsTrue(pool.IsActive(second));
        Assert.IsTrue(third.alive, "переиспользованный объект снова активирован");
    }

    [Test]
    public void Overflow_ReuseOldest_TakesOldestByIssueOrder()
    {
        var pool = MakePool(3);
        Item a = pool.Get();
        Item b = pool.Get();
        pool.Get();
        pool.Release(a);
        Item a2 = pool.Get();       // a выдан заново -- теперь он самый новый

        Item recycled = pool.Get(); // лимит исчерпан -> самый старый = b
        Assert.AreSame(a, a2);
        Assert.AreSame(b, recycled);
    }

    [Test]
    public void Overflow_Fail_ReturnsNull()
    {
        var pool = MakePool(1, BoundedPool<Item>.OverflowPolicy.Fail);
        pool.Get();
        Assert.IsNull(pool.Get());
        Assert.AreEqual(1, pool.CountAll);
    }

    [Test]
    public void Release_Twice_Throws()
    {
        var pool = MakePool(2);
        Item a = pool.Get();
        pool.Release(a);
        Assert.Throws<InvalidOperationException>(() => pool.Release(a));
    }

    [Test]
    public void Release_ForeignObject_Throws()
    {
        var pool = MakePool(2);
        Assert.Throws<InvalidOperationException>(() => pool.Release(new Item()));
    }

    [Test]
    public void Callbacks_AreCalledOnGetAndRelease()
    {
        var pool = MakePool(2);
        Item a = pool.Get();
        Assert.IsTrue(a.alive);
        pool.Release(a);
        Assert.IsFalse(a.alive);
    }

    [Test]
    public void Prewarm_CreatesInactiveObjects_LimitedByCapacity()
    {
        var pool = MakePool(4);
        pool.Prewarm(10);
        Assert.AreEqual(4, pool.CountInactive);
        Assert.AreEqual(0, pool.CountActive);
        pool.Get();
        Assert.AreEqual(4, pool.CreatedTotal, "после прогрева новые объекты не создаются");
    }

    [Test]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedPool<Item>(() => new Item(), 0));
        Assert.Throws<ArgumentNullException>(() => new BoundedPool<Item>(null, 4));
        var broken = new BoundedPool<Item>(() => null, 2);
        Assert.Throws<InvalidOperationException>(() => broken.Get());
    }

    [Test]
    public void SteadyState_GetRelease_DoesNotAllocate()
    {
        var pool = MakePool(16);
        pool.Prewarm(16);
        var held = new Item[16];
        // первый цикл -- прогрев JIT
        for (int i = 0; i < 16; i++) held[i] = pool.Get();
        for (int i = 0; i < 16; i++) pool.Release(held[i]);

        // Констрейнт Unity Test Framework: падает, если внутри делегата была аллокация в управляемой куче.
        Assert.That(() =>
        {
            for (int round = 0; round < 100; round++)
            {
                for (int i = 0; i < 16; i++) held[i] = pool.Get();
                pool.Get();                                 // переполнение -> ReuseOldest заберёт held[0] и выдаст снова
                for (int i = 0; i < 16; i++) pool.Release(held[i]);
            }
        }, UIs.Not.AllocatingGCMemory());
        Assert.AreEqual(0, pool.CountActive);
        Assert.AreEqual(16, pool.CreatedTotal);
    }
}
