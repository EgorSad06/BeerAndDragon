using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Конфигурация компонента -- плоский JSON без секретов.
//   Путь: аргумент запуска -config <путь>; по умолчанию <Data>/StreamingAssets/game-config.json
//   (пример лежит в репозитории: Assets/StreamingAssets/game-config.json).
//
//   targetFps -- целое 15..300: ограничение частоты кадров (видно в smoke-отчёте по avgFps)
//   logLevel  -- "info" | "verbose": verbose включает подробный журнал, как аргумент -diag
//
// Неверное значение, неизвестный ключ, битый JSON, отсутствующий файл, заданный через -config, --
// понятная ошибка в журнале [ERROR][Config] и выход с кодом 2 (ExitCodeConfigError).
// Проверка сделана явно, а не через Debug.Assert: assert вырезается из Release-сборки.
public static class GameConfig
{
    public const int ExitCodeConfigError = 2;
    public const int MinFps = 15, MaxFps = 300;
    public const string FileName = "game-config.json";

    public class Values
    {
        public int targetFps = 60;
        public string logLevel = "info";
    }

    public static Values Current { get; private set; } = new Values();
    public static string SourcePath { get; private set; }

    // Загружает конфигурацию по аргументам запуска. false -- ошибка (текст в error).
    public static bool Load(string[] args, out string error)
    {
        string explicitPath = GameLog.ArgValue(args, "-config");
        string path = explicitPath ?? Path.Combine(Application.streamingAssetsPath, FileName);
        SourcePath = path;

        if (!File.Exists(path))
        {
            if (explicitPath != null)
            {
                error = $"файл конфигурации не найден: {path}";
                return false;
            }
            Current = new Values();   // штатный файл не положили -- работаем на значениях по умолчанию
            SourcePath = "(по умолчанию)";
            error = null;
            return true;
        }

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e)
        {
            error = $"не удалось прочитать {path}: {e.Message}";
            return false;
        }

        if (!TryParse(text, out Values v, out error))
        {
            error = $"{path}: {error}";
            return false;
        }
        Current = v;
        return true;
    }

    // Применение к движку: ограничение кадров и уровень журнала
    public static void Apply()
    {
        Debug.Assert(Current.targetFps >= MinFps && Current.targetFps <= MaxFps, "targetFps прошёл проверку, но вне диапазона");
        QualitySettings.vSyncCount = 0;   // иначе targetFrameRate игнорируется
        Application.targetFrameRate = Current.targetFps;
        if (Current.logLevel == "verbose" && !GameLog.VerboseEnabled) GameLog.SetVerbose(true);
    }

    // ---- Разбор и проверка (чистые функции -- покрыты EditMode-тестами) ----

    public static bool TryParse(string json, out Values values, out string error)
    {
        values = new Values();
        if (!FlatJson.TryParse(json, out Dictionary<string, FlatJson.Value> map, out error)) return false;

        foreach (KeyValuePair<string, FlatJson.Value> kv in map)
        {
            switch (kv.Key)
            {
                case "targetFps":
                    if (kv.Value.IsString || !int.TryParse(kv.Value.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps))
                    {
                        error = $"targetFps = {kv.Value.Raw} -- ожидается целое число {MinFps}..{MaxFps}";
                        return false;
                    }
                    if (fps < MinFps || fps > MaxFps)
                    {
                        error = $"targetFps = {fps} -- вне диапазона {MinFps}..{MaxFps}";
                        return false;
                    }
                    values.targetFps = fps;
                    break;

                case "logLevel":
                    string level = kv.Value.IsString ? kv.Value.Raw.ToLowerInvariant() : null;
                    if (level != "info" && level != "verbose")
                    {
                        error = $"logLevel = {kv.Value.Raw} -- ожидается \"info\" или \"verbose\"";
                        return false;
                    }
                    values.logLevel = level;
                    break;

                default:
                    error = $"неизвестный параметр \"{kv.Key}\" (допустимы: targetFps, logLevel)";
                    return false;
            }
        }
        error = null;
        return true;
    }

    public static string Describe(Values v) => $"targetFps={v.targetFps}, logLevel={v.logLevel}";
}

// Разбор плоского JSON-объекта { "ключ": число | "строка" | true/false }. JsonUtility не подходит:
// на значение неверного типа он молча ставит 0 и не даёт понять, что конфигурация ошибочна.
public static class FlatJson
{
    public struct Value
    {
        public string Raw;
        public bool IsString;
    }

    public static bool TryParse(string json, out Dictionary<string, Value> map, out string error)
    {
        map = new Dictionary<string, Value>();
        error = null;
        int i = 0;
        string s = json ?? "";

        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != '{') return Fail(out error, "ожидается JSON-объект { ... }");
        i++;
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') return End(s, i + 1, out error);

        while (true)
        {
            SkipWs(s, ref i);
            if (!ReadString(s, ref i, out string key)) return Fail(out error, $"ожидается имя параметра в кавычках (символ {i + 1})");
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != ':') return Fail(out error, $"после \"{key}\" ожидается ':'");
            i++;
            SkipWs(s, ref i);

            Value v;
            if (i < s.Length && s[i] == '"')
            {
                if (!ReadString(s, ref i, out string str)) return Fail(out error, $"незакрытая строка у \"{key}\"");
                v = new Value { Raw = str, IsString = true };
            }
            else
            {
                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
                if (i == start) return Fail(out error, $"нет значения у \"{key}\"");
                v = new Value { Raw = s.Substring(start, i - start), IsString = false };
            }
            if (map.ContainsKey(key)) return Fail(out error, $"параметр \"{key}\" указан дважды");
            map[key] = v;

            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') return End(s, i + 1, out error);
            return Fail(out error, $"после \"{key}\" ожидается ',' или '}}'");
        }
    }

    private static bool End(string s, int i, out string error)
    {
        SkipWs(s, ref i);
        if (i < s.Length) return Fail(out error, "лишние символы после закрывающей '}'");
        error = null;
        return true;
    }

    private static bool Fail(out string error, string message)
    {
        error = "битый JSON: " + message;
        return false;
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static bool ReadString(string s, ref int i, out string value)
    {
        value = null;
        if (i >= s.Length || s[i] != '"') return false;
        int end = s.IndexOf('"', i + 1);
        if (end < 0) return false;
        value = s.Substring(i + 1, end - i - 1);
        i = end + 1;
        return true;
    }
}
