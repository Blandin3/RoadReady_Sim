using System.Collections.Generic;
using System.Linq;
using RoadReady.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Use Imported Car For Driver: puts the imported Bentley (Assets/ThirdParty/Bentley) inside
    /// PlayerCar.prefab (used by every scenario scene), hides the greybox cockpit, and wires the eye point, steering
    /// wheel, gear lever, handbrake, lamps and mirrors to the driving logic.
    /// Safe to run again after tweaking; it rebuilds the model each time.
    /// </summary>
    public static class PlayerCarModelSetup
    {
        const string k_ModelFile = "Assets/ThirdParty/Bentley/final_model_bATUR.fbx";
        const string k_ModelName = "Car Model";
        const string k_EyeName = "Eye point";
        const string k_WheelGrabName = "Steering Wheel Grab";

        // Driver's eye relative to the steering wheel hub (metres): up, and back towards the seat.
        const float k_EyeAboveWheel = 0.28f;
        const float k_EyeBehindWheel = 0.55f;

        [MenuItem("RoadReady/Use Imported Car For Driver (Bentley)", priority = 30)]
        public static void UseImportedCar()
        {
            EnsureModelImportScale();
            var carModel = AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelFile);
            if (carModel == null)
            {
                EditorUtility.DisplayDialog("RoadReady", $"Car model not found at\n{k_ModelFile}", "OK");
                return;
            }

            var playerPath = PrefabsFolder + "/PlayerCar.prefab";
            var root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var report = BuildModel(root, carModel);
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[RoadReady] Player car now uses {carModel.name}. {report}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The model's numbers are already metres; Unity would otherwise shrink them to a hundredth.</summary>
        static void EnsureModelImportScale()
        {
            var importer = AssetImporter.GetAtPath(k_ModelFile) as ModelImporter;
            if (importer == null || (!importer.useFileScale && Mathf.Approximately(importer.globalScale, 1f)))
                return;
            importer.useFileScale = false;
            importer.globalScale = 1f;
            importer.SaveAndReimport();
        }

        static string BuildModel(GameObject root, GameObject carModel)
        {
            var rootT = root.transform;
            foreach (var previous in new[]
            {
                k_ModelName, k_WheelGrabName, "Windshield Menu", "Windshield Menu Tint", CockpitControlsKit.ShifterName, CockpitControlsKit.HandbrakeName,
                CockpitControlsKit.StartButtonName, CockpitControlsKit.ExitButtonName, "SteeringWheel Pivot",
                "Indicator Front Left", "Indicator Rear Left", "Indicator Front Right", "Indicator Rear Right",
                PolyDriveDashboard.GroupName,
            })
            {
                // Earlier runs may have left several (each run adds one).
                Transform old;
                while ((old = rootT.Find(previous)) != null)
                    Object.DestroyImmediate(old.gameObject);
            }

            // Hide the greybox cockpit (kept in the prefab so it can be switched back on).
            foreach (Transform child in rootT)
                if (child.name != k_EyeName)
                    child.gameObject.SetActive(false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(carModel, rootT);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = k_ModelName;
            var m = model.transform;
            m.localPosition = Vector3.zero;
            m.localRotation = Quaternion.identity;
            m.localScale = Vector3.one;

            FixMaterials(m);

            // Nothing sees under the bonnet; an engine would be dead weight in VR.
            foreach (var t in m.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Engine_Geo"))
                    t.gameObject.SetActive(false);
            var wheelParts = CombineWheels(m);

            // The model faces +Z (the DriverController's forward). Centre it on the root and sit the tyres on the ground.
            var bounds = ModelBounds(model, rootT);
            m.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            bounds = ModelBounds(model, rootT);

            var driver = root.GetComponent<DriverController>();
            var cockpit = CockpitControlsKit.Build(rootT, m, driver, out var steering, out var axis);
            var hub = rootT.InverseTransformPoint(steering.position);

            var eye = rootT.Find(k_EyeName);
            if (eye == null)
            {
                eye = new GameObject(k_EyeName).transform;
                eye.SetParent(rootT, false);
            }

            eye.localPosition = new Vector3(hub.x, Mathf.Min(hub.y + k_EyeAboveWheel, bounds.max.y - 0.12f), hub.z - k_EyeBehindWheel);
            eye.localRotation = Quaternion.identity;

            var wheelbase = Wheelbase(m, rootT, bounds.size.z);
            var so = new SerializedObject(driver);
            so.FindProperty("m_EyePoint").objectReferenceValue = eye;
            so.FindProperty("m_SteeringWheel").objectReferenceValue = steering;
            so.FindProperty("m_SteeringAxis").vector3Value = axis;
            so.FindProperty("m_Length").floatValue = bounds.size.z;
            // Body width for the collision footprint: the measured bounds include the wing mirrors.
            so.FindProperty("m_Width").floatValue = Mathf.Min(bounds.size.x, 1.85f);
            so.FindProperty("m_Wheelbase").floatValue = wheelbase;
            so.FindProperty("m_Acceleration").floatValue = 4f; // 0-50 km/h in about 5.5 s, like an ordinary automatic
            // The greybox lamp cubes are hidden; the brake lamps are the model's red lens glass, the indicators small amber lamps.
            so.FindProperty("m_BrakeLights").arraySize = 0;
            so.FindProperty("m_LeftIndicators").arraySize = 0;
            so.FindProperty("m_RightIndicators").arraySize = 0;
            so.FindProperty("m_LeftIndicatorSlots").arraySize = 0;
            so.FindProperty("m_RightIndicatorSlots").arraySize = 0;
            var lamps = WireLamps(m, rootT, bounds, so);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Recreated each time so the mirror layout picks up the current defaults.
            var oldMirrors = root.GetComponent<CarMirrors>();
            if (oldMirrors != null)
                Object.DestroyImmediate(oldMirrors);
            var mirrors = root.AddComponent<CarMirrors>();
            mirrors.Configure(eye, MirrorScreenMaterial());
            var mirrorReport = MirrorsFromModel(mirrors, m, rootT, eye);

            // The Bentley's own dials are a flat texture (no needles to turn), so the PolyDrive cluster goes in front of it.
            var dashboard = PolyDriveDashboard.Build(root, hub);

            var controls = BuildControls(root, steering, axis, hub);
            if (hub.x > 0f)
                Debug.LogWarning("[RoadReady] The steering wheel is on the right (right-hand drive). Rwanda drives on the right, so a left-hand-drive car is expected.");

            return $"Size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00} m, wheelbase {wheelbase:0.00} m, eye point at {eye.localPosition}. " +
                   $"{wheelParts}, {cockpit}, {lamps}, {controls}, mirrors: {mirrorReport}. {dashboard}";
        }

        // ================================================================ materials

        /// <summary>
        /// The model's materials import with smoothness 1.42 (the valid maximum is 1), which makes the whole cabin look
        /// like a mirror. Each opaque material is copied into the project with a sensible smoothness; the clear windows,
        /// the red lens glass and the mirror glass keep theirs.
        /// </summary>
        static void FixMaterials(Transform model)
        {
            EnsureFolder(PrefabsFolder + "/CarMaterials");
            var copies = new Dictionary<Material, Material>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var source = mats[i];
                    if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Lit")
                        continue;
                    var n = source.name;
                    if (n.Contains("Window") || n == "RED_GLASS" || n.StartsWith("phong") || source.GetFloat("_Surface") > 0.5f)
                        continue;
                    if (!copies.TryGetValue(source, out var copy))
                        copies[source] = copy = SmoothCopy(source);
                    mats[i] = copy;
                }

                r.sharedMaterials = mats;
            }
        }

        static Material SmoothCopy(Material source)
        {
            var n = source.name;
            var name = n.Length > 48 ? n.Substring(n.Length - 48) : n;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            var path = PrefabsFolder + "/CarMaterials/" + name + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(source) { name = name };
                AssetDatabase.CreateAsset(copy, path);
            }

            var lower = n.ToLowerInvariant();
            var smoothness = lower.Contains("interior") || lower.Contains("seatbelt") ? 0.3f
                : lower.Contains("paint") || lower.Contains("coloured") ? 0.8f
                : 0.5f;
            copy.SetFloat("_Smoothness", smoothness);
            copy.SetFloat("_Metallic", Mathf.Min(source.GetFloat("_Metallic"), 0.2f));
            EditorUtility.SetDirty(copy);
            return copy;
        }

        /// <summary>
        /// Fits the three mirror screens onto the car's own mirrors. Each real mirror glass is a flat piece of the body
        /// mesh; its exact position, angle and outline (the convex hull of its corners) go to the screen, which then fills
        /// the real glass. Looked up near the spots measured on the Bentley (left, right, interior).
        /// </summary>
        static string MirrorsFromModel(CarMirrors mirrors, Transform model, Transform root, Transform eye)
        {
            var spots = new[] { new Vector3(-1.011f, 1.032f, 0.370f), new Vector3(1.011f, 1.032f, 0.370f), new Vector3(0f, 1.174f, 0.388f) };
            // Each material of each mesh on its own, so the glass is not welded to the housing round it.
            var groups = new List<List<Vector3[]>>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                if (renderer == null || mesh == null || mesh.vertexCount == 0 || filter.transform.parent == null || filter.transform.parent.name.StartsWith("Wheel"))
                    continue;
                var vertices = mesh.vertices;
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = new List<Vector3[]>();
                    var t = mesh.GetTriangles(s);
                    for (var i = 0; i < t.Length; i += 3)
                    {
                        var a = Local(filter, root, vertices[t[i]]);
                        var b = Local(filter, root, vertices[t[i + 1]]);
                        var c = Local(filter, root, vertices[t[i + 2]]);
                        var centre = (a + b + c) / 3f;
                        if (spots.Any(p => Vector3.Distance(p, centre) < 0.2f))
                            tris.Add(new[] { a, b, c });
                    }

                    groups.Add(tris);
                }
            }

            var clusters = groups.SelectMany(GlassClusters).ToList();
            var eyeLocal = eye.localPosition;
            var report = new List<string>();
            for (var index = 0; index < spots.Length && index < mirrors.Mirrors.Length; index++)
            {
                var spot = spots[index];
                Vector3[][] best = null;
                var bestDistance = float.MaxValue;
                foreach (var cluster in clusters)
                {
                    if (cluster.Count < 20 || cluster.Count > 80)
                        continue;
                    var points = cluster.SelectMany(t => t).ToList();
                    var mean = points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Count;
                    var distance = Vector3.Distance(mean, spot);
                    if (distance > 0.06f || distance >= bestDistance)
                        continue;
                    var n = AreaNormal(cluster);
                    var thickness = points.Max(p => Vector3.Dot(p - mean, n)) - points.Min(p => Vector3.Dot(p - mean, n));
                    var bounds = new Bounds(points[0], Vector3.zero);
                    foreach (var p in points)
                        bounds.Encapsulate(p);
                    if (thickness > 0.008f || bounds.size.magnitude < 0.1f)
                        continue; // glass is a flat plate of mirror size
                    bestDistance = distance;
                    best = cluster.ToArray();
                }

                if (best == null)
                {
                    report.Add($"{mirrors.Mirrors[index].name}: glass not found");
                    continue;
                }

                var centreOfGlass = Vector3.zero;
                foreach (var t in best)
                    centreOfGlass += (t[0] + t[1] + t[2]) / 3f;
                centreOfGlass /= best.Length;
                var normal = AreaNormal(best.ToList());
                if (Vector3.Dot(normal, eyeLocal - centreOfGlass) < 0f)
                    normal = -normal; // towards the driver
                var forward = -normal;
                var up = (Vector3.up - normal * Vector3.Dot(Vector3.up, normal)).normalized;
                var right = Vector3.Cross(up, forward);

                var plane = new List<Vector2>();
                foreach (var t in best)
                    foreach (var v in t)
                        plane.Add(new Vector2(Vector3.Dot(v - centreOfGlass, right), Vector3.Dot(v - centreOfGlass, up)));
                var min = plane.Aggregate(Vector2.Min);
                var max = plane.Aggregate(Vector2.Max);
                var size = max - min;
                var middle = (min + max) * 0.5f;

                var mirror = mirrors.Mirrors[index];
                mirror.exact = true;
                mirror.exactPosition = centreOfGlass + right * middle.x + up * middle.y + normal * 0.003f; // 3 mm in front: no flicker
                mirror.exactRotation = Quaternion.LookRotation(forward, up);
                mirror.exactOutline = ConvexHull(plane).Select(p => new Vector2((p.x - middle.x) / size.x, (p.y - middle.y) / size.y)).ToArray();
                mirror.size = size;
                report.Add($"{mirror.name} fits the glass ({size.x:0.00} x {size.y:0.00} m)");
            }

            return string.Join(", ", report);
        }

        static Vector3 Local(MeshFilter filter, Transform root, Vector3 vertex) => root.InverseTransformPoint(filter.transform.TransformPoint(vertex));

        /// <summary>Area-weighted normal of a group of triangles, all turned the same way.</summary>
        static Vector3 AreaNormal(List<Vector3[]> tris)
        {
            var sum = Vector3.zero;
            foreach (var t in tris)
            {
                var n = Vector3.Cross(t[1] - t[0], t[2] - t[0]);
                sum += sum != Vector3.zero && Vector3.Dot(n, sum) < 0f ? -n : n;
            }

            return sum.normalized;
        }

        /// <summary>Connected groups of triangles (vertices welded to 2 mm).</summary>
        static List<List<Vector3[]>> GlassClusters(List<Vector3[]> tris)
        {
            var ids = new Dictionary<Vector3Int, int>();
            int Id(Vector3 v)
            {
                var key = Vector3Int.RoundToInt(v * 500f);
                if (!ids.TryGetValue(key, out var id))
                    ids[key] = id = ids.Count;
                return id;
            }

            var triIds = tris.Select(t => new[] { Id(t[0]), Id(t[1]), Id(t[2]) }).ToList();
            var parent = Enumerable.Range(0, ids.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (var t in triIds)
            {
                parent[Find(t[0])] = Find(t[1]);
                parent[Find(t[1])] = Find(t[2]);
            }

            return tris.Select((t, i) => (t, root: Find(triIds[i][0])))
                .GroupBy(x => x.root)
                .Select(g => g.Select(x => x.t).ToList())
                .ToList();
        }

        /// <summary>Counter-clockwise convex hull (Andrew's monotone chain).</summary>
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var sorted = points.OrderBy(p => p.x).ThenBy(p => p.y).Distinct().ToList();
            if (sorted.Count < 3)
                return sorted;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var hull = new List<Vector2>();
            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            var lower = hull.Count + 1;
            for (var i = sorted.Count - 2; i >= 0; i--)
            {
                var p = sorted[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        // ================================================================ mirrors

        /// <summary>Unlit URP material saved as an asset, so the shader is always included in builds.</summary>
        static Material MirrorScreenMaterial()
        {
            var path = PrefabsFolder + "/MirrorScreen.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            material = new Material(shader) { name = "MirrorScreen" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        [MenuItem("RoadReady/Add Mirrors To Player Car", priority = 31)]
        public static void AddMirrors()
        {
            var playerPath = PrefabsFolder + "/PlayerCar.prefab";
            var root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var eye = root.transform.Find(k_EyeName);
                var oldMirrors = root.GetComponent<CarMirrors>();
                if (oldMirrors != null)
                    Object.DestroyImmediate(oldMirrors);
                var mirrors = root.AddComponent<CarMirrors>();
                mirrors.Configure(eye, MirrorScreenMaterial());
                var carModel = root.transform.Find(k_ModelName);
                if (carModel != null)
                    Debug.Log("[RoadReady] Mirrors: " + MirrorsFromModel(mirrors, carModel, root.transform, eye));
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                Debug.Log("[RoadReady] Left, right and interior mirrors added to the player car.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ================================================================ hand controls

        /// <summary>
        /// Grabbable steering wheel: a grab zone round the rim.
        /// </summary>
        static string BuildControls(GameObject root, Transform steering, Vector3 steeringAxis, Vector3 hub)
        {
            var rootT = root.transform;
            var driver = root.GetComponent<DriverController>();
            var so = new SerializedObject(driver);
            var lockDegrees = so.FindProperty("m_MaxSteerDegrees").floatValue * so.FindProperty("m_SteeringWheelRatio").floatValue;

            // Steering wheel: a sphere round the rim that the controllers' near grab can reach. Solid (not a trigger)
            // because XRI's near casting ignores triggers; the car ignores its own colliders when driving.
            var grabGo = new GameObject(k_WheelGrabName);
            grabGo.transform.SetParent(rootT, false);
            grabGo.transform.localPosition = hub;
            grabGo.AddComponent<SphereCollider>().radius = 0.24f;
            var wheel = grabGo.AddComponent<SteeringWheelGrab>();
            wheel.Configure(steering, steeringAxis, lockDegrees);
            so.FindProperty("m_Wheel").objectReferenceValue = wheel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return $"wheel grab (lock {lockDegrees:0}°)";
        }


        // ================================================================ wheels, lamps

        /// <summary>
        /// The model builds its four wheels out of over a thousand separate pieces. Merged into one mesh per material
        /// they cost a handful of draw calls instead (it matters on a headset).
        /// </summary>
        static string CombineWheels(Transform model)
        {
            var group = FindDeep(model, "Wheel1A_3D");
            if (group == null)
                return "no wheel group to merge";
            var pieces = group.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0 && f.GetComponent<MeshRenderer>() != null)
                .ToList();
            if (pieces.Count < 2)
                return "wheels left as they were";

            var count = pieces.Count;
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var piece in pieces)
            {
                var mats = piece.GetComponent<MeshRenderer>().sharedMaterials;
                for (var s = 0; s < piece.sharedMesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null)
                        continue;
                    if (!byMaterial.TryGetValue(mats[s], out var list))
                        byMaterial[mats[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = piece.sharedMesh, subMeshIndex = s, transform = group.worldToLocalMatrix * piece.transform.localToWorldMatrix });
                }
            }

            foreach (var piece in pieces)
                piece.gameObject.SetActive(false);

            // One mesh per material, then one mesh with a sub-mesh for each.
            var materials = new List<Material>();
            var parts = new List<CombineInstance>();
            foreach (var pair in byMaterial)
            {
                var merged = new Mesh { indexFormat = IndexFormat.UInt32 };
                merged.CombineMeshes(pair.Value.ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = merged, transform = Matrix4x4.identity });
                materials.Add(pair.Key);
            }

            var all = new Mesh { name = "PlayerCar_Wheels", indexFormat = IndexFormat.UInt32 };
            all.CombineMeshes(parts.ToArray(), false, false);
            EnsureFolder(GeneratedRoot + "/Meshes");
            var path = GeneratedRoot + "/Meshes/PlayerCar_Wheels.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(all, path);

            var holder = new GameObject("Wheels (merged)").transform;
            holder.SetParent(group, false);
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = all;
            holder.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            return $"wheels merged ({count} pieces -> 1 mesh, {materials.Count} materials)";
        }

        static string WireLamps(Transform model, Transform root, Bounds bounds, SerializedObject driver)
        {
            // Brake lamps: the red lens glass is the second material of the window mesh.
            var brake = new List<(Renderer, int, Color)>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name != "RED_GLASS")
                        continue;
                    mats[i] = BrakeLampMaterial(mats[i]);
                    r.sharedMaterials = mats;
                    brake.Add((r, i, new Color(0.35f, 0f, 0f)));
                }
            }

            WriteSlots(driver.FindProperty("m_BrakeLampSlots"), brake);

            // Indicators: small amber lamps at the four corners (the model's own lamps are one shared material).
            var amber = IndicatorMaterial();
            var left = new List<Renderer>();
            var right = new List<Renderer>();
            var front = bounds.max.z;
            var rear = bounds.min.z;
            foreach (var (name, x, y, z, size, isLeft) in new[]
            {
                ("Indicator Front Left", -0.68f, 0.66f, front - 0.38f, new Vector3(0.16f, 0.035f, 0.04f), true),
                ("Indicator Rear Left", -0.64f, 0.88f, rear + 0.26f, new Vector3(0.14f, 0.035f, 0.04f), true),
                ("Indicator Front Right", 0.68f, 0.66f, front - 0.38f, new Vector3(0.16f, 0.035f, 0.04f), false),
                ("Indicator Rear Right", 0.64f, 0.88f, rear + 0.26f, new Vector3(0.14f, 0.035f, 0.04f), false),
            })
            {
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(lamp.GetComponent<Collider>());
                lamp.name = name;
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(x, y, z);
                lamp.transform.localScale = size;
                var renderer = lamp.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = amber;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                (isLeft ? left : right).Add(renderer);
            }

            WriteRenderers(driver.FindProperty("m_LeftIndicators"), left);
            WriteRenderers(driver.FindProperty("m_RightIndicators"), right);
            return $"{brake.Count} brake lamp slot(s), {left.Count + right.Count} indicator lamps";
        }

        static Material BrakeLampMaterial(Material source)
        {
            var path = PrefabsFolder + "/PlayerCar_BrakeLamp.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = "PlayerCar_BrakeLamp" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_EmissionColor", new Color(0.35f, 0f, 0f));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material IndicatorMaterial()
        {
            var path = PrefabsFolder + "/PlayerCar_Indicator.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlayerCar_Indicator" };
            var amber = new Color(1f, 0.55f, 0f);
            material.SetColor("_BaseColor", amber * 0.25f);
            material.SetFloat("_Smoothness", 0.7f);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void WriteSlots(SerializedProperty array, List<(Renderer renderer, int index, Color off)> slots)
        {
            array.arraySize = slots.Count;
            for (var i = 0; i < slots.Count; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("renderer").objectReferenceValue = slots[i].renderer;
                element.FindPropertyRelative("materialIndex").intValue = slots[i].index;
                element.FindPropertyRelative("offEmission").colorValue = slots[i].off;
            }
        }

        static void WriteRenderers(SerializedProperty array, List<Renderer> renderers)
        {
            array.arraySize = renderers.Count;
            for (var i = 0; i < renderers.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }

        /// <summary>Distance between the front and rear wheel groups, from the merged wheel geometry.</summary>
        static float Wheelbase(Transform model, Transform root, float length)
        {
            var merged = FindDeep(model, "Wheels (merged)");
            var filter = merged != null ? merged.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
                return length * 0.6f;
            double front = 0, rear = 0;
            int nFront = 0, nRear = 0;
            foreach (var v in filter.sharedMesh.vertices)
            {
                var z = root.InverseTransformPoint(merged.TransformPoint(v)).z;
                if (z > 0f) { front += z; nFront++; }
                else { rear += z; nRear++; }
            }

            return nFront > 0 && nRear > 0 ? (float)(front / nFront - rear / nRear) : length * 0.6f;
        }

        // ================================================================ materials

        /// <summary>
        /// The pack ships Built-in RP materials (Standard + a custom double-sided shader), which render pink in URP.
        /// Switches them to URP/Lit, carrying over colour, textures, smoothness, emission and transparency.
        /// </summary>
        internal static int ConvertMaterialsToUrp(string folder)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[RoadReady] URP Lit shader not found.");
                return 0;
            }

            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var shaderName = mat.shader != null ? mat.shader.name : "";
                var doubleSided = shaderName == "Custom/DoubleSidedLit";
                if (!doubleSided && !shaderName.StartsWith("Standard") && shaderName != "Autodesk Interactive" && shaderName != "Hidden/InternalErrorShader")
                    continue;

                var mainTex = Tex(mat, "_MainTex");
                var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                var smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                var normal = Tex(mat, "_BumpMap");
                var emissionOn = mat.IsKeywordEnabled("_EMISSION");
                var emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                var emissionTex = Tex(mat, "_EmissionMap");
                var mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;
                var transparent = mode >= 2f || mat.name.Contains("Glass");

                mat.shader = lit;
                mat.SetTexture("_BaseMap", mainTex);
                if (transparent && color.a > 0.6f)
                    color.a = 0.25f; // windows must stay see-through from the driver's seat
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (emissionOn && emission.maxColorComponent > 0.001f)
                {
                    mat.SetColor("_EmissionColor", emission);
                    mat.SetTexture("_EmissionMap", emissionTex);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                if (transparent)
                    MakeTransparent(mat);
                if (doubleSided || transparent)
                    mat.SetFloat("_Cull", (float)CullMode.Off);

                EditorUtility.SetDirty(mat);
                count++;
            }

            AssetDatabase.SaveAssets();
            return count;
        }

        static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }

        static Texture Tex(Material mat, string name) => mat.HasProperty(name) ? mat.GetTexture(name) : null;

        // ================================================================ helpers

        static Transform FindDeep(Transform parent, string name) =>
            parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static Vector3 RendererCenter(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return t.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);
            return b.center;
        }

        /// <summary>Bounds of the LOD0 / non-LOD renderers, expressed in the root's local space.</summary>
        static Bounds ModelBounds(GameObject model, Transform root)
        {
            var renderers = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.activeInHierarchy && r is MeshRenderer && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null && r.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0)
                    renderers.Add(r);

            var first = true;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                foreach (var corner in Corners(b))
                {
                    var p = root.InverseTransformPoint(corner);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            return bounds;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (var i = 0; i < 8; i++)
                yield return new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
        }
    }
}
