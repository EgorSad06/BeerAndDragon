using NUnit.Framework;

// Настройки игрока: значения из PlayerPrefs (могут быть битые или старые) зажимаются в допустимые границы.
public class GameSettingsTests
{
    [TestCase(-1f, 0f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(7f, 1f)]
    [TestCase(float.NaN, GameSettings.DefaultVolume)]
    public void ClampVolume_Range0To1(float input, float expected)
    {
        Assert.AreEqual(expected, GameSettings.ClampVolume(input), 1e-5f);
    }

    [TestCase(0f, GameSettings.MinSensitivity)]
    [TestCase(1.5f, 1.5f)]
    [TestCase(99f, GameSettings.MaxSensitivity)]
    [TestCase(float.NaN, GameSettings.DefaultSensitivity)]
    public void ClampSensitivity_KeepsMouseUsable(float input, float expected)
    {
        Assert.AreEqual(expected, GameSettings.ClampSensitivity(input), 1e-5f);
    }

    [TestCase(10f, GameSettings.MinFov)]
    [TestCase(75f, 75f)]
    [TestCase(170f, GameSettings.MaxFov)]
    [TestCase(float.NaN, GameSettings.DefaultFov)]
    public void ClampFov_Range(float input, float expected)
    {
        Assert.AreEqual(expected, GameSettings.ClampFov(input), 1e-5f);
    }
}
