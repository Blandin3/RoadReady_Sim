using System.Collections.Generic;
using System.IO;
using RoadReady.Core;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEditor;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Primitive-based placeholder art so every scenario is playable before the final assets arrive.
    /// Every builder only creates geometry + RoadReady components; swap meshes later without touching logic.
    /// </summary>
    public static class GreyboxKit
    {
        public const string Root = "Assets/RoadReady";
        public const string GeneratedRoot = Root + "/Generated";
        public const string MaterialsFolder = GeneratedRoot + "/Materials";
        public const string PrefabsFolder = GeneratedRoot + "/Prefabs";
        public const string ConfigFolder = GeneratedRoot + "/Config";
        public const string HazardFolder = ConfigFolder + "/Hazards";
        public const string ScenarioFolder = ConfigFolder + "/Scenarios";
        public const string ConfigPath = ConfigFolder + "/RoadReadyConfig.asset";

        static readonly string[] k_MaterialNames =
        {
            "asphalt", "sidewalk", "marking", "ground", "grass", "pole", "housing", "lamp", "player_car", "interior", "car", "car_red",
            "moto", "helmet", "rider", "bus", "bicycle", "ped_a", "ped_b", "skin", "building_a", "building_b", "building_c", "building_d",
            "roof", "tree", "trunk", "marker", "lobby", "rw_blue", "rw_yellow", "rw_green",
        };

        /// <summary>
        /// Creates every shared material and prefab up front so no asset is created (and imported) while scenes
        /// are being built.
        /// </summary>
        public static void PrewarmSharedAssets()
        {
            s_Materials.Clear();
            foreach (var name in k_MaterialNames)
                Mat(name);
            foreach (var kind in new[] { RoadUserKind.Car, RoadUserKind.MotoTaxi, RoadUserKind.Bus, RoadUserKind.Bicycle })
                VehiclePrefab(kind);
            PedestrianPrefab("A");
            PedestrianPrefab("B");
            PlayerCarPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public const float RoadTop = 0f;
        public const float SidewalkTop = 0.12f;

        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ assets / folders

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            EnsureFolder(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void SetRef(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"[RoadReady] Property {property} not found on {target.GetType().Name}");
                return;
            }

            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ materials

        public static Material Mat(string name)
        {
            if (s_Materials.TryGetValue(name, out var cached) && cached != null)
                return cached;
            EnsureFolder(MaterialsFolder);
            var path = $"{MaterialsFolder}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                ApplyColor(mat, name);
                AssetDatabase.CreateAsset(mat, path);
            }

            s_Materials[name] = mat;
            return mat;
        }

        static void ApplyColor(Material mat, string name)
        {
            var color = name switch
            {
                "asphalt" => new Color(0.17f, 0.17f, 0.19f),
                "sidewalk" => new Color(0.62f, 0.6f, 0.56f),
                "marking" => new Color(0.95f, 0.95f, 0.92f),
                "ground" => new Color(0.52f, 0.32f, 0.22f), // Kigali red soil
                "grass" => new Color(0.33f, 0.5f, 0.25f),
                "pole" => new Color(0.25f, 0.25f, 0.27f),
                "housing" => new Color(0.08f, 0.08f, 0.08f),
                "lamp" => new Color(0.08f, 0.08f, 0.08f),
                "player_car" => new Color(0.08f, 0.32f, 0.68f),
                "interior" => new Color(0.12f, 0.12f, 0.13f),
                "car" => new Color(0.85f, 0.85f, 0.85f),
                "car_red" => new Color(0.7f, 0.12f, 0.12f),
                "moto" => new Color(0.1f, 0.1f, 0.1f),
                "helmet" => new Color(0.1f, 0.55f, 0.25f),
                "rider" => new Color(0.1f, 0.4f, 0.2f),
                "bus" => new Color(0.95f, 0.75f, 0.1f),
                "bicycle" => new Color(0.2f, 0.4f, 0.8f),
                "ped_a" => new Color(0.95f, 0.5f, 0.1f),
                "ped_b" => new Color(0.3f, 0.3f, 0.75f),
                "skin" => new Color(0.36f, 0.24f, 0.16f),
                "building_a" => new Color(0.93f, 0.89f, 0.8f),
                "building_b" => new Color(0.78f, 0.45f, 0.32f),
                "building_c" => new Color(0.62f, 0.75f, 0.82f),
                "building_d" => new Color(0.95f, 0.95f, 0.95f),
                "roof" => new Color(0.45f, 0.2f, 0.15f),
                "tree" => new Color(0.18f, 0.42f, 0.18f),
                "trunk" => new Color(0.35f, 0.22f, 0.12f),
                "marker" => new Color(1f, 0.82f, 0.05f),
                "lobby" => new Color(0.22f, 0.26f, 0.3f),
                "rw_blue" => new Color(0f, 0.63f, 0.87f),
                "rw_yellow" => new Color(0.98f, 0.82f, 0f),
                "rw_green" => new Color(0.13f, 0.38f, 0.24f),
                _ => Color.magenta,
            };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (name == "lamp")
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", name == "asphalt" ? 0.15f : 0.3f);
        }

        // ------------------------------------------------------------------ primitives

        public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, string material, bool collider = false, Quaternion? rotation = null, bool local = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            if (local)
            {
                go.transform.localPosition = position;
                go.transform.localRotation = rotation ?? Quaternion.identity;
            }
            else
            {
                go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            }

            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(material);
            return go;
        }

        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, string material, bool collider = false, Quaternion? rotation = null) =>
            Prim(PrimitiveType.Cube, parent, name, position, scale, material, collider, rotation);

        public static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Transform Point(Transform parent, string name, Vector3 position, float yaw = 0f)
        {
            var t = Group(parent, name);
            t.position = position;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        /// <summary>Yaw (degrees) for a direction on the ground plane.</summary>
        public static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ road network

        /// <summary>Axis-aligned rectangle of road (xmin..xmax, zmin..zmax).</summary>
        public static void RoadRect(Transform parent, string name, float xmin, float xmax, float zmin, float zmax)
        {
            Box(parent, name, new Vector3((xmin + xmax) * 0.5f, RoadTop - 0.05f, (zmin + zmax) * 0.5f), new Vector3(xmax - xmin, 0.1f, zmax - zmin), "asphalt", collider: true);
        }

        public static void SidewalkRect(Transform parent, Transform zones, string name, float xmin, float xmax, float zmin, float zmax)
        {
            Box(parent, name, new Vector3((xmin + xmax) * 0.5f, SidewalkTop - 0.11f, (zmin + zmax) * 0.5f), new Vector3(xmax - xmin, 0.22f, zmax - zmin), "sidewalk", collider: true);
            Zone(zones, name + " Zone", RoadZoneType.Sidewalk, xmin, xmax, zmin, zmax);
        }

        public static RoadZone Zone(Transform parent, string name, RoadZoneType type, float xmin, float xmax, float zmin, float zmax)
        {
            var t = Point(parent, name, new Vector3((xmin + xmax) * 0.5f, 0f, (zmin + zmax) * 0.5f));
            var zone = t.gameObject.AddComponent<RoadZone>();
            zone.Configure(type, new Vector3(xmax - xmin, 3f, zmax - zmin));
            return zone;
        }

        /// <summary>Zebra crossing across an E-W road (stripes run along x).</summary>
        public static RoadZone ZebraAcrossEW(Transform env, Transform zones, string name, float x, float roadHalfWidth, float width, RoadZoneType type = RoadZoneType.ZebraCrossing)
        {
            var group = Group(env, name + " Markings");
            for (var z = -roadHalfWidth + 0.5f; z <= roadHalfWidth - 0.4f; z += 1f)
                Box(group, "Stripe", new Vector3(x, 0.005f, z), new Vector3(width, 0.01f, 0.5f), "marking");
            var zone = Zone(zones, name, type, x - width * 0.5f, x + width * 0.5f, -roadHalfWidth, roadHalfWidth);
            zone.KerbA = Point(zone.transform, "Kerb A (south)", new Vector3(x, 0f, -roadHalfWidth - 0.3f));
            zone.KerbB = Point(zone.transform, "Kerb B (north)", new Vector3(x, 0f, roadHalfWidth + 0.3f));
            return zone;
        }

        public static void CentreLineX(Transform env, float xmin, float xmax, float z = 0f, float skipMin = float.NaN, float skipMax = float.NaN)
        {
            var group = Group(env, "Centre line");
            for (var x = xmin + 1.5f; x < xmax; x += 6f)
            {
                if (!float.IsNaN(skipMin) && x > skipMin && x < skipMax) continue;
                Box(group, "Dash", new Vector3(x, 0.005f, z), new Vector3(3f, 0.01f, 0.12f), "marking");
            }
        }

        public static void CentreLineZ(Transform env, float zmin, float zmax, float x = 0f)
        {
            var group = Group(env, "Centre line NS");
            for (var z = zmin + 1.5f; z < zmax; z += 6f)
                Box(group, "Dash", new Vector3(x, 0.005f, z), new Vector3(0.12f, 0.01f, 3f), "marking");
        }

        public static StopLine StopLine(Transform rules, Transform env, string name, Vector3 position, Vector3 forward, StopLineType type, float width = 3.5f,
            TrafficSignalController signal = null, string group = null, RoadZone crossing = null, Transform conflict = null)
        {
            var t = Point(rules, name, position, Yaw(forward));
            var line = t.gameObject.AddComponent<Traffic.StopLine>();
            line.Configure(type, width, signal, group, crossing, conflict);
            var right = new Vector3(forward.z, 0f, -forward.x);
            var markScale = Mathf.Abs(right.x) > 0.5f ? new Vector3(width, 0.01f, 0.35f) : new Vector3(0.35f, 0.01f, width);
            Box(env, name + " Marking", position + Vector3.up * 0.006f, markScale, type == StopLineType.GiveWay ? "marker" : "marking");
            return line;
        }

        public static WaypointPath Path(Transform parent, string name, params Vector3[] points)
        {
            var t = Group(parent, name);
            var path = t.gameObject.AddComponent<WaypointPath>();
            for (var i = 0; i < points.Length; i++)
                Point(t, "P" + i, points[i]);
            path.MarkDirty();
            return path;
        }

        public static SignalHead SignalHead(Transform parent, string name, Vector3 position, Vector3 facing, TrafficSignalController controller, string group, bool pedestrian)
        {
            var root = Point(parent, name, position, Yaw(facing));
            Prim(PrimitiveType.Cylinder, root, "Pole", new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), "pole", collider: true);
            var housingHeight = pedestrian ? 0.7f : 1.0f;
            Box(root, "Housing", new Vector3(0f, 3.1f, 0f), new Vector3(0.4f, housingHeight, 0.25f), "housing");
            Renderer Lamp(string n, float y) => Prim(PrimitiveType.Sphere, root, n, new Vector3(0f, y, 0.14f), Vector3.one * 0.22f, "lamp").GetComponent<Renderer>();
            var head = root.gameObject.AddComponent<Traffic.SignalHead>();
            if (pedestrian)
                head.Configure(controller, group, true, Lamp("Red (don't walk)", 3.25f), null, Lamp("Green (walk)", 2.95f));
            else
                head.Configure(controller, group, false, Lamp("Red", 3.4f), Lamp("Amber", 3.1f), Lamp("Green", 2.8f));
            return head;
        }

        public static void Buildings(Transform parent, float xmin, float xmax, float zFront, int side, int seed, params Vector2[] gaps)
        {
            var rng = new System.Random(seed);
            var group = Group(parent, side > 0 ? "Buildings North" : "Buildings South");
            var palette = new[] { "building_a", "building_b", "building_c", "building_d" };
            var x = xmin;
            while (x < xmax)
            {
                var width = 7f + (float)rng.NextDouble() * 8f;
                var inGap = false;
                foreach (var g in gaps)
                    if (x + width > g.x && x < g.y) { inGap = true; x = g.y + 1f; break; }
                if (inGap) continue;
                var height = 3.5f + (float)rng.NextDouble() * 9f;
                var depth = 8f + (float)rng.NextDouble() * 4f;
                var z = zFront + side * (depth * 0.5f + (float)rng.NextDouble() * 1.5f);
                Box(group, "Building", new Vector3(x + width * 0.5f, height * 0.5f, z), new Vector3(width - 0.6f, height, depth), palette[rng.Next(palette.Length)], collider: true);
                Box(group, "Roof", new Vector3(x + width * 0.5f, height + 0.15f, z), new Vector3(width - 0.3f, 0.3f, depth + 0.3f), "roof");
                x += width;
            }
        }

        public static void BuildingsZ(Transform parent, float zmin, float zmax, float xFront, int side, int seed)
        {
            var rng = new System.Random(seed);
            var group = Group(parent, side > 0 ? "Buildings East" : "Buildings West");
            var palette = new[] { "building_a", "building_b", "building_c", "building_d" };
            for (var z = zmin; z < zmax;)
            {
                var length = 7f + (float)rng.NextDouble() * 8f;
                var height = 3.5f + (float)rng.NextDouble() * 8f;
                var depth = 8f + (float)rng.NextDouble() * 4f;
                var x = xFront + side * (depth * 0.5f + 1f);
                Box(group, "Building", new Vector3(x, height * 0.5f, z + length * 0.5f), new Vector3(depth, height, length - 0.6f), palette[rng.Next(palette.Length)], collider: true);
                z += length;
            }
        }

        public static void Tree(Transform parent, Vector3 position)
        {
            var t = Group(parent, "Tree");
            t.position = position;
            Prim(PrimitiveType.Cylinder, t, "Trunk", new Vector3(0f, 1.2f, 0f), new Vector3(0.3f, 1.2f, 0.3f), "trunk", collider: true);
            Prim(PrimitiveType.Sphere, t, "Canopy", new Vector3(0f, 3.2f, 0f), new Vector3(2.8f, 2.4f, 2.8f), "tree");
        }

        public static void Ground(Transform parent, float size = 500f)
        {
            Box(parent, "Ground", new Vector3(0f, -0.15f, 0f), new Vector3(size, 0.1f, size), "ground", collider: true);
        }

        // ------------------------------------------------------------------ prefabs

        public static TrafficVehicleAI VehiclePrefab(RoadUserKind kind)
        {
            EnsureFolder(PrefabsFolder);
            var path = $"{PrefabsFolder}/NPC_{kind}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<TrafficVehicleAI>(path);
            if (existing != null)
                return existing;

            var root = new GameObject("NPC_" + kind);
            var ai = root.AddComponent<TrafficVehicleAI>();
            var brake = new List<Renderer>();
            var wheels = new List<Transform>();
            float length, width;
            switch (kind)
            {
                case RoadUserKind.MotoTaxi:
                    length = 2f; width = 0.8f;
                    Box(root.transform, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.45f, 0.5f, 1.8f), "moto");
                    Prim(PrimitiveType.Capsule, root.transform, "Rider", new Vector3(0f, 1.25f, -0.15f), new Vector3(0.45f, 0.55f, 0.45f), "rider");
                    Prim(PrimitiveType.Sphere, root.transform, "Helmet", new Vector3(0f, 1.85f, -0.1f), Vector3.one * 0.32f, "helmet");
                    Prim(PrimitiveType.Capsule, root.transform, "Passenger", new Vector3(0f, 1.2f, -0.6f), new Vector3(0.42f, 0.5f, 0.42f), "ped_b");
                    wheels.Add(Wheel(root.transform, new Vector3(0f, 0.32f, 0.72f), 0.64f));
                    wheels.Add(Wheel(root.transform, new Vector3(0f, 0.32f, -0.72f), 0.64f));
                    brake.Add(Box(root.transform, "Brake light", new Vector3(0f, 0.8f, -0.92f), new Vector3(0.18f, 0.08f, 0.05f), "lamp").GetComponent<Renderer>());
                    break;
                case RoadUserKind.Bus:
                    length = 10f; width = 2.5f;
                    Box(root.transform, "Body", new Vector3(0f, 1.6f, 0f), new Vector3(2.4f, 2.7f, 10f), "bus");
                    Box(root.transform, "Windows", new Vector3(0f, 2.2f, 0f), new Vector3(2.45f, 0.8f, 9f), "interior");
                    for (var z = -3.5f; z <= 3.5f; z += 7f)
                    {
                        wheels.Add(Wheel(root.transform, new Vector3(-1.1f, 0.5f, z), 1f));
                        wheels.Add(Wheel(root.transform, new Vector3(1.1f, 0.5f, z), 1f));
                    }

                    brake.Add(Box(root.transform, "Brake L", new Vector3(-0.9f, 0.9f, -5.02f), new Vector3(0.3f, 0.2f, 0.05f), "lamp").GetComponent<Renderer>());
                    brake.Add(Box(root.transform, "Brake R", new Vector3(0.9f, 0.9f, -5.02f), new Vector3(0.3f, 0.2f, 0.05f), "lamp").GetComponent<Renderer>());
                    break;
                case RoadUserKind.Bicycle:
                    length = 1.8f; width = 0.6f;
                    Box(root.transform, "Frame", new Vector3(0f, 0.55f, 0f), new Vector3(0.08f, 0.35f, 1.1f), "bicycle");
                    Prim(PrimitiveType.Capsule, root.transform, "Rider", new Vector3(0f, 1.3f, -0.1f), new Vector3(0.4f, 0.55f, 0.4f), "ped_a");
                    wheels.Add(Wheel(root.transform, new Vector3(0f, 0.35f, 0.55f), 0.7f));
                    wheels.Add(Wheel(root.transform, new Vector3(0f, 0.35f, -0.55f), 0.7f));
                    break;
                default:
                    length = 4.4f; width = 1.8f;
                    Box(root.transform, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.7f, 4.4f), "car");
                    Box(root.transform, "Cabin", new Vector3(0f, 1.25f, -0.2f), new Vector3(1.6f, 0.55f, 2.3f), "interior");
                    foreach (var x in new[] { -0.8f, 0.8f })
                    foreach (var z in new[] { -1.35f, 1.35f })
                        wheels.Add(Wheel(root.transform, new Vector3(x, 0.33f, z), 0.66f));
                    brake.Add(Box(root.transform, "Brake L", new Vector3(-0.65f, 0.75f, -2.21f), new Vector3(0.3f, 0.12f, 0.05f), "lamp").GetComponent<Renderer>());
                    brake.Add(Box(root.transform, "Brake R", new Vector3(0.65f, 0.75f, -2.21f), new Vector3(0.3f, 0.12f, 0.05f), "lamp").GetComponent<Renderer>());
                    break;
            }

            ai.SetShape(kind, length, width);
            var so = new SerializedObject(ai);
            var wheelProp = so.FindProperty("m_Wheels");
            wheelProp.arraySize = wheels.Count;
            for (var i = 0; i < wheels.Count; i++) wheelProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            var brakeProp = so.FindProperty("m_BrakeLights");
            brakeProp.arraySize = brake.Count;
            for (var i = 0; i < brake.Count; i++) brakeProp.GetArrayElementAtIndex(i).objectReferenceValue = brake[i];
            so.FindProperty("m_FocusPoint").objectReferenceValue = Point(root.transform, "Focus", new Vector3(0f, 1f, length * 0.3f));
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<TrafficVehicleAI>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform Wheel(Transform parent, Vector3 position, float diameter)
        {
            // Pivot rotates around local X (rolling); the cylinder child is laid on its side.
            var pivot = Group(parent, "Wheel");
            pivot.localPosition = position;
            Prim(PrimitiveType.Cylinder, pivot, "Tyre", Vector3.zero, new Vector3(diameter, 0.1f, diameter), "housing", rotation: Quaternion.Euler(0f, 0f, 90f));
            return pivot;
        }

        public static PedestrianAI PedestrianPrefab(string variant)
        {
            EnsureFolder(PrefabsFolder);
            var path = $"{PrefabsFolder}/NPC_Pedestrian_{variant}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<PedestrianAI>(path);
            if (existing != null)
                return existing;
            var root = new GameObject("NPC_Pedestrian_" + variant);
            var ped = root.AddComponent<PedestrianAI>();
            Prim(PrimitiveType.Capsule, root.transform, "Body", new Vector3(0f, 0.8f, 0f), new Vector3(0.45f, 0.65f, 0.35f), variant == "A" ? "ped_a" : "ped_b");
            Prim(PrimitiveType.Sphere, root.transform, "Head", new Vector3(0f, 1.58f, 0f), Vector3.one * 0.24f, "skin");
            Box(root.transform, "Nose (facing)", new Vector3(0f, 1.58f, 0.12f), new Vector3(0.05f, 0.05f, 0.08f), "skin");
            var so = new SerializedObject(ped);
            so.FindProperty("m_FocusPoint").objectReferenceValue = Point(root.transform, "Focus", new Vector3(0f, 1.2f, 0f));
            so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<PedestrianAI>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Left-hand-drive greybox cockpit with the DriverController.</summary>
        public static DriverController PlayerCarPrefab()
        {
            EnsureFolder(PrefabsFolder);
            var path = $"{PrefabsFolder}/PlayerCar.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<DriverController>(path);
            if (existing != null)
                return existing;

            var root = new GameObject("PlayerCar");
            var t = root.transform;
            Box(t, "Floor", new Vector3(0f, 0.35f, 0f), new Vector3(1.8f, 0.2f, 4.3f), "player_car");
            Box(t, "Hood", new Vector3(0f, 0.8f, 1.55f), new Vector3(1.8f, 0.3f, 1.2f), "player_car");
            Box(t, "Boot", new Vector3(0f, 0.8f, -1.75f), new Vector3(1.8f, 0.35f, 0.8f), "player_car");
            Box(t, "Dashboard", new Vector3(0f, 0.95f, 0.78f), new Vector3(1.7f, 0.18f, 0.45f), "interior");
            Box(t, "Door L", new Vector3(-0.88f, 0.75f, -0.1f), new Vector3(0.06f, 0.55f, 2.2f), "player_car");
            Box(t, "Door R", new Vector3(0.88f, 0.75f, -0.1f), new Vector3(0.06f, 0.55f, 2.2f), "player_car");
            Box(t, "A-pillar L", new Vector3(-0.82f, 1.3f, 0.75f), new Vector3(0.07f, 0.65f, 0.07f), "interior", rotation: Quaternion.Euler(-30f, 0f, 0f));
            Box(t, "A-pillar R", new Vector3(0.82f, 1.3f, 0.75f), new Vector3(0.07f, 0.65f, 0.07f), "interior", rotation: Quaternion.Euler(-30f, 0f, 0f));
            Box(t, "Roof", new Vector3(0f, 1.6f, -0.35f), new Vector3(1.7f, 0.06f, 1.7f), "player_car");
            Box(t, "B-pillar L", new Vector3(-0.82f, 1.25f, -0.6f), new Vector3(0.07f, 0.7f, 0.07f), "interior");
            Box(t, "B-pillar R", new Vector3(0.82f, 1.25f, -0.6f), new Vector3(0.07f, 0.7f, 0.07f), "interior");
            Box(t, "Seat", new Vector3(-0.38f, 0.65f, -0.35f), new Vector3(0.5f, 0.15f, 0.5f), "interior");
            Box(t, "Seat back", new Vector3(-0.38f, 1.0f, -0.62f), new Vector3(0.5f, 0.65f, 0.1f), "interior");

            var column = Group(t, "Steering column");
            column.localPosition = new Vector3(-0.38f, 1.0f, 0.42f);
            column.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            var wheel = Group(column, "Steering wheel");
            Prim(PrimitiveType.Cylinder, wheel, "Rim", Vector3.zero, new Vector3(0.38f, 0.015f, 0.38f), "housing", rotation: Quaternion.Euler(90f, 0f, 0f));
            Box(wheel, "Spoke", Vector3.zero, new Vector3(0.36f, 0.04f, 0.02f), "pole");

            var brakes = new[]
            {
                Box(t, "Brake L", new Vector3(-0.65f, 0.85f, -2.16f), new Vector3(0.3f, 0.12f, 0.05f), "lamp").GetComponent<Renderer>(),
                Box(t, "Brake R", new Vector3(0.65f, 0.85f, -2.16f), new Vector3(0.3f, 0.12f, 0.05f), "lamp").GetComponent<Renderer>(),
            };
            var left = new[]
            {
                Box(t, "Indicator FL", new Vector3(-0.8f, 0.8f, 2.16f), new Vector3(0.15f, 0.08f, 0.05f), "lamp").GetComponent<Renderer>(),
                Box(t, "Indicator RL", new Vector3(-0.85f, 0.95f, -2.16f), new Vector3(0.12f, 0.08f, 0.05f), "lamp").GetComponent<Renderer>(),
                Box(t, "Dash arrow L", new Vector3(-0.55f, 1.05f, 0.68f), new Vector3(0.04f, 0.02f, 0.04f), "lamp").GetComponent<Renderer>(),
            };
            var right = new[]
            {
                Box(t, "Indicator FR", new Vector3(0.8f, 0.8f, 2.16f), new Vector3(0.15f, 0.08f, 0.05f), "lamp").GetComponent<Renderer>(),
                Box(t, "Indicator RR", new Vector3(0.85f, 0.95f, -2.16f), new Vector3(0.12f, 0.08f, 0.05f), "lamp").GetComponent<Renderer>(),
                Box(t, "Dash arrow R", new Vector3(-0.21f, 1.05f, 0.68f), new Vector3(0.04f, 0.02f, 0.04f), "lamp").GetComponent<Renderer>(),
            };

            var eye = Point(t, "Eye point", new Vector3(-0.38f, 1.22f, -0.2f));
            var driver = root.AddComponent<DriverController>();
            driver.Configure(eye, wheel, brakes, left, right);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<DriverController>();
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
