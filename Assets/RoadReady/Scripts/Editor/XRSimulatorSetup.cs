using RoadReady.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Puts XRI's XR Interaction Simulator in the bootstrap scene so the app can be tried in the editor without a
    /// headset (keyboard + mouse drive the simulated headset and controllers). Tagged EditorOnly so builds strip it,
    /// and guarded so it switches itself off when a real headset is connected.
    /// </summary>
    public static class XRSimulatorSetup
    {
        const string PrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab";
        const string BootstrapScene = GreyboxKit.GeneratedRoot + "/Scenes/RR_Bootstrap.unity";
        const string ObjectName = "XR Interaction Simulator (editor only)";

        [MenuItem("RoadReady/Add XR Interaction Simulator To Bootstrap", priority = 34)]
        public static void AddToBootstrap()
        {
            var scene = EditorSceneManager.GetActiveScene().path == BootstrapScene
                ? EditorSceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(BootstrapScene, OpenSceneMode.Single);
            if (AddToOpenScene())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        /// <summary>Adds the simulator to the open scene (used when the bootstrap scene is generated). Returns false if unavailable.</summary>
        public static bool AddToOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[RoadReady] XR Interaction Simulator sample not found. Import it from Package Manager > XR Interaction Toolkit > Samples.");
                return false;
            }

            var existing = GameObject.Find(ObjectName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var simulator = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            simulator.name = ObjectName;
            simulator.tag = "EditorOnly"; // never part of a build
            simulator.AddComponent<SimulatorHeadsetGuard>();
            return true;
        }
    }
}
