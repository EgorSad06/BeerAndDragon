using NUnit.Framework;
using UnityEngine;

public class PlayerHealthTests
{
    private GameObject go;
    private PlayerHealth health;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("HealthTest");
        health = go.AddComponent<PlayerHealth>();
        health.maxHealth = 100f;
        TestUtil.CallAwake(health);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
        PlayerInputLock.Locked = false; // смерть блокирует ввод -- возвращаем как было
    }

    [Test]
    public void StartsWithMaxHealth()
    {
        Assert.AreEqual(100f, health.Health);
        Assert.IsFalse(health.IsDead);
        Assert.IsFalse(health.CanHeal);
    }

    [Test]
    public void TakeDamage_ReducesHealth()
    {
        health.TakeDamage(30f);
        Assert.AreEqual(70f, health.Health, 0.001f);
        Assert.IsTrue(health.CanHeal);
    }

    [Test]
    public void TakeDamage_NegativeOrZero_Ignored()
    {
        health.TakeDamage(-50f);
        health.TakeDamage(0f);
        Assert.AreEqual(100f, health.Health);
    }

    [Test]
    public void Heal_IsClampedToMax()
    {
        health.TakeDamage(10f);
        health.Heal(500f);
        Assert.AreEqual(100f, health.Health);
    }

    [Test]
    public void LethalDamage_KillsAndClampsToZero()
    {
        int damagedEvents = 0, diedEvents = 0;
        health.Damaged += _ => damagedEvents++;
        health.Died += () => diedEvents++;

        health.TakeDamage(250f);

        Assert.AreEqual(0f, health.Health);
        Assert.IsTrue(health.IsDead);
        Assert.AreEqual(1, damagedEvents);
        Assert.AreEqual(1, diedEvents);
    }

    [Test]
    public void DeadPlayer_IgnoresDamageAndHealing()
    {
        health.TakeDamage(200f);
        health.TakeDamage(10f);
        health.Heal(50f);

        Assert.AreEqual(0f, health.Health);
        Assert.IsFalse(health.CanHeal);
    }
}
