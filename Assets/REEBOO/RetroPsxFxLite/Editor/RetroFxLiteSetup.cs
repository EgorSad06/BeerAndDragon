// Retro PSX & CRT FX LITE - REEBOO Labs. One-click setup for the Lite version.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace REEBOO.RetroFx.Lite.Editor
{
    public static class RetroFxLiteSetup
    {
        [MenuItem("Tools/REEBOO/Retro FX Lite/Setup Current Pipeline", false, 0)]
        private static void Menu()
        {
            string log = Setup();
            Debug.Log("[Retro FX Lite] " + log);
            EditorUtility.DisplayDialog("Retro FX Lite", log, "OK");
        }

        [MenuItem("Tools/REEBOO/Retro FX Lite/Get the Full Version", false, 100)]
        private static void Full() => Application.OpenURL("https://reebo0.itch.io/retro-psx-crt-fx-unity");

        /// <summary>Adds the Lite feature to every URP renderer in use and a global Volume to the active scene. Returns a log.</summary>
        public static string Setup()
        {
            var assets = new List<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);
            for (int i = 0; i < QualitySettings.count; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q && !assets.Contains(q)) assets.Add(q);
            if (assets.Count == 0) return "No URP asset is active. Retro FX Lite needs the Universal Render Pipeline.";

            var log = new System.Text.StringBuilder();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (var asset in assets)
                foreach (ScriptableRendererData data in asset.rendererDataList)
                {
                    if (data == null) continue;
                    if (data.rendererFeatures.Any(f => f is RetroFxLiteFeature)) { log.AppendLine(data.name + ": already set up."); continue; }
                    var feature = ScriptableObject.CreateInstance<RetroFxLiteFeature>();
                    feature.name = "RetroFxLite";
                    var fso = new SerializedObject(feature);
                    fso.FindProperty("shader").objectReferenceValue = Shader.Find(RetroFxLiteFeature.ScreenShaderName);
                    fso.ApplyModifiedPropertiesWithoutUndo();
                    Undo.RegisterCreatedObjectUndo(feature, "Add Retro FX Lite");
                    Undo.RecordObject(data, "Add Retro FX Lite");
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id);
                    var so = new SerializedObject(data);
                    var list = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                    so.ApplyModifiedProperties();
                    data.SetDirty();
                    EditorUtility.SetDirty(data);
                    log.AppendLine("Added Retro FX Lite to " + data.name + ".");
                }
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetActiveScene();
            bool hasVolume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(v => v.isGlobal && v.sharedProfile != null && v.sharedProfile.Has<RetroFxLiteVolume>());
            if (scene.IsValid() && !hasVolume)
            {
                var go = new GameObject("Retro FX Lite");
                Undo.RegisterCreatedObjectUndo(go, "Create Retro FX Lite Volume");
                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 10f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                string dir = string.IsNullOrEmpty(scene.path) ? "Assets" : Path.GetDirectoryName(scene.path).Replace('\\', '/');
                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{(string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name)}_RetroFxLiteProfile.asset");
                AssetDatabase.CreateAsset(profile, path);
                var fx = profile.Add<RetroFxLiteVolume>(true);
                fx.pixelResolution.Override(240);
                fx.scanlines.Override(0.35f);
                fx.name = "RetroFxLiteVolume";
                fx.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(fx, profile);
                AssetDatabase.SaveAssets();
                vol.sharedProfile = profile;
                EditorSceneManager.MarkSceneDirty(scene);
                log.AppendLine("Created global Volume with profile " + path + ".");
            }
            Undo.CollapseUndoOperations(group);
            return log.ToString();
        }
    }
}
