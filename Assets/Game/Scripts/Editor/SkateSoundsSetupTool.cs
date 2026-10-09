using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > BeerAndDragon > Setup Skate Sounds
//   Вешает SkateSounds на игрока и подставляет звуки из Assets/Game/Assets/Sounds/JetSet:
//   все "spray" -- звуки трюков (по номеру), "success" -- финальный звук за 4 трюка подряд.
public static class SkateSoundsSetupTool
{
    const string SoundDir = "Assets/Game/Assets/Sounds/JetSet";

    [MenuItem("Tools/BeerAndDragon/Setup Skate Sounds")]
    public static void Setup()
    {
        KnightMovement player = Object.FindFirstObjectByType<KnightMovement>();
        if (player == null || player.GetComponent<SkaterController>() == null)
        {
            Debug.LogWarning("[SkateSounds] Нужен игрок со SkaterController -- сначала Tools > BeerAndDragon > Setup Skateboard.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SoundDir });
        var clips = guids.Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)))
                         .Where(c => c != null)
                         .ToList();

        AudioClip[] sprays = clips.Where(c => c.name.ToLower().Contains("spray"))
                                  .OrderBy(c => c.name)
                                  .ToArray();
        AudioClip success = clips.FirstOrDefault(c => c.name.ToLower().Contains("success"));
        if (sprays.Length == 0) sprays = clips.Where(c => c != success).OrderBy(c => c.name).ToArray();

        SkateSounds s = player.GetComponent<SkateSounds>();
        if (s == null) s = Undo.AddComponent<SkateSounds>(player.gameObject);
        Undo.RecordObject(s, "Skate Sounds");
        s.trickClips = sprays;
        s.comboFinisher = success;
        EditorUtility.SetDirty(s);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log($"[SkateSounds] Трюки: {string.Join(", ", sprays.Select(c => c.name))} | финал: {(success != null ? success.name : "-")}");
    }
}
