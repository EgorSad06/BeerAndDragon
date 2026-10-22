using System.Linq;
using UnityEditor;
using UnityEngine;

// Настройки импорта моделей врагов из Assets/Game/Assets/enemies (их собирают скрипты tools/blender/*.py):
//  - риг Humanoid (подходят анимации с других гуманоидов, например Mixamo);
//  - клипы получают короткие имена: "Rig|Rig|Walk" -> "Walk"; Idle и Walk зациклены;
//  - текстуры -- PSX: point-фильтр, без мип-карт и сжатия.
public class EnemyModelImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Game/Assets/enemies/";
    private static readonly string[] Looped = { "Idle", "Walk" };

    private bool IsEnemyAsset => assetPath.Replace('\\', '/').StartsWith(Folder);

    void OnPreprocessModel()
    {
        if (!IsEnemyAsset) return;
        ModelImporter m = (ModelImporter)assetImporter;
        m.animationType = ModelImporterAnimationType.Human;
        m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        m.importAnimation = true;
        m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        m.materialLocation = ModelImporterMaterialLocation.External;
    }

    void OnPreprocessAnimation()
    {
        if (!IsEnemyAsset) return;
        ModelImporter m = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = m.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        foreach (ModelImporterClipAnimation c in clips)
        {
            c.name = c.takeName.Split('|').Last();
            c.loopTime = Looped.Contains(c.name);
            c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = c.loopTime;
        }
        m.clipAnimations = clips;
    }

    // FBX не переносит свечение в URP: глаза (*EyeFire, *EyeFlame) светятся синим в HDR, остальное матовое
    void OnPostprocessMaterial(Material material)
    {
        if (!IsEnemyAsset) return;
        material.SetFloat("_Smoothness", 0.05f);
        Color glow = material.name.EndsWith("EyeFire") ? new Color(0.05f, 0.3f, 1f) * 6f
                   : material.name.EndsWith("EyeFlame") ? new Color(0.15f, 0.55f, 1f) * 4f
                   : Color.black;
        if (glow == Color.black) return;
        glow.a = 1f;
        material.SetColor("_BaseColor", new Color(0.3f, 0.6f, 1f));
        material.SetColor("_EmissionColor", glow);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    void OnPreprocessTexture()
    {
        if (!IsEnemyAsset) return;
        TextureImporter t = (TextureImporter)assetImporter;
        t.filterMode = FilterMode.Point;
        t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
