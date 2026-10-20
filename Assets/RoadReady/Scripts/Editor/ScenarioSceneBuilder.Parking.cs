using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Hazards;
using RoadReady.Scenarios;
using RoadReady.Traffic;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Reverse bay parking in the imported car park (Assets/Parking.FBX, 36 x 17 m: two rows of bays angled 17.7
    /// degrees either side of a 7.5 m aisle). The learner starts in the aisle at the right-hand end facing left,
    /// drives past the target bay in the middle of the north row (parked cars either side) and reverses into it. A
    /// pedestrian walking along the bay fronts is the hazard; the wall at the back of the bays is the "kerb".
    /// </summary>
    public static partial class ScenarioSceneBuilder
    {
        const string k_ParkingModel = "Assets/Parking.FBX";

        // North row of bays, measured from the model's painted lines: line i crosses z = 3.75 at
        // x = k_LineX0 + i * k_LinePitch and leans k_LineSlope metres in x per metre of z. Lines run z 3.6 -> 8.0.
        const float k_LineX0 = -16.94f, k_LinePitch = 2.3475f, k_LineSlope = 0.3196f, k_LineZRef = 3.75f;
        const float k_BayZMin = 3.6f, k_BayZMax = 8.0f, k_LineWidth = 0.12f;
        const int k_TargetLine = 7; // the bay between lines 7 and 8, in the middle of the row
        const float k_NorthWallZ = 8.55f;

        static float LineX(int line, float z) => k_LineX0 + line * k_LinePitch + k_LineSlope * (z - k_LineZRef);

        public static void BuildParking(string defPath)
        {
            const string name = "RR_Parking";
            var rig = NewScene(name, defPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_ParkingModel) == null)
            {
                Debug.LogError($"[RoadReady] {k_ParkingModel} not found - the parking scenario needs the imported car park.");
                Save(rig, name);
                return;
            }

            // The car park, with mesh colliders so the car drives on the floor and stops at walls, fences and posts.
            var lot = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(k_ParkingModel), rig.env, false);
            lot.name = "Car park";
            foreach (var filter in lot.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            var floorY = HighwayLoopKit.SurfaceY(new Vector3(0f, 0f, 0f));

            // Target bay: centred between its two lines, its +Z pointing into the bay (towards the back wall).
            var axis = new Vector3(k_LineSlope, 0f, 1f).normalized;
            var bayRotation = Quaternion.LookRotation(axis);
            var midZ = (k_BayZMin + k_BayZMax) * 0.5f;
            var bayCentre = new Vector3((LineX(k_TargetLine, midZ) + LineX(k_TargetLine + 1, midZ)) * 0.5f, floorY, midZ);
            var bayWidth = k_LinePitch * axis.z; // perpendicular distance between two lines
            var bayLength = (k_BayZMax - k_BayZMin) / axis.z;
            var area = Group(rig.root, "Parking area");
            var bay = Point(area, "Target bay", bayCentre);
            bay.rotation = bayRotation;

            var lines = new List<Transform>();
            foreach (var line in new[] { k_TargetLine, k_TargetLine + 1 })
            {
                var t = Point(area, $"Bay line {line}", new Vector3(LineX(line, midZ), floorY, midZ));
                t.rotation = bayRotation;
                t.localScale = new Vector3(k_LineWidth, 1f, bayLength);
                lines.Add(t);
            }

            // The back wall of the bays is the "kerb": bumping it is a fault, not a crash.
            var kerb = Box(area, "Back wall (kerb)", new Vector3(0f, floorY + 0.6f, k_NorthWallZ - 0.1f), new Vector3(35.5f, 1.2f, 0.2f), "kerb", collider: true);
            kerb.GetComponent<MeshRenderer>().enabled = false;
            kerb.AddComponent<ParkingKerb>();

            // Parked cars in the bays either side (one reversed in, one driven in forwards).
            ParkedCar(rig, bayCentre + new Vector3(-k_LinePitch, 0f, 0f), Quaternion.LookRotation(-axis), "NPC_Car_Car2");
            ParkedCar(rig, bayCentre + new Vector3(k_LinePitch, 0f, 0f), bayRotation, "NPC_Car_Jeep2");

            // Learner: in the aisle at the right-hand end, facing left, already past the bay's far side.
            PlayerCar(rig, new Vector3(12.5f, floorY, 0.6f), 270f);

            // Hazard: a pedestrian walking along the bay fronts towards the car once it starts reversing.
            // Starts along the bay fronts ahead-left of the car (clear of it) and walks across the back of the bay.
            var walk = Path(rig.paths, "Pedestrian behind the car", new Vector3(7f, floorY, 3.0f), new Vector3(bayCentre.x - 1f, floorY, 3.0f), new Vector3(-9f, floorY, 3.0f));
            var pedestrian = ScriptedPedestrian(rig, "Pedestrian - walks behind the car", walk, Perspective.Driver, "C");
            var hazard = Hazard(rig, HZ("parking_pedestrian_behind"), HazardTriggerMode.Manual, new Vector3(bayCentre.x, floorY, 3.0f));
            hazard.FocusPointOverride = pedestrian.transform;
            Act(hazard, pedestrian, HazardActorMode.Activate);

            var parking = area.gameObject.AddComponent<ParkingArea>();
            parking.Configure(bay, new Vector2(bayWidth, bayLength), lines, new List<Transform>(), hazard);
            rig.ctx.Parking = parking;

            // A "parking" sign at the entrance and a slow-down sign facing the aisle.
            RoadSigns.Place(rig.env, "Parking lot", new Vector3(-16.6f, floorY, -3.6f), Vector3.right);
            RoadSigns.Place(rig.env, "Slow", new Vector3(16.9f, floorY, -3.4f), Vector3.left);

            Save(rig, name);
        }

        /// <summary>A parked car: the traffic prefab's model only (no AI), with a solid collider.</summary>
        static void ParkedCar(Rig rig, Vector3 position, Quaternion rotation, string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/{prefabName}.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/NPC_Car.prefab");
            var car = Object.Instantiate(prefab, rig.env);
            car.name = "Parked car";
            car.transform.SetPositionAndRotation(position, rotation);
            foreach (var behaviour in car.GetComponentsInChildren<MonoBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            foreach (var source in car.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(source);
            var bounds = new Bounds(position, Vector3.zero);
            foreach (var r in car.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            var box = car.AddComponent<BoxCollider>();
            box.center = car.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(1.8f, bounds.size.y, 4.3f);
        }
    }
}
