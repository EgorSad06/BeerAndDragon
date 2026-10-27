using UnityEditor;
using UnityEngine;

// Картинки портрета рыцаря (Resources/UI/Portrait, их грузит PlayerPortrait):
//  - Portrait* (рамка, кровь, череп) -- спрайты UI с прозрачностью; мип-карты (Kaiser, трилинейно, со сдвигом к
//    чёткости), иначе тонкий узор рамки при уменьшении рассыпается в кашу;
//  - KnightHelmet_0..4 -- стадии повреждений шлема (tools/blender/helmet_damage.py), обычные текстуры 1024.
// Картинки экрана смерти (Resources/UI/Death, их грузит DeathScreen):
//  - DeathCard -- карта таро: без сжатия и без подгонки к степени двойки, иначе тонкие линии гравюры плывут;
//  - DeathFog -- дымка, тайлится (шейдер её прокручивает).
// Картинки главного меню (Resources/UI/Menu, их грузит MainMenu): спрайты UI без сжатия -- фоны 1920x1080
// и рамки кнопок; мип-карты, чтобы уменьшенные рамки не рябили.
public class PortraitTextureImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Game/Resources/UI/Portrait/";
    private const string DeathFolder = "Assets/Game/Resources/UI/Death/";
    private const string MenuFolder = "Assets/Game/Resources/UI/Menu/";

    void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (path.StartsWith(MenuFolder))
        {
            TextureImporter m = (TextureImporter)assetImporter;
            m.textureType = TextureImporterType.Sprite;
            m.spriteImportMode = SpriteImportMode.Single;
            m.alphaIsTransparency = true;
            m.mipmapEnabled = true;
            m.filterMode = FilterMode.Trilinear;
            m.wrapMode = TextureWrapMode.Clamp;
            m.maxTextureSize = 2048;
            m.textureCompression = TextureImporterCompression.Uncompressed;
            return;
        }
        if (path.StartsWith(DeathFolder))
        {
            TextureImporter d = (TextureImporter)assetImporter;
            d.textureType = TextureImporterType.Default;
            d.npotScale = TextureImporterNPOTScale.None;
            d.mipmapEnabled = true;
            d.filterMode = FilterMode.Trilinear;
            d.textureCompression = TextureImporterCompression.Uncompressed;
            bool fog = System.IO.Path.GetFileName(path).StartsWith("DeathFog");
            d.wrapMode = fog ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            d.alphaIsTransparency = !fog;
            return;
        }
        if (!path.StartsWith(Folder)) return;
        TextureImporter t = (TextureImporter)assetImporter;
        t.wrapMode = TextureWrapMode.Clamp;
        if (System.IO.Path.GetFileName(assetPath).StartsWith("Portrait"))
        {
            t.textureType = TextureImporterType.Sprite;
            t.spriteImportMode = SpriteImportMode.Single;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = true;
            t.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            t.mipMapBias = -0.5f;
            t.filterMode = FilterMode.Trilinear;
            t.textureCompression = TextureImporterCompression.Uncompressed;
        }
        else
        {
            t.mipmapEnabled = false;
            t.textureType = TextureImporterType.Default;
            t.maxTextureSize = 1024;
            t.filterMode = FilterMode.Bilinear;
        }
    }
}
