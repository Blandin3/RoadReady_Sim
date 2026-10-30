using System.Collections.Generic;
using System.Linq;
using RoadReady.Player;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Add PolyDrive Dashboard To Player Car: copies the instrument cluster of the PolyDrive Bavaria
    /// (dial face, four needles and ten warning icons) into PlayerCar.prefab, in front of the steering wheel, and wires
    /// it to the DriverController: speedometer and rev counter follow the car, the fuel and temperature needles sit at
    /// normal, the parking-brake, turn-arrow and dipped-beam icons light with the car's state.
    /// Also run by "Use Imported Car For Driver (Bentley)", so the cluster survives rebuilding the car.
    /// </summary>
    public static class PolyDriveDashboard
    {
        public const string GroupName = "PolyDrive Cluster";
        const string k_PackRoot = "Assets/PolyDrive - FREE";
        const string k_Prefab = k_PackRoot + "/Prefabs/Bavaria_Sport-3.prefab";
        const float k_LiftAboveRim = -0.03f; // metres; raise/lower if the dials still sit behind the rim or float above the dash

        // Needle -> what it shows. The sweeps are read off the printed dial (Gauges_05.png); the pack's needles rest on zero.
        static readonly (string name, DashboardGauges.Source source, float min, float max, float sweep, float fixedValue)[] k_Needles =
        {
            ("Needle_#2", DashboardGauges.Source.Speed, 0f, 250f, 225f, 0f),
            ("Needle_#3", DashboardGauges.Source.Rpm, 0f, 8000f, 235f, 0f),
            ("Needle_#4", DashboardGauges.Source.Fixed, 0f, 1f, 50f, 0.6f),   // fuel: no fuel model, shown at 60 %
            ("Needle_#1", DashboardGauges.Source.Fixed, 50f, 130f, 50f, 90f), // coolant: normal running temperature
        };

        // Icon texture -> when it lights. Chosen by the texture on the icon, not by the object's name (the pack's names do
        // not match the pictures). The two turn arrows share one texture; the left-most is the left arrow.
        static readonly Dictionary<string, (DashboardWarningLights.Condition condition, Color colour)> k_Icons =
            new Dictionary<string, (DashboardWarningLights.Condition, Color)>
            {
                ["Low_Beam_Icon"] = (DashboardWarningLights.Condition.EngineOn, new Color(0.1f, 0.9f, 0.2f)),
                ["High_Beam_Icon"] = (DashboardWarningLights.Condition.Never, new Color(0.2f, 0.4f, 1f)),
                ["Fog_Light_Icon"] = (DashboardWarningLights.Condition.Never, new Color(0.1f, 0.9f, 0.2f)),
                ["Hazard_Warning_Icon"] = (DashboardWarningLights.Condition.Never, new Color(1f, 0.1f, 0.05f)),
                ["Seatbelt_Icon"] = (DashboardWarningLights.Condition.Never, new Color(1f, 0.1f, 0.05f)),
                ["Parking_Brake_Icon"] = (DashboardWarningLights.Condition.HandbrakeOn, new Color(1f, 0.1f, 0.05f)),
                ["Fuel_Low_Icon"] = (DashboardWarningLights.Condition.Never, new Color(1f, 0.55f, 0f)),
                ["Check_Engine_Icon"] = (DashboardWarningLights.Condition.Never, new Color(1f, 0.55f, 0f)),
            };

        [MenuItem("RoadReady/Add PolyDrive Dashboard To Player Car", priority = 32)]
        public static void AddToPlayerCar()
        {
            var playerPath = PrefabsFolder + "/PlayerCar.prefab";
            var root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var grab = root.transform.Find("Steering Wheel Grab");
                if (grab == null)
                {
                    EditorUtility.DisplayDialog("RoadReady", "The player car has no steering wheel yet. Run RoadReady > Use Imported Car For Driver (Bentley) first.", "OK");
                    return;
                }

                var report = Build(root, grab.localPosition);
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[RoadReady] " + report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Replaces any earlier cluster on the (open) player car root. <paramref name="hub"/> is the steering wheel hub in root space.</summary>
        public static string Build(GameObject root, Vector3 hub)
        {
            var rootT = root.transform;
            Transform old;
            while ((old = rootT.Find(GroupName)) != null)
                Object.DestroyImmediate(old.gameObject);

            var pack = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (pack == null)
                return $"PolyDrive dashboard not added: {k_Prefab} not found.";

            PlayerCarModelSetup.ConvertMaterialsToUrp(k_PackRoot + "/Materials");
            PrepareMaterials();

            var driver = root.GetComponent<DriverController>();
            var gauges = root.GetComponent<DashboardGauges>() ?? root.AddComponent<DashboardGauges>();
            var lights = root.GetComponent<DashboardWarningLights>() ?? root.AddComponent<DashboardWarningLights>();

            var source = PrefabUtility.LoadPrefabContents(k_Prefab);
            try
            {
                var sourceRoot = source.transform;
                var find = sourceRoot.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                if (!find.TryGetValue("_Dashboard", out var face) || !find.TryGetValue("SteeringWheel", out var sourceWheel))
                    return "PolyDrive dashboard not added: the pack's prefab has no _Dashboard / SteeringWheel.";

                var group = new GameObject(GroupName).transform;
                group.SetParent(rootT, false);
                // The pack's wheel hub lands on the Bentley's, so the cluster keeps the pack's place relative to the wheel.
                // The Bentley's wheel is bigger and sits higher than the pack's, so its rim covers the dials unless the cluster is lifted clear of it.
                group.localPosition = hub - sourceRoot.InverseTransformPoint(sourceWheel.position) + Vector3.up * k_LiftAboveRim;

                Transform Copy(string name)
                {
                    if (!find.TryGetValue(name, out var part))
                        return null;
                    var copy = Object.Instantiate(part.gameObject, group, false).transform;
                    copy.name = name;
                    copy.localPosition = sourceRoot.InverseTransformPoint(part.position);
                    copy.localRotation = Quaternion.Inverse(sourceRoot.rotation) * part.rotation;
                    return copy;
                }

                // The dial normal (towards the driver) is the dial face mesh's thinnest direction.
                var filter = face.GetComponent<MeshFilter>();
                var extents = filter.sharedMesh.bounds.extents;
                var thin = extents.x <= extents.y && extents.x <= extents.z ? Vector3.right : extents.y <= extents.z ? Vector3.up : Vector3.forward;
                var normal = Quaternion.Inverse(sourceRoot.rotation) * face.TransformDirection(thin);
                if (normal.z > 0f)
                    normal = -normal; // the driver sits behind the cluster (-Z)

                Copy("_Dashboard");

                var needles = new List<DashboardGauges.Needle>();
                foreach (var (name, src, min, max, sweep, fixedValue) in k_Needles)
                {
                    var needle = Copy(name);
                    if (needle == null)
                        continue;
                    var pivot = DialCentre(needle);
                    needles.Add(new DashboardGauges.Needle
                    {
                        needle = needle,
                        source = src,
                        axis = Quaternion.Inverse(needle.localRotation) * normal,
                        usePivot = true,
                        pivot = pivot,
                        minValue = min,
                        maxValue = max,
                        degreesAtMax = sweep,
                        fixedValue = fixedValue,
                    });
                }

                var lamps = new List<DashboardWarningLights.Lamp>();
                var arrows = new List<Renderer>();
                var green = new Color(0.1f, 0.9f, 0.2f);
                foreach (var name in find.Keys.Where(k => k.StartsWith("Dashboard_Icon")).OrderBy(k => k))
                {
                    var icon = Copy(name);
                    var renderer = icon != null ? icon.GetComponent<Renderer>() : null;
                    var texture = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.GetTexture("_BaseMap") : null;
                    if (texture == null)
                        continue;
                    if (texture.name == "Turn_indicator_Icon")
                        arrows.Add(renderer);
                    else if (k_Icons.TryGetValue(texture.name, out var rule))
                        lamps.Add(new DashboardWarningLights.Lamp { renderer = renderer, condition = rule.condition, onColor = rule.colour });
                }

                // Left of the two arrows is the left indicator (the cluster faces -Z, so the driver's left is smaller x).
                arrows = arrows.OrderBy(r => group.InverseTransformPoint(r.bounds.center).x).ToList();
                for (var i = 0; i < arrows.Count; i++)
                    lamps.Add(new DashboardWarningLights.Lamp
                    {
                        renderer = arrows[i],
                        condition = i == 0 ? DashboardWarningLights.Condition.IndicatorLeft : DashboardWarningLights.Condition.IndicatorRight,
                        onColor = green,
                    });

                gauges.Needles = needles.ToArray();
                lights.Lamps = lamps.ToArray();
                if (driver != null)
                    EditorUtility.SetDirty(driver);
                EditorUtility.SetDirty(gauges);
                EditorUtility.SetDirty(lights);
                return $"PolyDrive dashboard added: {needles.Count} needles, {lamps.Count} warning icons, dial normal {normal}.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
            }
        }

        /// <summary>
        /// The needle meshes are not modelled around their origin: the centre cap (the roundest sub-mesh) marks the dial
        /// centre, returned in the cluster's space.
        /// </summary>
        static Vector3 DialCentre(Transform needle)
        {
            var mesh = needle.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var best = Vector3.zero;
            var roundest = float.MaxValue;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var bounds = new Bounds();
                var first = true;
                foreach (var i in mesh.GetIndices(s))
                {
                    if (first) { bounds = new Bounds(vertices[i], Vector3.zero); first = false; }
                    else bounds.Encapsulate(vertices[i]);
                }

                var size = bounds.size;
                var sorted = new[] { size.x, size.y, size.z }.OrderByDescending(v => v).ToArray();
                var aspect = sorted[0] / Mathf.Max(sorted[1], 1e-6f);
                if (!first && aspect < roundest)
                {
                    roundest = aspect;
                    best = bounds.center;
                }
            }

            return needle.parent.InverseTransformPoint(needle.TransformPoint(best));
        }

        /// <summary>The pack's materials are cut-outs on a black dial; the dial glows, the icons take their colour from the lamp script.</summary>
        static void PrepareMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { k_PackRoot + "/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var n = mat.name;
                var cluster = n == "_Dashboard" || n.StartsWith("Dashboard_Icon") || n.StartsWith("Needle");
                if (!cluster)
                    continue;

                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (n == "_Dashboard")
                {
                    mat.SetTexture("_EmissionMap", mat.GetTexture("_BaseMap"));
                    mat.SetColor("_EmissionColor", Color.white * 0.9f);
                    mat.SetFloat("_Smoothness", 0.1f);
                }
                else if (n.StartsWith("Needle"))
                {
                    mat.SetColor("_EmissionColor", mat.GetColor("_BaseColor") * 0.7f);
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
