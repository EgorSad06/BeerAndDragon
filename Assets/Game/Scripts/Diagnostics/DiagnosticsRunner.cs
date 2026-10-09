using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// Живёт всю игру (DontDestroyOnLoad), создаётся из GameLog.
//  - логирует загрузку сцен и время до первой отрисованной сцены;
//  - F11 -- включить/выключить диагностический режим;
//  - считает ошибки и исключения;
//  - режим -ciSmoke [сек]: подождать N секунд после загрузки сцены, записать отчёт
//    (-diagReport путь.json) и выйти с кодом 0 (ошибок не было) или 1.
public class DiagnosticsRunner : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F11;

    private bool smoke;
    private float smokeSeconds = 5f;
    private string reportPath;

    private float firstSceneTime = -1f;
    private float smokeStart = -1f;
    private int frames;
    private float fpsTime;
    private float minFps = float.MaxValue;
    private int errorCount, warningCount;

    public static void Create(string[] args)
    {
        GameObject go = new GameObject("[Diagnostics]");
        DontDestroyOnLoad(go);
        DiagnosticsRunner r = go.AddComponent<DiagnosticsRunner>();
        r.smoke = GameLog.HasArg(args, "-ciSmoke");
        string sec = GameLog.ArgValue(args, "-ciSmoke");
        if (sec != null && float.TryParse(sec, NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) r.smokeSeconds = s;
        r.reportPath = GameLog.ArgValue(args, "-diagReport");
        if (r.smoke) GameLog.Info("Smoke", $"Smoke-режим: выход через {r.smokeSeconds} с после загрузки сцены");
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.logMessageReceived += CountLogs;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.logMessageReceived -= CountLogs;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        float t = GameLog.SecondsSinceStartup;
        GameLog.Info("Scene", $"Загружена сцена '{scene.name}' ({mode}), объектов в корне: {scene.rootCount}, t={t:0.00} с");
        if (firstSceneTime < 0f) firstSceneTime = t;
        if (smoke && smokeStart < 0f) smokeStart = Time.realtimeSinceStartup;
    }

    private void CountLogs(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errorCount++;
        else if (type == LogType.Warning) warningCount++;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) GameLog.SetVerbose(!GameLog.VerboseEnabled);

        float dt = Time.unscaledDeltaTime;
        if (dt > 0f && firstSceneTime >= 0f)
        {
            frames++;
            fpsTime += dt;
            if (frames > 10) minFps = Mathf.Min(minFps, 1f / dt); // первые кадры после загрузки не считаем
        }

        if (GameLog.VerboseEnabled && Time.frameCount % 600 == 0)
            GameLog.Verbose("Perf", $"FPS ~{(fpsTime > 0 ? frames / fpsTime : 0):0}, память {System.GC.GetTotalMemory(false) / (1024 * 1024)} МБ");

        if (smoke && smokeStart >= 0f && Time.realtimeSinceStartup - smokeStart >= smokeSeconds)
            FinishSmoke();
    }

    private void FinishSmoke()
    {
        smoke = false;
        float avgFps = fpsTime > 0f ? frames / fpsTime : 0f;
        string json = "{\n" +
            $"  \"version\": \"{Application.version}\",\n" +
            $"  \"unity\": \"{Application.unityVersion}\",\n" +
            $"  \"platform\": \"{Application.platform}\",\n" +
            $"  \"firstSceneLoadedSeconds\": {firstSceneTime.ToString("0.000", CultureInfo.InvariantCulture)},\n" +
            $"  \"framesRendered\": {frames},\n" +
            $"  \"avgFps\": {avgFps.ToString("0.0", CultureInfo.InvariantCulture)},\n" +
            $"  \"minFps\": {(minFps == float.MaxValue ? 0f : minFps).ToString("0.0", CultureInfo.InvariantCulture)},\n" +
            $"  \"errors\": {errorCount},\n" +
            $"  \"warnings\": {warningCount}\n" +
            "}\n";

        if (!string.IsNullOrEmpty(reportPath))
        {
            try { File.WriteAllText(reportPath, json); }
            catch (System.Exception e) { GameLog.Warn("Smoke", "Не удалось записать отчёт: " + e.Message); }
        }

        GameLog.Info("Smoke", (errorCount == 0 ? "SMOKE OK" : "SMOKE FAIL") +
                              $" | старт сцены {firstSceneTime:0.00} с | ~{avgFps:0} FPS | ошибок {errorCount}");
        Application.Quit(errorCount == 0 ? 0 : 1);
    }
}
