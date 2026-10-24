using System.Collections.Generic;
using System.Linq;
using REEBOO.RetroFx.Lite;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Экран «как на PS1» (Tools/BeerAndDragon/Setup PS1 Look):
//  - во все рендереры URP (PC_Renderer, Mobile_Renderer) ставятся фичи Retro FX Lite (REEBOO, Assets/REEBOO --
//    пикселизация) и RetroColorFeature (свой: уровни цвета + дизеринг), в этом порядке;
//  - настройки -- в профиль по умолчанию URP (Assets/Game/Settings/DefaultVolumeProfile), значит, во всех сценах:
//    Intensity 1, Pixel Resolution 240, Colour Levels 32, Dither 0.55; сканлайны и кривизна экрана выключены;
//    дымка: рыжая (как закат на небе), с 6 до 45 м, вдали 85%, на небе 25%, 8 ступеней;
//  - небо: закат Assets/Game/Assets/Sky/SunsetSky.png (развёртка куба «крестом» 4x3) -> кубмапа и материал
//    SunsetSky.mat (Skybox/Cubemap); ставится в открытую сцену (Lighting -> Environment -> Skybox Material).
// PSX-шейдеры поверхностей (дрожание вершин, аффинные текстуры) не ставятся -- материалы остаются свои.
public static class RetroLookSetupTool
{
    const string Profile = "Assets/Game/Settings/DefaultVolumeProfile.asset";
    const int PixelResolution = 240;
    const string SkyTexture = "Assets/Game/Assets/Sky/SunsetSky.png";
    const string SkyMaterial = "Assets/Game/Assets/Sky/SunsetSky.mat";

    [MenuItem("Tools/BeerAndDragon/Setup PS1 Look")]
    public static void Setup()
    {
        foreach (ScriptableRendererData data in Renderers())
        {
            AddFeature<RetroFxLiteFeature>(data, "RetroFxLite", Shader.Find(RetroFxLiteFeature.ScreenShaderName));
            AddFeature<RetroColorFeature>(data, "RetroColor", Shader.Find(RetroColorFeature.ShaderName));
        }

        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Profile);
        RetroFxLiteVolume screen = Override<RetroFxLiteVolume>(profile);
        screen.intensity.Override(1f);
        screen.pixelResolution.Override(PixelResolution);
        screen.scanlines.Override(0f);
        screen.scanlineCount.Override(0);
        screen.curvature.Override(0f);
        RetroColorVolume colour = Override<RetroColorVolume>(profile);
        colour.colourLevels.Override(32);
        colour.dither.Override(0.55f);
        colour.pixelResolution.Override(PixelResolution);
        colour.fogMax.Override(0.85f);
        colour.fogColor.Override(new Color(0.78f, 0.36f, 0.14f));
        colour.skyFog.Override(0.25f);
        colour.fogStart.Override(6f);
        colour.fogEnd.Override(45f);
        colour.fogSteps.Override(8);
        EditorUtility.SetDirty(screen);
        EditorUtility.SetDirty(colour);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        RenderSettings.skybox = Sky();
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[PS1 Look] renderers: " + string.Join(", ", Renderers().Select(r => r.name)) + "; profile " + Profile);
    }

    // Кубмапа из «креста» и материал неба (создаются один раз)
    public static Material Sky()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SkyTexture);
        if (importer.textureShape != TextureImporterShape.TextureCube)
        {
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Skybox/Cubemap"));
            AssetDatabase.CreateAsset(mat, SkyMaterial);
        }
        mat.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(SkyTexture));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static IEnumerable<ScriptableRendererData> Renderers()
    {
        var assets = new List<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);
        for (int i = 0; i < QualitySettings.count; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q && !assets.Contains(q)) assets.Add(q);
        var result = new List<ScriptableRendererData>();
        foreach (UniversalRenderPipelineAsset a in assets)
            foreach (ScriptableRendererData r in a.rendererDataList)
                if (r != null && !result.Contains(r)) result.Add(r);
        return result;
    }

    // Фича в конец списка рендерера (если её ещё нет), с назначенным шейдером -- так он попадёт в сборку
    private static void AddFeature<T>(ScriptableRendererData data, string name, Shader shader) where T : ScriptableRendererFeature
    {
        if (data.rendererFeatures.Any(f => f is T)) return;
        T feature = ScriptableObject.CreateInstance<T>();
        feature.name = name;
        var fso = new SerializedObject(feature);
        fso.FindProperty("shader").objectReferenceValue = shader;
        fso.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.AddObjectToAsset(feature, data);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id);
        var so = new SerializedObject(data);
        SerializedProperty list = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();
        data.SetDirty();
        EditorUtility.SetDirty(data);
    }

    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T existing)) return existing;
        T component = profile.Add<T>(true);
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }
}
