using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Пространство имён обязательно: параметризованные фикстуры NUnit в глобальном пространстве имён
// не загружаются (ArgumentNullException: pathName), а Unity при этом сообщает 0 упавших тестов.
namespace BeerAndDragon.Tests
{
    // Потребитель пула BulletHoles (ПЗ-2): один и тот же код потребителя с обеими реализациями пула.
    // Реализация выбирается только здесь -- в точке сборки теста, как в игре в Weapon.SetupBulletHoles.
    [TestFixture(PoolKind.Bounded)]
    [TestFixture(PoolKind.Linked)]
    public class BulletHolesTests
    {
        private readonly PoolKind kind;
        private readonly List<GameObject> created = new List<GameObject>();
        public BulletHolesTests(PoolKind kind) => this.kind = kind;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in created) Object.DestroyImmediate(go);
            created.Clear();
        }

        private BulletHoles Make(int capacity, float lifetime)
        {
            IObjectPool<GameObject> pool = ObjectPoolFactory.Create(kind,
                () => { var go = new GameObject("hole"); go.SetActive(false); created.Add(go); return go; },
                capacity, PoolOverflow.ReuseOldest, go => go.SetActive(true), go => go.SetActive(false));
            return new BulletHoles(pool, 0.01f, lifetime, false);
        }

        [Test]
        public void Place_PutsDecalOnSurfaceWithOffset()
        {
            BulletHoles holes = Make(3, 10f);
            GameObject d = holes.Place(new Vector3(1f, 2f, 3f), Vector3.up, 0f, 0f);

            Assert.IsTrue(d.activeSelf);
            TestUtil.AreClose(new Vector3(1f, 2.01f, 3f), d.transform.position);
            TestUtil.AreClose(Vector3.up, d.transform.forward);
            Assert.AreEqual(1, holes.Active);
        }

        [Test]
        public void Overflow_RecyclesOldestDecal()
        {
            BulletHoles holes = Make(2, 10f);
            GameObject a = holes.Place(Vector3.zero, Vector3.up, 0f, 0f);
            holes.Place(Vector3.one, Vector3.up, 0f, 0f);
            GameObject c = holes.Place(Vector3.right * 5f, Vector3.up, 0f, 0f);

            Assert.AreSame(a, c, "четвёртая дырка при лимите 2 -- это переставленная первая");
            TestUtil.AreClose(Vector3.right * 5f + Vector3.up * 0.01f, c.transform.position);
            Assert.AreEqual(2, holes.Active);
            Assert.AreEqual(2, created.Count, "новых объектов сверх лимита не создаётся");
        }

        [Test]
        public void Tick_ReleasesExpiredInOrder_AfterRecycle()
        {
            BulletHoles holes = Make(2, 5f);
            holes.Place(Vector3.zero, Vector3.up, 0f, 0f);                 // истекает в 5
            GameObject b = holes.Place(Vector3.zero, Vector3.up, 0f, 1f);  // в 6
            GameObject c = holes.Place(Vector3.zero, Vector3.up, 0f, 2f);  // переставлена первая, истекает в 7

            holes.Tick(5.5f);                     // запись первой в очереди уже снята при переиспользовании
            Assert.AreEqual(2, holes.Active);
            holes.Tick(6f);
            Assert.IsFalse(b.activeSelf);
            Assert.IsTrue(c.activeSelf);
            holes.Tick(7f);
            Assert.IsFalse(c.activeSelf);
            Assert.AreEqual(0, holes.Active);
        }

        [Test]
        public void ZeroLifetime_DecalsStayUntilRecycled()
        {
            BulletHoles holes = Make(2, 0f);
            holes.Place(Vector3.zero, Vector3.up, 0f, 0f);
            holes.Tick(1e6f);
            Assert.AreEqual(1, holes.Active);
        }
    }
}
