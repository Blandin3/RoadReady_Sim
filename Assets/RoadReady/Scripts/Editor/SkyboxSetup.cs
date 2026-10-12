using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Day-Night Skyboxes per scene: each scenario gets its own time of day (all daylight, so hazards stay visible),
    /// and the lobby gets a bright morning sky. Textures are clamped (no seams) and capped at 1024 px for the Quest.
    /// </summary>
    public static class SkyboxSetup
    {
        const string Pack = "Assets/Day-Night Skyboxes";
        const string BootstrapScene = GreyboxKit.GeneratedRoot + "/Scenes/RR_Bootstrap.unity";

        public static bool Available => AssetDatabase.LoadAssetAtPath<Material>($"{Pack}/Materials/SkyNoon.mat") != null;

        /// <summary>Sky for a generated scene (by scene name); null if the pack is missing.</summary>
        public static Material For(string sceneName)
        {
            var sky = sceneName switch
            {
                "RR_Tutorial" => "SkyBrightMorning",
                "RR_ZebraCrossing" => "SkyMorning",
                "RR_SignalizedIntersection" => "SkyNoon",
                "RR_MotoTaxiJunction" => "SkyAfterNoon",
                "RR_OpenRoad" => "SkyCloudy",
                _ => "SkyBrightMorning",
            };
            PrepareTextures();
            return AssetDatabase.LoadAssetAtPath<Material>($"{Pack}/Materials/{sky}.mat");
        }

        /// <summary>Assigns the scene's sky to the open scene's lighting settings.</summary>
        public static void Apply(string sceneName)
        {
            var sky = For(sceneName);
            if (sky != null)
                RenderSettings.skybox = sky;
        }

        public static void ApplyToBootstrap()
        {
            if (!Available)
                return;
            var scene = EditorSceneManager.GetActiveScene().path == BootstrapScene
                ? EditorSceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(BootstrapScene, OpenSceneMode.Single);
            Apply("RR_Bootstrap");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("RoadReady/Apply Skyboxes To Generated Scenes", priority = 33)]
        public static void ApplyToAllScenes()
        {
            foreach (var name in new[] { "RR_Tutorial", "RR_ZebraCrossing", "RR_SignalizedIntersection", "RR_MotoTaxiJunction", "RR_OpenRoad", "RR_Bootstrap" })
            {
                var path = $"{GreyboxKit.GeneratedRoot}/Scenes/{name}.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Apply(name);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        static bool s_Prepared;

        static void PrepareTextures()
        {
            if (s_Prepared)
                return;
            s_Prepared = true;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { $"{Pack}/Textures" }))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                if (importer == null || importer.wrapMode == TextureWrapMode.Clamp && importer.maxTextureSize <= 1024)
                    continue;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 1024;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }
    }
}
