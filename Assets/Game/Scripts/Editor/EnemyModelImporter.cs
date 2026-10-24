using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Настройки импорта моделей врагов из Assets/Game/Assets/enemies (их собирают скрипты tools/blender/*.py):
//  - риг Humanoid (на него ложатся анимации Mixamo; их импорт -- MixamoClipImporter, папка Animations/Mixamo);
//  - клипы получают короткие имена: "Rig|Rig|Walk" -> "Walk"; Idle, Walk и Block зациклены; движение корня (наклон и
//    опускание таза в Death, шаг в Attack) запечено в позу -- враг двигается скриптом, root motion не нужен;
//  - события: в Shoot (лучник) -- OnArrowRelease на кадре, где отпущена тетива; в Attack -- OnAttackHit на кадре
//    удара (SkeletonCreep.ArrowReleased / AttackHit); модели из StaticModels (стрела) -- без рига;
//  - меши читаемые (Read/Write) -- SkeletonDeath режет их на кости во время игры;
//  - материалы: матовые; *EyeFire светится синим (HDR); *EyeGlow -- аддитивный ореол с текстурой Textures/<имя>.png;
//    если есть Textures/<материал>_Emission.png -- это карта свечения (синие глаза на текстуре скелета);
//  - текстуры -- PSX: point-фильтр, без мип-карт и сжатия; у ореола края не повторяются (Clamp).
public class EnemyModelImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Game/Assets/enemies/";
    private static readonly string[] Looped = { "Idle", "Walk", "Block" };
    private const float ShootReleaseFrame = 2f;   // от начала клипа Shoot (tools/blender/skeleton_archer.py)
    private const float AttackHitFrame = 14f;     // от начала клипа Attack: удар проходит перед скелетом
    private static readonly string[] StaticModels = { "Arrow" };   // без рига и анимаций (снаряды)

    private bool IsEnemyAsset
    {
        get
        {
            string path = assetPath.Replace('\\', '/');
            return path.StartsWith(Folder) && !path.StartsWith(MixamoClipImporter.Folder);
        }
    }

    void OnPreprocessModel()
    {
        if (!IsEnemyAsset) return;
        ModelImporter m = (ModelImporter)assetImporter;
        string name = Path.GetFileNameWithoutExtension(assetPath);
        if (StaticModels.Contains(name))
        {
            m.animationType = ModelImporterAnimationType.None;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            m.materialLocation = ModelImporterMaterialLocation.External;
            return;
        }
        m.animationType = ModelImporterAnimationType.Human;
        m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        m.importAnimation = true;
        m.isReadable = true;   // SkeletonDeath режет скин-меш на кости во время игры
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
            c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            if (c.name == "Shoot") c.events = new[] { Event("OnArrowRelease", ShootReleaseFrame, c) };
            if (c.name == "Attack") c.events = new[] { Event("OnAttackHit", AttackHitFrame, c) };
        }
        m.clipAnimations = clips;
    }

    private static AnimationEvent Event(string function, float frame, ModelImporterClipAnimation c) =>
        new AnimationEvent { functionName = function, time = frame / (c.lastFrame - c.firstFrame) };

    // FBX не переносит в URP свечение и прозрачность -- задаём здесь
    void OnPostprocessMaterial(Material material)
    {
        if (!IsEnemyAsset) return;
        material.SetFloat("_Smoothness", 0.05f);
        if (material.name.EndsWith("EyeGlow")) MakeAdditiveGlow(material);
        else if (material.name.EndsWith("EyeFire")) MakeGlow(material, new Color(0.05f, 0.3f, 1f) * 6f);
        else
        {
            Texture2D map = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(material.name + "_Emission"));
            if (map != null) AddEmissionMap(material, map);
        }
    }

    private string TexturePath(string name) =>
        Path.Combine(Path.GetDirectoryName(assetPath), "Textures", name + ".png").Replace('\\', '/');

    // Светятся только непустые пиксели карты (глаза на текстуре скелета)
    private static void AddEmissionMap(Material material, Texture2D map)
    {
        material.SetTexture("_EmissionMap", map);
        Color hdr = new Color(0.45f, 0.7f, 1f) * 2.5f;   // синее, без пересвета в бирюзу
        hdr.a = 1f;
        material.SetColor("_EmissionColor", hdr);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    private static void MakeGlow(Material material, Color hdr)
    {
        hdr.a = 1f;
        material.SetColor("_BaseColor", new Color(0.3f, 0.6f, 1f));
        material.SetColor("_EmissionColor", hdr);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    // Ореол: Unlit, аддитивно (чёрный фон текстуры не виден), двусторонний, без записи глубины
    private void MakeAdditiveGlow(Material material)
    {
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        string tex = Path.Combine(Path.GetDirectoryName(assetPath), "Textures", material.name + ".png").Replace('\\', '/');
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);   // Additive
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    void OnPreprocessTexture()
    {
        if (!IsEnemyAsset) return;
        TextureImporter t = (TextureImporter)assetImporter;
        t.filterMode = FilterMode.Point;
        t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed;
        if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("EyeGlow"))
            t.wrapMode = TextureWrapMode.Clamp;
    }
}
