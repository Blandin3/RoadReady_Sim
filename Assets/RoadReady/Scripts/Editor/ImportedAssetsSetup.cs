using System.Collections.Generic;
using System.Linq;
using RoadReady.Core;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Swaps the greybox traffic for the imported art:
    /// cars and minibuses from the Stylized Vehicles Pack, pedestrians from City People (walking with a
    /// Speed-driven idle/walk/jog blend tree, so they stand arms-down instead of in the T-pose), and the driver's
    /// hands from the XR Hands sample models. The NPC prefabs keep their AI/audio components; only the visuals change.
    /// Motorbikes and bicycles stay greybox (neither pack has them).
    /// </summary>
    public static partial class ImportedAssetsSetup
    {
        const string VehiclePack = "Assets/Stylized Vehicles Pack Free";
        const string PeoplePack = "Assets/DenysAlmaral/CityPeople";
        const string HandsSample = "Assets/Samples/XR Hands/1.8.1/HandVisualizer";
        const string AnimationsFolder = GeneratedRoot + "/Animations";
        const string BootstrapScene = GeneratedRoot + "/Scenes/RR_Bootstrap.unity";

        /// <summary>Extra car models; NPC_Car itself is the sedan.</summary>
        public static readonly string[] CarVariants = { "Car2", "Jeep2" };

        static readonly (string variant, string model, bool female)[] k_People =
        {
            ("A", "city/casual_Male_G", false),
            ("B", "city/casual_Female_G", true),
            ("C", "downtown/casual_Male_K", false),
            ("D", "downtown/casual_Female_K", true),
            ("E", "elder/elder_Female_A", true),
            ("F", "professions/Doctor_Male_B", false),
        };

        public static IEnumerable<string> PedestrianVariants =>
            PeopleAvailable ? k_People.Select(p => p.variant) : new[] { "A", "B" };

        static bool VehiclesAvailable => AssetDatabase.LoadAssetAtPath<GameObject>($"{VehiclePack}/Prefabs/Sedan1.prefab") != null;
        static bool PeopleAvailable => AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePack}/Prefabs/{k_People[0].model}.prefab") != null;

        [MenuItem("RoadReady/Use Imported Vehicles, People and Hands", priority = 32)]
        public static void RunFromMenu()
        {
            Run();
            SetUpBootstrapHands();
            SkyboxSetup.ApplyToBootstrap();
            Debug.Log("[RoadReady] Imported vehicles, people and hands applied. Run 'Rebuild Scenario Scenes Only' to use the new variants in every scenario.");
        }

        /// <summary>Prefab and material work only (called from the build pipeline before scenes are built).</summary>
        public static void Run()
        {
            if (VehiclesAvailable)
            {
                PlayerCarModelSetup.ConvertMaterialsToUrp($"{VehiclePack}/Materials");
                Vehicle($"{PrefabsFolder}/NPC_Car.prefab", "Sedan1", RoadUserKind.Car, 4.5f);
                foreach (var model in CarVariants)
                    Vehicle(CarVariantPath(model), model, RoadUserKind.Car, model == "Jeep2" ? 4.6f : 4.3f);
                Vehicle($"{PrefabsFolder}/NPC_Bus.prefab", "MicroBus4", RoadUserKind.Bus, 5.4f);
            }
            else
            {
                Debug.LogWarning("[RoadReady] Stylized Vehicles Pack not found; traffic stays greybox.");
            }

            if (PeopleAvailable)
            {
                var male = LocomotionController("Pedestrian_M", "m");
                var female = LocomotionController("Pedestrian_F", "f");
                foreach (var person in k_People)
                    Pedestrian(person.variant, person.model, person.female ? female : male);
            }
            else
            {
                Debug.LogWarning("[RoadReady] City People not found; pedestrians stay greybox.");
            }

            if (ScootersAvailable && PeopleAvailable)
                MotoTaxis();
            else
                Debug.LogWarning("[RoadReady] Electric Scooter pack or City People not found; moto-taxis stay greybox.");

            AssetDatabase.SaveAssets();
        }

        public static string CarVariantPath(string model) => $"{PrefabsFolder}/NPC_Car_{model}.prefab";

        /// <summary>Car variants that exist on disk (for the traffic spawners).</summary>
        public static IEnumerable<TrafficVehicleAI> CarVariantPrefabs() =>
            CarVariants.Select(m => AssetDatabase.LoadAssetAtPath<TrafficVehicleAI>(CarVariantPath(m))).Where(p => p != null);

        // ------------------------------------------------------------------ vehicles

        static void Vehicle(string path, string modelName, RoadUserKind kind, float targetLength)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                // Variants start as a copy of NPC_Car so they carry the same AI and engine audio.
                VehiclePrefab(kind);
                AssetDatabase.CopyAsset($"{PrefabsFolder}/NPC_{kind}.prefab", path);
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehiclePack}/Prefabs/{modelName}.prefab");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = System.IO.Path.GetFileNameWithoutExtension(path);
                RemoveVisuals(root.transform);

                var model = Object.Instantiate(source, root.transform, false);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (var c in model.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                // Real-world size, centred on the root, wheels on the ground.
                var bounds = WorldBounds(model);
                model.transform.localScale = Vector3.one * (targetLength / bounds.size.z);
                bounds = WorldBounds(model);
                model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = WorldBounds(model);

                var body = model.transform.Find(modelName);
                var width = body != null && body.TryGetComponent<Renderer>(out var bodyRenderer) ? bodyRenderer.bounds.size.x : bounds.size.x;

                var wheels = new List<Transform>();
                var radius = 0.33f;
                foreach (var name in new[] { "FL", "FR", "BL", "BR" })
                {
                    var wheel = model.transform.Find(name);
                    if (wheel == null || !wheel.TryGetComponent<Renderer>(out var tyre))
                        continue;
                    radius = tyre.bounds.extents.y;
                    wheels.Add(WheelPivot(wheel, tyre.bounds.center));
                }

                var lamps = BrakeLamps(root.transform, body, bounds, kind == RoadUserKind.Bus ? 0.36f : 0.6f, width);

                var ai = root.GetComponent<TrafficVehicleAI>();
                ai.SetShape(kind, bounds.size.z, width);
                var so = new SerializedObject(ai);
                var wheelProp = so.FindProperty("m_Wheels");
                wheelProp.arraySize = wheels.Count;
                for (var i = 0; i < wheels.Count; i++) wheelProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
                so.FindProperty("m_WheelRadius").floatValue = radius;
                var brakeProp = so.FindProperty("m_BrakeLights");
                brakeProp.arraySize = lamps.Count;
                for (var i = 0; i < lamps.Count; i++) brakeProp.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
                var focus = root.transform.Find("Focus");
                if (focus != null)
                    focus.localPosition = new Vector3(0f, bounds.size.y * 0.6f, bounds.size.z * 0.3f);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The pack's wheel pivots are not at the hub; spin a pivot placed at the tyre centre instead.</summary>
        static Transform WheelPivot(Transform wheel, Vector3 hub)
        {
            var pivot = new GameObject("Wheel " + wheel.name).transform;
            pivot.SetParent(wheel.parent, false);
            pivot.position = hub;
            pivot.localRotation = Quaternion.identity;
            wheel.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>
        /// Thin lamps laid on the rear of the body (found from the mesh), lit by TrafficVehicleAI when braking, so a
        /// braking car ahead is visible to the learner.
        /// </summary>
        static List<Renderer> BrakeLamps(Transform root, Transform body, Bounds bounds, float heightFraction, float width)
        {
            var lamps = new List<Renderer>();
            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
                return lamps;

            var vertices = filter.sharedMesh.vertices.Select(v => root.InverseTransformPoint(body.TransformPoint(v))).ToArray();
            var y = bounds.min.y + bounds.size.y * heightFraction;
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * width * 0.36f;
                var near = vertices.Where(v => Mathf.Abs(v.x - x) < 0.15f && Mathf.Abs(v.y - y) < 0.15f).ToList();
                if (near.Count == 0)
                    continue;
                var z = near.Min(v => v.z);
                var lamp = Box(root, side < 0f ? "Brake L" : "Brake R", new Vector3(x, y, z - 0.012f), new Vector3(0.24f, 0.08f, 0.02f), "lamp");
                lamps.Add(lamp.GetComponent<Renderer>());
            }

            return lamps;
        }

        // ------------------------------------------------------------------ pedestrians

        static void Pedestrian(string variant, string modelPath, RuntimeAnimatorController controller)
        {
            var path = $"{PrefabsFolder}/NPC_Pedestrian_{variant}.prefab";
            PedestrianPrefab(variant); // creates the greybox prefab (AI + focus point) if it does not exist yet
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePack}/Prefabs/{modelPath}.prefab");
            if (source == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RemoveVisuals(root.transform);
                var model = Object.Instantiate(source, root.transform, false);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;

                // The pack's demo script plays random clips (dances) and adds a collider: the AI drives the animation instead.
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null && behaviour.GetType().Name == "CityPeople")
                        Object.DestroyImmediate(behaviour);
                foreach (var c in model.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                var animator = model.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    r.updateWhenOffscreen = false;

                var ped = root.GetComponent<PedestrianAI>();
                var so = new SerializedObject(ped);
                so.FindProperty("m_Animator").objectReferenceValue = animator;
                var focus = root.transform.Find("Focus");
                if (focus != null)
                    focus.localPosition = new Vector3(0f, WorldBounds(model).size.y * 0.7f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>"Speed" (m/s) blends standing idle -> walk -> jog, matching PedestrianAI.</summary>
        static AnimatorController LocomotionController(string name, string sex)
        {
            EnsureFolder(AnimationsFolder);
            var path = $"{AnimationsFolder}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip($"idle_{sex}_1_" + (sex == "m" ? "200f" : "150f")), 0f);
            tree.AddChild(Clip($"locom_{sex}_basicWalk_30f"), 1.3f);
            tree.AddChild(Clip($"locom_{sex}_jogging_30f"), 2.6f);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimationClip Clip(string file)
        {
            var path = $"{PeoplePack}/Animations/{file}.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null)
                Debug.LogWarning($"[RoadReady] Animation clip not found in {path}");
            return clip;
        }

        // ------------------------------------------------------------------ hands

        /// <summary>Gives the bootstrap PlayerRig the XR Hands sample meshes for the driving hands.</summary>
        public static void SetUpBootstrapHands()
        {
            var left = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsSample}/Models/LeftHand.fbx");
            var right = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsSample}/Models/RightHand.fbx");
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{HandsSample}/Materials/HandsDefaultMaterial.mat");
            if (left == null || right == null)
            {
                Debug.LogWarning("[RoadReady] XR Hands sample (HandVisualizer) not imported; driving hands are disabled.");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene().path == BootstrapScene
                ? EditorSceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(BootstrapScene, OpenSceneMode.Single);
            var rig = Object.FindAnyObjectByType<PlayerRig>();
            if (rig == null)
                return;
            ApplyHands(rig);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Used when the bootstrap scene is generated.</summary>
        public static void ApplyHands(PlayerRig rig)
        {
            var left = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsSample}/Models/LeftHand.fbx");
            var right = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsSample}/Models/RightHand.fbx");
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{HandsSample}/Materials/HandsDefaultMaterial.mat");
            rig.SetHandModels(left, right, material);
            EditorUtility.SetDirty(rig);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Removes every visual child (greybox or a previous model); keeps the focus point and audio children.</summary>
        static void RemoveVisuals(Transform root)
        {
            foreach (var child in root.Cast<Transform>().ToList())
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
                    Object.DestroyImmediate(child.gameObject);
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
