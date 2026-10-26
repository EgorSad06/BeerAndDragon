using UnityEditor;
using UnityEngine;

// Картинки портрета рыцаря (Resources/UI/Portrait, их грузит PlayerPortrait):
//  - Portrait* (рамка, кровь, череп) -- спрайты UI с прозрачностью; мип-карты (Kaiser, трилинейно, со сдвигом к
//    чёткости), иначе тонкий узор рамки при уменьшении рассыпается в кашу;
//  - KnightHelmet_0..4 -- стадии повреждений шлема (tools/blender/helmet_damage.py), обычные текстуры 1024.
public class PortraitTextureImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Game/Resources/UI/Portrait/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
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
