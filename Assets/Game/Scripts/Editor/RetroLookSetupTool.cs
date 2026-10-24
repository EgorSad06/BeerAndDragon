using System.Collections.Generic;
using System.Linq;
using REEBOO.RetroFx.Lite;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Экран «как на PS1» (Tools/BeerAndDragon/Setup PS1 Look):
//  - во все рендереры URP (PC_Renderer, Mobile_Renderer) ставятся фичи Retro FX Lite (REEBOO, Assets/REEBOO --
//    пикселизация) и RetroColorFeature (свой: уровни цвета + дизеринг), в этом порядке;
//  - настройки -- в профиль по умолчанию URP (Assets/Game/Settings/DefaultVolumeProfile), значит, во всех сценах:
//    Intensity 1, Pixel Resolution 240, Colour Levels 32, Dither 0.55; сканлайны и кривизна экрана выключены.
// PSX-шейдеры поверхностей (дрожание вершин, аффинные текстуры) не ставятся -- материалы остаются свои.
public static class RetroLookSetupTool
{
    const string Profile = "Assets/Game/Settings/DefaultVolumeProfile.asset";
    const int PixelResolution = 240;

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
        EditorUtility.SetDirty(screen);
        EditorUtility.SetDirty(colour);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[PS1 Look] renderers: " + string.Join(", ", Renderers().Select(r => r.name)) + "; profile " + Profile);
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
