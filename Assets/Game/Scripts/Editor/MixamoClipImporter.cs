using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Анимации Mixamo для скелетов: Assets/Game/Assets/enemies/Animations/Mixamo (FBX for Unity, 30 к/с).
//  - Humanoid, аватар из файла; материалы не импортируются (в части файлов есть модель X Bot -- она не нужна);
//  - один клип на файл, имя клипа = имя файла; shield_strafe -- два клипа: _left и зеркальный _right;
//  - часть ходьбы и бега снята с продвижением: горизонталь уходит в root motion (врага двигает ИИ, root motion
//    выключен), высота и поворот -- в позе;
//  - события: удары -- OnAttackHit, лучник -- OnArrowTake (взял стрелу), OnArrowNock (наложил), OnArrowRelease
//    (отпустил); кадры найдены по скорости и положению правой руки;
//  - archer_aim обрезан до натяжения и удержания (целиком -- 4 с «силового выстрела»).
public class MixamoClipImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Game/Assets/enemies/Animations/Mixamo/";

    private static readonly string[] Looped =
    {
        "creep_idle", "creep_walk", "creep_run", "shield_idle", "shield_walk", "shield_run", "shield_strafe_left",
        "shield_strafe_right", "shield_block_idle", "archer_idle", "archer_walk", "archer_strafe_left", "archer_strafe_right",
    };

    private static readonly (string clip, string function, int frame)[] Events =
    {
        ("creep_attack1", "OnAttackHit", 18), ("creep_attack2", "OnAttackHit", 29), ("creep_attack3", "OnAttackHit", 44),
        ("shield_attack1", "OnAttackHit", 24), ("shield_attack2", "OnAttackHit", 17),
        ("archer_draw", "OnArrowTake", 10), ("archer_draw", "OnArrowNock", 17), ("archer_shoot", "OnArrowRelease", 2),
    };

    private static readonly Dictionary<string, int> Frames = new Dictionary<string, int> { { "archer_aim", 40 } };

    private bool IsMixamo => assetPath.Replace('\\', '/').StartsWith(Folder);

    void OnPreprocessModel()
    {
        if (!IsMixamo) return;
        ModelImporter m = (ModelImporter)assetImporter;
        m.animationType = ModelImporterAnimationType.Human;
        m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        m.importAnimation = true;
        m.materialImportMode = ModelImporterMaterialImportMode.None;
        m.importCameras = m.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!IsMixamo) return;
        ModelImporter m = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] defaults = m.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0) return;
        string file = Path.GetFileNameWithoutExtension(assetPath);
        var clips = new List<ModelImporterClipAnimation>();
        if (file == "shield_strafe")
        {
            clips.Add(Clip(defaults[0], "shield_strafe_left", false));
            clips.Add(Clip(defaults[0], "shield_strafe_right", true));
        }
        else clips.Add(Clip(defaults[0], file, false));
        m.clipAnimations = clips.ToArray();
    }

    private static ModelImporterClipAnimation Clip(ModelImporterClipAnimation src, string name, bool mirror)
    {
        var c = new ModelImporterClipAnimation
        {
            takeName = src.takeName,
            name = name,
            firstFrame = src.firstFrame,
            lastFrame = Frames.TryGetValue(name, out int n) ? src.firstFrame + n : src.lastFrame,
            mirror = mirror,
            loopTime = Looped.Contains(name),
            lockRootRotation = true,
            lockRootHeightY = true,
            lockRootPositionXZ = false,
            keepOriginalOrientation = true,
            keepOriginalPositionY = true,
            keepOriginalPositionXZ = false,
        };
        c.events = Events.Where(e => e.clip == name)
            .Select(e => new AnimationEvent { functionName = e.function, time = e.frame / (c.lastFrame - c.firstFrame) })
            .ToArray();
        return c;
    }
}
