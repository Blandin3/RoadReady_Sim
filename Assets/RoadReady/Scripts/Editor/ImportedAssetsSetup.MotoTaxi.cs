using System.Collections.Generic;
using System.Linq;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Two-wheelers (the moto-taxi road-user kind) built from the Electric Scooter pack: stand-up e-scooters at real
    /// size, each ridden by a City People person standing on the deck with hands on the bars and a helmet on. The
    /// posed riders are baked to static meshes, so they cost no animation at runtime.
    /// </summary>
    public static partial class ImportedAssetsSetup
    {
        const string ScooterPack = "Assets/MARCIN'S Assets/Electric Scooter";
        const string RiderMeshFolder = GeneratedRoot + "/Meshes/Riders";
        const float k_MotoLength = 1.15f; // a typical stand-up e-scooter

        // (prefab variant, scooter model, rider, rider is female)
        static readonly (string variant, string scooter, string rider, bool female)[] k_Motos =
        {
            ("", "Scooter_1", "city/casual_Male_G", false),
            ("Scooter_3", "Scooter_3", "downtown/casual_Female_K", true),
            ("Scooter_6", "Scooter_6", "worker_Male_constructor_B", false),
            ("Scooter_8", "Scooter_8", "city/casual_Female_G", true),
        };

        static bool ScootersAvailable => AssetDatabase.LoadAssetAtPath<GameObject>($"{ScooterPack}/Prefabs/Scooter_1.prefab") != null;

        public static string MotoVariantPath(string variant) =>
            string.IsNullOrEmpty(variant) ? $"{PrefabsFolder}/NPC_MotoTaxi.prefab" : $"{PrefabsFolder}/NPC_MotoTaxi_{variant}.prefab";

        /// <summary>Extra moto-taxi variants that exist on disk (for the traffic spawners).</summary>
        public static IEnumerable<TrafficVehicleAI> MotoVariantPrefabs() =>
            k_Motos.Where(m => m.variant != "").Select(m => AssetDatabase.LoadAssetAtPath<TrafficVehicleAI>(MotoVariantPath(m.variant))).Where(p => p != null);

        static void MotoTaxis()
        {
            PlayerCarModelSetup.ConvertMaterialsToUrp(ScooterPack);
            AssetDatabase.DeleteAsset(RiderMeshFolder); // riders are re-baked below
            foreach (var moto in k_Motos)
                MotoTaxi(moto.variant, moto.scooter, moto.rider, moto.female);
        }

        static void MotoTaxi(string variant, string scooterName, string riderModel, bool female)
        {
            var path = MotoVariantPath(variant);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                VehiclePrefab(RoadUserKind.MotoTaxi);
                AssetDatabase.CopyAsset(MotoVariantPath(""), path);
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = System.IO.Path.GetFileNameWithoutExtension(path);
                RemoveVisuals(root.transform);

                // Scooter: real length, centred, wheels on the ground. Its demo script reads the old Input Manager
                // (which throws with the Input System) - remove it.
                var scooter = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{ScooterPack}/Prefabs/{scooterName}.prefab"), root.transform, false);
                scooter.name = "Scooter";
                foreach (var behaviour in scooter.GetComponentsInChildren<MonoBehaviour>(true))
                    Object.DestroyImmediate(behaviour);
                foreach (var c in scooter.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                scooter.transform.localPosition = Vector3.zero;
                scooter.transform.localRotation = Quaternion.identity;
                scooter.transform.localScale = Vector3.one;
                var bounds = WorldBounds(scooter);
                if (bounds.size.x > bounds.size.z)
                {
                    scooter.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    bounds = WorldBounds(scooter);
                }

                scooter.transform.localScale *= k_MotoLength / bounds.size.z;
                bounds = WorldBounds(scooter);
                scooter.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = WorldBounds(scooter);

                // Face +Z: the handlebar is at the front.
                var bar = Find(scooter.transform, "handlebar");
                if (bar != null && bar.GetComponent<Renderer>().bounds.center.z < 0f)
                {
                    scooter.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                    bounds = WorldBounds(scooter);
                }

                var wheels = new List<Transform>();
                var radius = 0.3f;
                foreach (var name in new[] { "front_wheel", "back_wheel" })
                {
                    var wheel = Find(scooter.transform, name);
                    if (wheel == null || !wheel.TryGetComponent<Renderer>(out var tyre))
                        continue;
                    radius = tyre.bounds.extents.y;
                    wheels.Add(WheelPivot(wheel, tyre.bounds.center));
                }

                // Deck: the highest point of the body over the rear half; the grips: the handlebar ends.
                var deck = SeatPoint(scooter, bounds);
                var grips = bar != null ? GripPoints(bar) : (new Vector3(-0.25f, 1.1f, 0.45f), new Vector3(0.25f, 1.1f, 0.45f));
                var riders = Group(root.transform, "Riders");
                Rider(riders, "Rider", riderModel, female, deck, grips);

                var lamp = Box(root.transform, "Brake light", new Vector3(0f, deck.y + 0.05f, bounds.min.z - 0.01f), new Vector3(0.16f, 0.06f, 0.03f), "lamp");

                var ai = root.GetComponent<TrafficVehicleAI>();
                ai.SetShape(RoadUserKind.MotoTaxi, bounds.size.z, Mathf.Max(0.7f, bounds.size.x));
                var so = new SerializedObject(ai);
                var wheelProp = so.FindProperty("m_Wheels");
                wheelProp.arraySize = wheels.Count;
                for (var i = 0; i < wheels.Count; i++) wheelProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
                so.FindProperty("m_WheelRadius").floatValue = radius;
                var brakeProp = so.FindProperty("m_BrakeLights");
                brakeProp.arraySize = 1;
                brakeProp.GetArrayElementAtIndex(0).objectReferenceValue = lamp.GetComponent<Renderer>();
                var focus = root.transform.Find("Focus");
                if (focus != null)
                    focus.localPosition = new Vector3(0f, 1.2f, 0.2f);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[RoadReady] {root.name}: {scooterName} {bounds.size:F2}, deck {deck:F2}, grips {grips.Item1:F2}/{grips.Item2:F2}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform Find(Transform root, string prefix) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(prefix) && t.GetComponent<Renderer>() != null);

        static Vector3 SeatPoint(GameObject scooter, Bounds bounds)
        {
            var best = new Vector3(0f, bounds.min.y + bounds.size.y * 0.45f, bounds.center.z - bounds.size.z * 0.15f);
            var body = Find(scooter.transform, "main");
            if (body == null || !body.TryGetComponent<MeshFilter>(out var filter))
                return best;
            var points = filter.sharedMesh.vertices.Select(v => body.TransformPoint(v))
                .Where(p => p.z < bounds.center.z + bounds.size.z * 0.25f && p.z > bounds.min.z + 0.08f && Mathf.Abs(p.x) < 0.1f && p.y < bounds.min.y + 0.22f) // the deck: low, clear of the stem top and the rear mudguard
                .ToList();
            if (points.Count == 0)
                return best;
            var top = points.Max(p => p.y);
            var seatPoints = points.Where(p => p.y > top - 0.03f).ToList();
            return new Vector3(0f, top, seatPoints.Average(p => p.z));
        }

        static (Vector3 left, Vector3 right) GripPoints(Transform bar)
        {
            var b = bar.GetComponent<Renderer>().bounds;
            return (new Vector3(b.min.x + 0.06f, b.max.y - 0.03f, b.center.z), new Vector3(b.max.x - 0.06f, b.max.y - 0.03f, b.center.z));
        }

        // ------------------------------------------------------------------ riders

        /// <summary>
        /// A City People person standing on the deck: relaxed upright stance, left foot forward, knees soft, hands on
        /// the grips and a helmet on. Posed with the humanoid muscles, then baked to a static mesh.
        /// </summary>
        static void Rider(Transform parent, string name, string modelPath, bool female, Vector3 deck, (Vector3 left, Vector3 right) grips)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePack}/Prefabs/{modelPath}.prefab");
            if (source == null)
                return;
            var person = Object.Instantiate(source, parent, false);
            person.transform.localPosition = Vector3.zero;
            person.transform.localRotation = Quaternion.identity;
            foreach (var behaviour in person.GetComponentsInChildren<MonoBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            var animator = person.GetComponentInChildren<Animator>();

            // Start from the model's bind pose (straight legs, T-pose arms); lower the arms, then reach for the bars.
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            foreach (var (muscle, value) in RidingPose())
            {
                var index = System.Array.IndexOf(HumanTrait.MuscleName, muscle);
                if (index >= 0) pose.muscles[index] = value;
            }

            handler.SetHumanPose(ref pose);
            handler.Dispose();
            person.transform.localPosition = Vector3.zero;
            person.transform.localRotation = Quaternion.identity;

            // Stand on the deck: lowest foot just above it, feet in the middle of the deck.
            var feet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes }
                .Select(animator.GetBoneTransform).Where(t => t != null).ToList();
            var hipBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            var footY = feet.Min(t => t.position.y);
            // Riders stand towards the front of the deck, within reach of the bars.
            var target = parent.TransformPoint(new Vector3(0f, deck.y + 0.02f, grips.left.z - 0.48f));
            var footZ = feet.Average(t => t.position.z);
            person.transform.position += new Vector3(target.x - hipBone.position.x, target.y - footY, target.z - footZ);

            Reach(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, parent.TransformPoint(grips.left));
            Reach(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, parent.TransformPoint(grips.right));

            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var headPosition = head != null ? head.position : person.transform.position + Vector3.up * 1.6f;

            Bake(person, parent, name, modelPath);
            Object.DestroyImmediate(person);

            Prim(PrimitiveType.Sphere, parent, name + " helmet", parent.InverseTransformPoint(headPosition) + new Vector3(0f, 0.1f, 0f),
                new Vector3(0.26f, 0.24f, 0.29f), "helmet");
        }

        static IEnumerable<(string, float)> RidingPose()
        {
            yield return ("Left Arm Down-Up", -0.4f);
            yield return ("Right Arm Down-Up", -0.4f);
            yield return ("Left Forearm Stretch", 0.7f);
            yield return ("Right Forearm Stretch", 0.7f);
            yield return ("Spine Front-Back", 0.08f);
        }

        /// <summary>Two-bone reach: swing the upper arm so the hand lands on (or as near as possible to) the target.</summary>
        static void Reach(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, Vector3 target)
        {
            var a = animator.GetBoneTransform(upper);
            var b = animator.GetBoneTransform(lower);
            var c = animator.GetBoneTransform(hand);
            if (a == null || b == null || c == null)
                return;
            for (var i = 0; i < 4; i++)
            {
                a.rotation = Quaternion.FromToRotation(c.position - a.position, target - a.position) * a.rotation;
                b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
            }
        }

        static void Bake(GameObject person, Transform parent, string name, string modelPath)
        {
            EnsureFolder(GeneratedRoot + "/Meshes");
            EnsureFolder(RiderMeshFolder);
            var holder = Group(parent, name);
            foreach (var smr in person.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var assetPath = $"{RiderMeshFolder}/{parent.parent.name}_{name}_{smr.name}.asset";
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(mesh, assetPath);
                var part = new GameObject(smr.name);
                part.transform.SetParent(holder, false);
                part.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = smr.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
