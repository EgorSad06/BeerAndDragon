using System;
using UnityEngine;

// Идентификатор версии сборки: версия продукта + короткий SHA коммита (+ "-dirty", если собрано
// с незакоммиченными изменениями) + режим сборки. По SHA однозначно находится исходный код:
//   git show <sha>   /   git checkout <sha>
// Файл BuildInfo/Resources/build_info.json пишет BuildInfoGenerator перед сборкой (в CI -- шаг workflow),
// в git он не хранится. Пример строки:  BeerAndDragon 0.1.0+a13fe29 (Release)
public static class BuildInfo
{
    public const string ResourceName = "build_info";

    [Serializable]
    public class Data
    {
        public string commit = "unknown";
        public bool dirty;
        public string builtAt = "";
    }

    private static Data data;

    public static Data Info
    {
        get
        {
            if (data != null) return data;
            TextAsset t = Resources.Load<TextAsset>(ResourceName);
            data = t != null ? Parse(t.text) : new Data();
            return data;
        }
    }

    public static string Version => Application.version;
    public static string Commit => Info.commit;
    public static bool Dirty => Info.dirty;

    // Debug.isDebugBuild = true в редакторе и в Development-сборке
    public static string BuildType => Application.isEditor ? "Editor" : Debug.isDebugBuild ? "Development" : "Release";

    public static string Full => Format(Application.productName, Version, Commit, Dirty, BuildType);

    public static string Format(string product, string version, string commit, bool dirty, string buildType) =>
        $"{product} {version}+{commit}{(dirty ? "-dirty" : "")} ({buildType})";

    public static Data Parse(string json)
    {
        Data d = null;
        try { d = JsonUtility.FromJson<Data>(json); }
        catch (ArgumentException) { }
        if (d == null || string.IsNullOrEmpty(d.commit)) d = new Data();
        return d;
    }
}
