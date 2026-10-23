using NUnit.Framework;
using UnityEngine;

namespace BeerAndDragon.Tests
{
    // Зона появления скелетов: случайные точки внутри круга, не вплотную друг к другу
    public class SkeletonSpawnZoneTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(12)]
        public void Points_CountAndInsideCircle(int count)
        {
            var points = SkeletonSpawnZone.PickPoints(count, 6f, 1.2f, new System.Random(1));
            Assert.AreEqual(count, points.Count);
            foreach (Vector2 p in points) Assert.LessOrEqual(p.magnitude, 6f + 1e-4f);
        }

        [Test]
        public void Points_KeepSpacing_WhenThereIsRoom()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var points = SkeletonSpawnZone.PickPoints(8, 6f, 1.2f, new System.Random(seed));
                for (int i = 0; i < points.Count; i++)
                    for (int j = i + 1; j < points.Count; j++)
                        Assert.GreaterOrEqual(Vector2.Distance(points[i], points[j]), 1.2f, $"seed {seed}");
            }
        }

        [Test]
        public void Points_TooCrowded_StillAllPlaced()
        {
            var points = SkeletonSpawnZone.PickPoints(30, 1f, 1.2f, new System.Random(3));
            Assert.AreEqual(30, points.Count);
            foreach (Vector2 p in points) Assert.LessOrEqual(p.magnitude, 1f + 1e-4f);
        }

        [Test]
        public void Points_SameSeed_SameLayout()
        {
            var a = SkeletonSpawnZone.PickPoints(5, 6f, 1.2f, new System.Random(42));
            var b = SkeletonSpawnZone.PickPoints(5, 6f, 1.2f, new System.Random(42));
            CollectionAssert.AreEqual(a, b);
        }
    }
}
