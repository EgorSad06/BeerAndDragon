using NUnit.Framework;
using UnityEngine;

public class WeaponTests
{
    [TestCase(0f, 1f)]
    [TestCase(5f, 1f)]      // до начала спада -- полный урон
    [TestCase(10f, 1f)]
    [TestCase(20f, 0.6f)]   // середина: Lerp(1, 0.2, 0.5)
    [TestCase(30f, 0.2f)]
    [TestCase(100f, 0.2f)]  // дальше конца -- минимум
    public void FalloffMultiplier_ByDistance(float distance, float expected)
    {
        Assert.AreEqual(expected, Weapon.FalloffMultiplier(distance, 10f, 30f, 0.2f), 0.0001f);
    }

    [Test]
    public void FalloffMultiplier_InvalidRange_NoFalloff()
    {
        Assert.AreEqual(1f, Weapon.FalloffMultiplier(50f, 30f, 10f, 0.2f));
    }

    [Test]
    public void SpreadDirection_ZeroCone_ReturnsForward()
    {
        Vector3 fwd = new Vector3(0.3f, 0.2f, 1f).normalized;
        TestUtil.AreClose(fwd, Weapon.SpreadDirection(fwd, 0f));
    }

    [Test]
    public void SpreadDirection_StaysInsideCone()
    {
        Random.InitState(12345);
        Vector3[] forwards = { Vector3.forward, Vector3.up, Vector3.down, new Vector3(1f, 1f, 0f).normalized };
        foreach (Vector3 fwd in forwards)
        {
            for (int i = 0; i < 500; i++)
            {
                Vector3 dir = Weapon.SpreadDirection(fwd, 6f);
                Assert.AreEqual(1f, dir.magnitude, 0.001f, "направление должно быть единичным");
                Assert.LessOrEqual(Vector3.Angle(fwd, dir), 6.01f, $"дробина вылетела из конуса для {fwd}");
            }
        }
    }

    [Test]
    public void SpreadDirection_ActuallySpreads()
    {
        Random.InitState(42);
        float maxAngle = 0f;
        for (int i = 0; i < 200; i++)
            maxAngle = Mathf.Max(maxAngle, Vector3.Angle(Vector3.forward, Weapon.SpreadDirection(Vector3.forward, 6f)));
        Assert.Greater(maxAngle, 3f, "разброс подозрительно мал -- конус не работает");
    }

    [Test]
    public void AddAmmo_RespectsMaxReserve()
    {
        GameObject go = new GameObject("WeaponTest");
        Weapon w = go.AddComponent<Weapon>();
        w.reserveAmmo = 8;
        w.maxReserveAmmo = 10;
        w.infiniteReserve = false;

        int taken = w.AddAmmo(5);

        Assert.AreEqual(2, taken);
        Assert.AreEqual(10, w.ReserveAmmo);
        Assert.AreEqual(0, w.AddAmmo(5), "полный запас -- коробка остаётся лежать");
        Object.DestroyImmediate(go);
    }

    [Test]
    public void AddAmmo_InfiniteReserve_TakesNothing()
    {
        GameObject go = new GameObject("WeaponTest");
        Weapon w = go.AddComponent<Weapon>();
        w.infiniteReserve = true;
        Assert.AreEqual(0, w.AddAmmo(10));
        Object.DestroyImmediate(go);
    }
}
