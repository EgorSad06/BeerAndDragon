using System;
using NUnit.Framework;

// Пространство имён обязательно: параметризованные фикстуры NUnit в глобальном пространстве имён
// не загружаются (ArgumentNullException: pathName), а Unity при этом сообщает 0 упавших тестов.
namespace BeerAndDragon.Tests
{
    // Контракт IObjectPool<T> (ПЗ-2, docs/modules/object-pool.md): один и тот же набор проверок
    // выполняется для каждой реализации. Реализации взаимозаменяемы, только если все тесты зелёные для обеих.
    [TestFixture(PoolKind.Bounded)]
    [TestFixture(PoolKind.Linked)]
    public class ObjectPoolContractTests
    {
        private readonly PoolKind kind;
        public ObjectPoolContractTests(PoolKind kind) => this.kind = kind;

        private class Item { public bool alive; }

        private IObjectPool<Item> MakePool(int capacity, PoolOverflow policy = PoolOverflow.ReuseOldest) =>
            ObjectPoolFactory.Create(kind, () => new Item(), capacity, policy, i => i.alive = true, i => i.alive = false);

        // ---- штатные случаи ----

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
        public void Get_ReturnsLastReleasedFirst()
        {
            var pool = MakePool(3);
            Item a = pool.Get(), b = pool.Get();
            pool.Release(a);
            pool.Release(b);
            Assert.AreSame(b, pool.Get(), "свободные выдаются в обратном порядке возврата");
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

        // ---- граничные случаи ----

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
        public void Capacity1_EveryGetRecyclesTheSameObject()
        {
            var pool = MakePool(1);
            Item a = pool.Get();
            Assert.AreSame(a, pool.Get());
            Assert.AreSame(a, pool.Get());
            Assert.AreEqual(1, pool.CreatedTotal);
            Assert.AreEqual(2, pool.Recycled);
        }

        [Test]
        public void Overflow_Fail_ReturnsNull_StateUnchanged()
        {
            var pool = MakePool(1, PoolOverflow.Fail);
            Item a = pool.Get();
            Assert.IsNull(pool.Get());
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(0, pool.Recycled);
            Assert.IsTrue(pool.IsActive(a));
        }

        [Test]
        public void Prewarm_CreatesInactiveObjects_LimitedByCapacity()
        {
            var pool = MakePool(4);
            pool.Prewarm(0);
            pool.Prewarm(-5);
            Assert.AreEqual(0, pool.CountAll, "0 и отрицательное ничего не создают");
            pool.Prewarm(10);
            Assert.AreEqual(4, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
            pool.Get();
            Assert.AreEqual(4, pool.CreatedTotal, "после прогрева новые объекты не создаются");
        }

        [Test]
        public void IsActive_NullAndForeign_False()
        {
            var pool = MakePool(2);
            Assert.IsFalse(pool.IsActive(null));
            Assert.IsFalse(pool.IsActive(new Item()));
        }

        // ---- ошибочные случаи и порядок проверки ----

        [Test]
        public void Release_Twice_Throws_StateUnchanged()
        {
            var pool = MakePool(2);
            Item a = pool.Get();
            pool.Release(a);
            Assert.Throws<InvalidOperationException>(() => pool.Release(a));
            Assert.AreEqual(0, pool.CountActive);
            Assert.AreEqual(1, pool.CountInactive);
        }

        [Test]
        public void Release_ForeignObject_Throws()
        {
            var pool = MakePool(2);
            Assert.Throws<InvalidOperationException>(() => pool.Release(new Item()));
        }

        [Test]
        public void Release_Null_ThrowsArgumentNull_BeforeOtherChecks()
        {
            var pool = MakePool(2);
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
        }

        [Test]
        public void InvalidArguments_Throw_InContractOrder()
        {
            // create == null проверяется раньше capacity
            Assert.Throws<ArgumentNullException>(() => ObjectPoolFactory.Create<Item>(kind, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObjectPoolFactory.Create(kind, () => new Item(), 0));
            var broken = ObjectPoolFactory.Create<Item>(kind, () => null, 2);
            Assert.Throws<InvalidOperationException>(() => broken.Get());
        }
    }
}
