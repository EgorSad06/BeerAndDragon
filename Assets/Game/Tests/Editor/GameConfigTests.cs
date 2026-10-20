using NUnit.Framework;

// Конфигурация компонента (ПЗ-1): допустимые значения применяются, неверные -- понятная ошибка.
public class GameConfigTests
{
    private static GameConfig.Values Parse(string json)
    {
        Assert.IsTrue(GameConfig.TryParse(json, out GameConfig.Values v, out string error), error);
        return v;
    }

    private static string ParseError(string json)
    {
        Assert.IsFalse(GameConfig.TryParse(json, out _, out string error), "ожидалась ошибка конфигурации");
        Assert.IsNotEmpty(error);
        return error;
    }

    [Test]
    public void ValidConfig_IsApplied()
    {
        GameConfig.Values v = Parse("{ \"targetFps\": 30, \"logLevel\": \"verbose\" }");
        Assert.AreEqual(30, v.targetFps);
        Assert.AreEqual("verbose", v.logLevel);
    }

    [Test]
    public void EmptyObject_GivesDefaults()
    {
        GameConfig.Values v = Parse("{}");
        Assert.AreEqual(60, v.targetFps);
        Assert.AreEqual("info", v.logLevel);
    }

    [Test]
    public void TargetFps_NotANumber_IsError()
    {
        StringAssert.Contains("targetFps", ParseError("{ \"targetFps\": \"sixty\" }"));
    }

    [TestCase(14)]
    [TestCase(301)]
    public void TargetFps_OutOfRange_IsError(int fps)
    {
        StringAssert.Contains("вне диапазона", ParseError("{ \"targetFps\": " + fps + " }"));
    }

    [Test]
    public void LogLevel_Unknown_IsError()
    {
        StringAssert.Contains("logLevel", ParseError("{ \"logLevel\": \"loud\" }"));
    }

    [Test]
    public void UnknownKey_IsError()
    {
        StringAssert.Contains("неизвестный параметр", ParseError("{ \"targetFPS\": 60 }"));
    }

    [TestCase("")]
    [TestCase("targetFps: 60")]
    [TestCase("{ \"targetFps\": 60 ")]
    [TestCase("{ \"targetFps\": 60 } x")]
    public void BrokenJson_IsError(string json)
    {
        StringAssert.Contains("битый JSON", ParseError(json));
    }

    [Test]
    public void BuildInfo_FormatContainsVersionAndCommit()
    {
        Assert.AreEqual("BeerAndDragon 0.1.0+a13fe29 (Release)", BuildInfo.Format("BeerAndDragon", "0.1.0", "a13fe29", false, "Release"));
        Assert.AreEqual("BeerAndDragon 0.1.0+a13fe29-dirty (Development)", BuildInfo.Format("BeerAndDragon", "0.1.0", "a13fe29", true, "Development"));
    }

    [Test]
    public void BuildInfo_ParseBrokenJson_GivesUnknown()
    {
        Assert.AreEqual("unknown", BuildInfo.Parse("not json").commit);
        Assert.AreEqual("abc1234", BuildInfo.Parse("{\"commit\":\"abc1234\",\"dirty\":false}").commit);
    }
}
