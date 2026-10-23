using NUnit.Framework;
using UnityEngine;

namespace BeerAndDragon.Tests
{
    // ИИ скелетов: прицел лучника, дистанция, досягаемость удара, реакция щитовика на меч игрока
    public class SkeletonTacticsTests
    {
        private const float G = 9.81f;

        // Где окажется снаряд через время t
        private static Vector3 Fly(Vector3 from, Vector3 velocity, float t) =>
            from + velocity * t + 0.5f * G * t * t * Vector3.down;

        [Test]
        public void Aim_NoGravity_StraightAtTarget()
        {
            Vector3 dir = SkeletonTactics.AimDirection(Vector3.zero, new Vector3(0, 0, 10), Vector3.zero, 20f, 0f);
            Assert.AreEqual(1f, Vector3.Dot(dir, Vector3.forward), 1e-4f);
        }

        [Test]
        public void Aim_Gravity_AimsAboveAndHits()
        {
            Vector3 from = new Vector3(0, 1.5f, 0), target = new Vector3(0, 1.1f, 12f);
            Vector3 dir = SkeletonTactics.AimDirection(from, target, Vector3.zero, 28f, G);
            Assert.Greater(dir.y, (target - from).normalized.y);       // навес: выше прямой линии
            float t = 12f / (dir.z * 28f);
            Assert.AreEqual(0f, Vector3.Distance(Fly(from, dir * 28f, t), target), 0.05f);
        }

        [Test]
        public void Aim_MovingTarget_Leads()
        {
            Vector3 from = Vector3.zero, target = new Vector3(0, 0, 14f), vel = new Vector3(4f, 0, 0);
            Vector3 dir = SkeletonTactics.AimDirection(from, target, vel, 28f, G);
            Assert.Greater(dir.x, 0f);                                // упреждение в сторону бега
            float t = 14f / (dir.z * 28f);
            Assert.AreEqual(0f, Vector3.Distance(Fly(from, dir * 28f, t), target + vel * t), 0.1f);
        }

        [Test]
        public void Aim_OutOfRange_FallsBackToStraight()
        {
            Vector3 dir = SkeletonTactics.AimDirection(Vector3.zero, new Vector3(0, 0, 500f), Vector3.zero, 10f, G);
            Assert.AreEqual(1f, Vector3.Dot(dir, Vector3.forward), 1e-4f);
        }

        [TestCase(20f, SkeletonTactics.RangeMove.Advance)]
        [TestCase(14.01f, SkeletonTactics.RangeMove.Advance)]
        [TestCase(14f, SkeletonTactics.RangeMove.Hold)]
        [TestCase(8f, SkeletonTactics.RangeMove.Hold)]
        [TestCase(5f, SkeletonTactics.RangeMove.Hold)]
        [TestCase(4.99f, SkeletonTactics.RangeMove.Retreat)]
        public void ArcherMove_ByDistance(float distance, SkeletonTactics.RangeMove expected)
        {
            Assert.AreEqual(expected, SkeletonTactics.ArcherMove(distance, 5f, 14f));
        }

        [TestCase(0f, 1.5f, true)]     // прямо перед носом
        [TestCase(0f, 2.2f, false)]    // дальше досягаемости
        [TestCase(1.2f, 1.2f, true)]   // 45° -- в конусе 70°
        [TestCase(1.5f, 0f, false)]    // сбоку, 90°
        [TestCase(0f, -1f, false)]     // за спиной
        public void InStrike_ReachAndCone(float x, float z, bool expected)
        {
            Assert.AreEqual(expected, SkeletonTactics.InStrike(Vector3.zero, Vector3.forward, new Vector3(x, 0.5f, z), 2.1f, 70f));
        }

        [Test]
        public void Shield_FarAway_Ignores()
        {
            Assert.AreEqual(SkeletonTactics.ShieldReaction.None,
                SkeletonTactics.ReactToThreat(true, true, 0.1f, 0.35f, 5f, 3.5f, 1f, 0f));
        }

        [Test]
        public void Shield_Charging_Blocks()
        {
            Assert.AreEqual(SkeletonTactics.ShieldReaction.Block,
                SkeletonTactics.ReactToThreat(true, false, 0f, 0.35f, 2f, 3.5f, 1f, 0f));
        }

        [TestCase(0.1f, 0.2f, SkeletonTactics.ShieldReaction.Parry)]   // удар только начался, повезло
        [TestCase(0.1f, 0.9f, SkeletonTactics.ShieldReaction.Block)]   // не повезло -- просто щит
        [TestCase(0.5f, 0.2f, SkeletonTactics.ShieldReaction.Block)]   // поздно парировать: удар уже прошёл
        public void Shield_Swing_ParryOrBlock(float progress, float roll, SkeletonTactics.ShieldReaction expected)
        {
            Assert.AreEqual(expected, SkeletonTactics.ReactToThreat(false, true, progress, 0.35f, 2f, 3.5f, 0.35f, roll));
        }

        [Test]
        public void Shield_NoThreat_None()
        {
            Assert.AreEqual(SkeletonTactics.ShieldReaction.None,
                SkeletonTactics.ReactToThreat(false, false, 0f, 0.35f, 1f, 3.5f, 1f, 0f));
        }
    }
}
