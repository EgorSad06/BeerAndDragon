using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Настройки импорта моделей врагов из Assets/Game/Assets/enemies (их собирают скрипты tools/blender/*.py):
//  - риг Humanoid (подходят анимации с других гуманоидов, например Mixamo);
//  - клипы получают короткие имена: "Rig|Rig|Walk" -> "Walk"; Idle и Walk зациклены;
//  - материалы: матовые; *EyeFire светится синим (HDR); *EyeGlow -- аддитивный ореол с текстурой Textures/<имя>.png;
//  - текстуры -- PSX: point-фильтр, без мип-карт и сжатия; у ореола края не повторяются (Clamp).
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

    // FBX не переносит в URP свечение и прозрачность -- задаём здесь
    void OnPostprocessMaterial(Material material)
    {
        if (!IsEnemyAsset) return;
        material.SetFloat("_Smoothness", 0.05f);
        if (material.name.EndsWith("EyeGlow")) MakeAdditiveGlow(material);
        else if (material.name.EndsWith("EyeFire")) MakeGlow(material, new Color(0.05f, 0.3f, 1f) * 6f);
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
