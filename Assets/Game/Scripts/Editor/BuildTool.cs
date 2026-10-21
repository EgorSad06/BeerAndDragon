using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Сборка компонента в двух режимах -- из меню или из командной строки (так её запускает инструкция и CI):
//
//   Unity.exe -batchmode -quit -projectPath . -executeMethod BuildTool.BuildDevelopment -buildOutput build/dev
//   Unity.exe -batchmode -quit -projectPath . -executeMethod BuildTool.BuildRelease     -buildOutput build/release
//   (необязательно: -buildTarget StandaloneWindows64 | StandaloneLinux64; по умолчанию Windows)
//
// Development = BuildOptions.Development | AllowDebugging: скрипты компилируются без оптимизаций
//   (DebuggableAttribute: DisableOptimizations), с .pdb, определены DEVELOPMENT_BUILD и UNITY_ASSERTIONS
//   (Debug.Assert работает), можно подключить отладчик и Profiler.
// Release = BuildOptions.None: скрипты с оптимизацией, без .pdb, Debug.Assert вырезаны компилятором.
public static class BuildTool
{
    [MenuItem("Tools/BeerAndDragon/Build/Development")]
    public static void BuildDevelopment() => Build(true);

    [MenuItem("Tools/BeerAndDragon/Build/Release")]
    public static void BuildRelease() => Build(false);

    static void Build(bool development)
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = ArgValue(args, "-buildOutput") ?? (development ? "build/dev" : "build/release");
        BuildTarget target = BuildTarget.StandaloneWindows64;
        string targetArg = ArgValue(args, "-buildTarget");
        if (targetArg != null && !Enum.TryParse(targetArg, true, out target))
            Fail($"неизвестная платформа -buildTarget {targetArg}");

        string exe = target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows
            ? "BeerAndDragon.exe" : "BeerAndDragon.x86_64";
        BuildOptions options = development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None;
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        BuildInfoGenerator.Write();
        BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
        // Флаги перечисляем явно: BuildOptions.ToString() печатает лишние имена (в перечислении есть элементы с одинаковыми битами)
        Debug.Log($"[Build] режим={(development ? "Development" : "Release")} target={target} " +
                  $"Development={(options & BuildOptions.Development) != 0} AllowDebugging={(options & BuildOptions.AllowDebugging) != 0} " +
                  $"backend={PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group))} " +
                  $"stripping={PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group))} " +
                  $"version={PlayerSettings.bundleVersion} out={output}/{exe}");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(output, exe),
            target = target,
            options = options,
        });

        BuildSummary s = report.summary;
        Debug.Log($"[Build] результат={s.result} размер={s.totalSize / (1024 * 1024)} МБ время={s.totalTime.TotalSeconds:0} с " +
                  $"ошибок={s.totalErrors} предупреждений={s.totalWarnings}");
        if (s.result != BuildResult.Succeeded) Fail("сборка не удалась");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Fail(string message)
    {
        Debug.LogError("[Build] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(1);
        throw new BuildFailedException(message);
    }

    static string ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}

// Пишет Assets/Game/BuildInfo/Resources/build_info.json (папка в .gitignore) (коммит, признак незакоммиченных изменений, время сборки).
// Вызывается перед любой сборкой: из BuildTool и как IPreprocessBuildWithReport (сборка из меню, GameCI).
// Источник SHA: git; если git недоступен (контейнер CI) -- переменная GITHUB_SHA; иначе файл,
// заранее записанный шагом CI, остаётся как есть.
public class BuildInfoGenerator : IPreprocessBuildWithReport
{
    public const string FilePath = "Assets/Game/BuildInfo/Resources/" + BuildInfo.ResourceName + ".json";

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Write();

    public static void Write()
    {
        string sha = Git("rev-parse --short=7 HEAD");
        bool dirty = false;
        if (sha != null)
        {
            // Изменения в самом build_info.json и в каталоге сборки не считаются
            string status = Git("status --porcelain") ?? "";
            dirty = status.Split('\n').Select(l => l.Trim()).Any(l => l.Length > 0 && !l.Contains(BuildInfo.ResourceName + ".json"));
        }
        else
        {
            string env = Environment.GetEnvironmentVariable("GITHUB_SHA");
            if (!string.IsNullOrEmpty(env)) sha = env.Substring(0, Math.Min(7, env.Length));
        }

        if (sha == null)
        {
            if (File.Exists(FilePath))
            {
                Debug.Log("[Build] git недоступен -- оставляю " + FilePath + ": " + File.ReadAllText(FilePath).Trim());
                return;
            }
            sha = "unknown";
        }

        BuildInfo.Data d = new BuildInfo.Data { commit = sha, dirty = dirty, builtAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
        AssetDatabase.ImportAsset(FilePath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Build] build_info: commit={sha} dirty={dirty}");
    }

    static string Git(string arguments)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            using Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception)
        {
            return null;   // git не установлен
        }
    }
}
