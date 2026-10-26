using NUnit.Framework;

// Уровни эффектов портрета: каждые 10% здоровья -- новый уровень, эффект включается ровно на границе (90% -> 1).
public class PlayerPortraitTests
{
    [TestCase(100f, 0)]
    [TestCase(90.5f, 0)]
    [TestCase(90f, 1)]
    [TestCase(80f, 2)]
    [TestCase(70f, 3)]
    [TestCase(60f, 4)]
    [TestCase(55f, 4)]
    [TestCase(50f, 5)]
    [TestCase(40f, 6)]
    [TestCase(30f, 7)]
    [TestCase(20f, 8)]
    [TestCase(10f, 9)]
    [TestCase(0.5f, 9)]
    [TestCase(0f, 10)]
    public void LevelFor_EveryTenPercent(float health, int expected)
    {
        Assert.AreEqual(expected, PlayerPortrait.LevelFor(health, 100f));
    }

    [Test]
    public void LevelFor_UsesPercentOfMaxHealth()
    {
        Assert.AreEqual(1, PlayerPortrait.LevelFor(180f, 200f));
        Assert.AreEqual(8, PlayerPortrait.LevelFor(40f, 200f));
    }
}
