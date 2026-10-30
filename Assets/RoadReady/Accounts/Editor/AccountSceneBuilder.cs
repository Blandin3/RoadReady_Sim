using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadReady.Accounts.Editor
{
    /// <summary>
    /// RoadReady > Accounts: builds the account scenes into Assets/RoadReady/Accounts/Scenes, and adds them to
    /// Build Settings. Each scene is almost empty (a camera or XR rig plus one controller script); the screen itself
    /// is drawn by the controller when the scene starts. Nothing outside the Accounts folder is changed, except that
    /// "Add Account Scenes To Build Settings" appends to the scene list.
    /// </summary>
    public static class AccountSceneBuilder
    {
        const string k_SceneFolder = "Assets/RoadReady/Accounts/Scenes";
        const string k_XrRig = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        // Scene name -> the controller that draws it. More are added as the stages are built.
        static readonly (string scene, System.Type controller)[] k_Scenes =
        {
            (SceneRouter.SchoolSetup, typeof(SchoolSetupController)),
            (SceneRouter.Home, typeof(HomeController)),
            (SceneRouter.MemberRegister, typeof(MemberRegisterController)),
            (SceneRouter.StartTraining, typeof(StartTrainingController)),
            (SceneRouter.Dashboard, typeof(DashboardController)),
        };

        [MenuItem("RoadReady/Accounts/Build Account Scenes")]
        public static void BuildScenes()
        {
            if (!AssetDatabase.IsValidFolder(k_SceneFolder))
                AssetDatabase.CreateFolder("Assets/RoadReady/Accounts", "Scenes");

            foreach (var (name, controller) in k_Scenes)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AddViewer();
                new GameObject(name + " Controller").AddComponent(controller);
                EditorSceneManager.SaveScene(scene, $"{k_SceneFolder}/{name}.unity");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RoadReady Accounts] Built {k_Scenes.Length} scene(s) in {k_SceneFolder}. Now run RoadReady > Accounts > Add Account Scenes To Build Settings.");
        }

        [MenuItem("RoadReady/Accounts/Add Account Scenes To Build Settings")]
        public static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var added = 0;
            foreach (var (name, _) in k_Scenes)
            {
                var path = $"{k_SceneFolder}/{name}.unity";
                if (!System.IO.File.Exists(path) || scenes.Any(s => s.path == path))
                    continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
                added++;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[RoadReady Accounts] Added {added} scene(s) to Build Settings (existing scenes left as they were). " +
                      "Drag SchoolSetup to the top of the list if the app should start there.");
        }

        /// <summary>
        /// The game's own setup makes Play always start from RR_Bootstrap, so pressing Play in SchoolSetup opens the game
        /// instead. This makes Play start from the account scene for testing. The game's setup puts the redirect back
        /// after a script recompile or an editor restart; use the next menu item to put it back by hand.
        /// </summary>
        [MenuItem("RoadReady/Accounts/Test Accounts (Play From SchoolSetup)")]
        public static void PlayFromSchoolSetup()
        {
            var path = $"{k_SceneFolder}/{SceneRouter.SchoolSetup}.unity";
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (scene == null)
            {
                Debug.LogWarning("[RoadReady Accounts] Run RoadReady > Accounts > Build Account Scenes first.");
                return;
            }

            EditorSceneManager.playModeStartScene = scene;
            Debug.Log("[RoadReady Accounts] Play now starts from SchoolSetup.");
        }

        [MenuItem("RoadReady/Accounts/Restore Game Start (Play From RR_Bootstrap)")]
        public static void PlayFromGame()
        {
            var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/RoadReady/Generated/Scenes/RR_Bootstrap.unity");
            EditorSceneManager.playModeStartScene = bootstrap;
            Debug.Log("[RoadReady Accounts] Play now starts from RR_Bootstrap, as before.");
        }

        /// <summary>The XR rig from the XR Interaction Toolkit starter assets (controller rays); a plain camera when it is not there.</summary>
        static void AddViewer()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(k_XrRig);
            if (rig != null)
            {
                PrefabUtility.InstantiatePrefab(rig);
                return;
            }

            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 1.4f, 0f);
            var cam = camera.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiKit.Background;
        }
    }
}
