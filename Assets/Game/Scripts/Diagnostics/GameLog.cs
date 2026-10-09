using System;
using System.IO;
using System.Text;
using UnityEngine;

// Минимальная диагностика игры.
//  - При старте пишет в лог: версию игры, Unity, платформу, железо, аргументы запуска.
//  - Все сообщения Unity (включая ошибки и исключения) дублируются в файл
//    <persistentDataPath>/logs/game_<дата>.log -- его можно приложить к баг-репорту.
//  - GameLog.Info / Warn / Error -- для существенных операций (инвентарь, смерть, скейт...).
//  - Диагностический режим: аргумент запуска -diag (или F11 в игре) включает Verbose-сообщения.
//  - Smoke-режим для CI: -ciSmoke [секунды] -- игра сама выходит после загрузки сцены
//    и пишет отчёт со временем старта (-diagReport <путь к json>).
public static class GameLog
{
    public static bool VerboseEnabled { get; private set; }
    public static string LogFilePath { get; private set; }

    private static StreamWriter writer;
    private static readonly object fileLock = new object();
    private static float startupRealtime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        VerboseEnabled = false;
        writer = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        VerboseEnabled = HasArg(args, "-diag");
        startupRealtime = Time.realtimeSinceStartup;

        OpenFile();
        Application.logMessageReceivedThreaded += OnUnityLog;
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Error("Crash", "Необработанное исключение: " + e.ExceptionObject);
        Application.quitting += () =>
        {
            Info("App", $"Выход. Время работы {Time.realtimeSinceStartup:0.0} с");
            CloseFile();
        };

        Info("App", $"Старт {Application.productName} {Application.version} | Unity {Application.unityVersion} | " +
                    $"{Application.platform} | {SystemInfo.operatingSystem}");
        Info("App", $"CPU {SystemInfo.processorType} x{SystemInfo.processorCount}, RAM {SystemInfo.systemMemorySize} МБ, " +
                    $"GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
        Info("App", "Аргументы: " + string.Join(" ", args));
        if (LogFilePath != null) Info("App", "Журнал: " + LogFilePath);
        if (VerboseEnabled) Info("App", "Диагностический режим включён (-diag)");

        DiagnosticsRunner.Create(args);
    }

    public static void Info(string area, string message) => Debug.Log(Format("INFO", area, message));
    public static void Warn(string area, string message) => Debug.LogWarning(Format("WARN", area, message));
    public static void Error(string area, string message) => Debug.LogError(Format("ERROR", area, message));

    // Подробные сообщения -- только в диагностическом режиме
    public static void Verbose(string area, string message)
    {
        if (VerboseEnabled) Debug.Log(Format("DEBUG", area, message));
    }

    public static void SetVerbose(bool enabled)
    {
        VerboseEnabled = enabled;
        Info("App", "Диагностический режим " + (enabled ? "включён" : "выключен"));
    }

    public static float SecondsSinceStartup => Time.realtimeSinceStartup - startupRealtime;

    private static string Format(string level, string area, string message) => $"[{level}][{area}] {message}";

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        lock (fileLock)
        {
            if (writer == null) return;
            StringBuilder sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(type).Append(": ").Append(condition);
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
                sb.AppendLine().Append(stackTrace.TrimEnd());
            writer.WriteLine(sb.ToString());
        }
    }

    private static void OpenFile()
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "logs");
            Directory.CreateDirectory(dir);
            LogFilePath = Path.Combine(dir, $"game_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            writer = new StreamWriter(LogFilePath, false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e)
        {
            LogFilePath = null;
            Debug.LogWarning("[WARN][App] Не удалось открыть файл журнала: " + e.Message);
        }
    }

    private static void CloseFile()
    {
        lock (fileLock)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    public static bool HasArg(string[] args, string name)
    {
        foreach (string a in args)
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
