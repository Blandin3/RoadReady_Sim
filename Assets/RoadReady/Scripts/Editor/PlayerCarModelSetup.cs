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
    /// RoadReady > Use Imported Car For Driver: puts the PolyDrive "Bavaria_Sport-3" model inside PlayerCar.prefab
    /// (used by every scenario scene), hides the greybox cockpit, and wires the eye point and steering wheel.
    /// Safe to run again after tweaking; it rebuilds the model each time.
    /// </summary>
    public static class PlayerCarModelSetup
    {
        const string k_PackFolder = "Assets/ThirdParty/PolyDrive - FREE";
        const string k_CarPrefab = k_PackFolder + "/Prefabs/Bavaria_Sport-3.prefab";
        const string k_ModelFile = k_PackFolder + "/Models/03_Bavaria/Bavaria_Sport-3.fbx";
        const string k_ModelName = "Car Model";
        const string k_EyeName = "Eye point";
        const string k_WheelGrabName = "Steering Wheel Grab";

        // Driver's eye relative to the steering wheel hub (metres): up, and back towards the seat.
        const float k_EyeAboveWheel = 0.30f;
        const float k_EyeBehindWheel = 0.55f;

        [MenuItem("RoadReady/Use Imported Car For Driver (PolyDrive Bavaria)", priority = 30)]
        public static void UseImportedCar()
        {
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CarPrefab);
            if (carPrefab == null)
            {
                EditorUtility.DisplayDialog("RoadReady", $"Car prefab not found at\n{k_CarPrefab}", "OK");
                return;
            }

            var converted = ConvertMaterialsToUrp(k_PackFolder + "/Materials");
            PolishMaterials(k_PackFolder);
            var playerPath = PrefabsFolder + "/PlayerCar.prefab";
            var root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var report = BuildModel(root, carPrefab);
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                Debug.Log($"[RoadReady] Player car now uses {carPrefab.name}. {report} Converted {converted} material(s) to URP.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static string BuildModel(GameObject root, GameObject carPrefab)
        {
            var rootT = root.transform;
            foreach (var previous in new[] { k_ModelName, k_WheelGrabName, CockpitControlsKit.ShifterName, CockpitControlsKit.HandbrakeName })
            {
                var old = rootT.Find(previous);
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
            }

            // Hide the greybox cockpit (kept in the prefab so it can be switched back on).
            foreach (Transform child in rootT)
                if (child.name != k_EyeName)
                    child.gameObject.SetActive(false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, rootT);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = k_ModelName;
            var m = model.transform;
            m.localPosition = Vector3.zero;
            m.localRotation = Quaternion.identity;
            m.localScale = Vector3.one;
            var restored = RestoreMaterialsFromModel(model);
            // A few pack renderers have empty material slots (they render magenta inside the cabin).
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterials.Any(mat => mat == null))
                    r.enabled = false;

            var wheel = FindDeep(m, "SteeringWheel");
            if (wheel == null)
                throw new System.InvalidOperationException("[RoadReady] No 'SteeringWheel' object in the car model.");

            // Face +Z (the DriverController's forward): long side along Z, steering wheel in the front half.
            var bounds = ModelBounds(model, rootT);
            if (bounds.size.x > bounds.size.z)
            {
                m.localRotation = Quaternion.Euler(0f, 90f, 0f);
                bounds = ModelBounds(model, rootT);
            }

            if (rootT.InverseTransformPoint(RendererCenter(wheel)).z < bounds.center.z)
            {
                m.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                bounds = ModelBounds(model, rootT);
            }

            // Centre on the root and sit the tyres on the ground.
            m.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            bounds = ModelBounds(model, rootT);

            var steering = SetupSteeringPivot(wheel, rootT, out var axis);
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
            var driver = root.GetComponent<DriverController>();
            var so = new SerializedObject(driver);
            so.FindProperty("m_EyePoint").objectReferenceValue = eye;
            so.FindProperty("m_SteeringWheel").objectReferenceValue = steering;
            so.FindProperty("m_SteeringAxis").vector3Value = axis;
            so.FindProperty("m_Length").floatValue = bounds.size.z;
            // Body width for the collision footprint: the measured bounds include the wing mirrors.
            so.FindProperty("m_Width").floatValue = Mathf.Min(bounds.size.x, 1.85f);
            so.FindProperty("m_Wheelbase").floatValue = wheelbase;
            // The greybox lamp cubes are hidden; the model's own lamps are wired as material slots instead.
            so.FindProperty("m_BrakeLights").arraySize = 0;
            so.FindProperty("m_LeftIndicators").arraySize = 0;
            so.FindProperty("m_RightIndicators").arraySize = 0;
            var lamps = WireLamps(model, rootT, so);
            so.ApplyModifiedPropertiesWithoutUndo();

            var gauges = WireGauges(root, m, eye);
            var controls = BuildControls(root, steering, axis, hub);

            if (hub.x > 0f)
                Debug.LogWarning("[RoadReady] The steering wheel is on the right (right-hand drive). Rwanda drives on the right, so a left-hand-drive car is expected.");

            return $"Size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00} m, wheelbase {wheelbase:0.00} m, eye point at {eye.localPosition}. " +
                   $"Restored {restored} renderer material set(s) from the model, {lamps}, {gauges}, {controls}.";
        }

        // ================================================================ hand controls

        /// <summary>
        /// Grabbable steering wheel (a grab zone round the rim) and the model's own gear lever and handbrake.
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

            // The model's own gear lever and handbrake, cut out of the body mesh so they can move.
            var cockpit = CockpitControlsKit.Build(rootT, rootT.Find(k_ModelName), driver);
            return $"wheel grab (lock {lockDegrees:0}°), {cockpit}";
        }

        // ================================================================ materials from the model

        /// <summary>
        /// The pack prefab has scrambled material slots (the body uses the gauge texture everywhere, which is what
        /// made the windscreen grey). The FBX finds the right materials by name, so copy them from there.
        /// </summary>
        static int RestoreMaterialsFromModel(GameObject model)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelFile);
            if (fbx == null)
            {
                Debug.LogWarning($"[RoadReady] Model file not found at {k_ModelFile}; materials left as in the prefab.");
                return 0;
            }

            var source = fbx.GetComponentsInChildren<Renderer>(true)
                .GroupBy(r => r.name)
                .ToDictionary(g => g.Key, g => g.ToList());
            var count = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!source.TryGetValue(r.name, out var candidates))
                    continue;
                var match = candidates.FirstOrDefault(c => c.sharedMaterials.Length == r.sharedMaterials.Length && c.sharedMaterials.All(mat => mat != null));
                if (match == null || match.sharedMaterials.SequenceEqual(r.sharedMaterials))
                    continue;
                r.sharedMaterials = match.sharedMaterials;
                count++;
            }

            return count;
        }

        /// <summary>Clear glass, back-lit gauges and needles, and lamps that start switched off.</summary>
        static void PolishMaterials(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder + "/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit")
                    continue;
                var n = mat.name;

                if (n.StartsWith("Glass"))
                {
                    MakeTransparent(mat);
                    mat.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f, 0.06f));
                    mat.SetFloat("_Smoothness", 0.2f);
                    mat.SetFloat("_Cull", (float)CullMode.Off);
                    // Reflections and highlights are not faded by alpha and turned the windscreen grey.
                    mat.SetFloat("_BlendModePreserveSpecular", 0f);
                    mat.SetFloat("_EnvironmentReflections", 0f);
                    mat.SetFloat("_SpecularHighlights", 0f);
                    mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                else if (n == "_Dashboard" || n.StartsWith("Gauges"))
                {
                    Glow(mat, Color.white * 1.2f);
                }
                else if (n.StartsWith("Needle_Color") || n.StartsWith("Digital_Needle"))
                {
                    Glow(mat, mat.GetColor("_BaseColor") * 1.5f);
                }
                else if (n.StartsWith("Dashboard_Icon") || n == "Left_Indicator" || n == "Right_Indicator")
                {
                    Glow(mat, Color.black); // off until the car switches it on
                }
                else if (n == "TailLight")
                {
                    Glow(mat, new Color(0.6f, 0.02f, 0.02f)); // dim running light; brighter while braking
                }
                else
                {
                    continue;
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Emission using the base texture, so printed dials and icons light up like a back-lit cluster.</summary>
        static void Glow(Material mat, Color emission)
        {
            mat.SetTexture("_EmissionMap", mat.GetTexture("_BaseMap"));
            mat.SetColor("_EmissionColor", emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // ================================================================ lamps & gauges

        static string WireLamps(GameObject model, Transform root, SerializedObject driver)
        {
            var brake = new List<(Renderer, int, Color)>();
            var left = new List<(Renderer, int, Color)>();
            var right = new List<(Renderer, int, Color)>();
            var turnIcons = new List<Renderer>();
            Renderer hazardIcon = null;

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;
                    if (mat.name == "TailLight") brake.Add((r, i, mat.GetColor("_EmissionColor")));
                    else if (mat.name == "Left_Indicator") left.Add((r, i, Color.black));
                    else if (mat.name == "Right_Indicator") right.Add((r, i, Color.black));
                    else if (mat.name.StartsWith("Dashboard_Icon"))
                    {
                        var tex = mat.GetTexture("_BaseMap");
                        if (tex != null && tex.name.Contains("Turn_indicator")) turnIcons.Add(r);
                        else if (tex != null && tex.name.Contains("Hazard")) hazardIcon = r;
                    }
                }
            }

            // Dashboard arrows: the one left of the hazard triangle is the left indicator.
            var middle = hazardIcon != null
                ? root.InverseTransformPoint(hazardIcon.bounds.center).x
                : turnIcons.Count > 0 ? turnIcons.Average(r => root.InverseTransformPoint(r.bounds.center).x) : 0f;
            foreach (var icon in turnIcons)
                (root.InverseTransformPoint(icon.bounds.center).x < middle ? left : right).Add((icon, 0, Color.black));

            WriteSlots(driver.FindProperty("m_BrakeLampSlots"), brake);
            WriteSlots(driver.FindProperty("m_LeftIndicatorSlots"), left);
            WriteSlots(driver.FindProperty("m_RightIndicatorSlots"), right);
            return $"{brake.Count} brake / {left.Count} left / {right.Count} right lamp slot(s) incl. {turnIcons.Count} dashboard arrow(s)";
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

        /// <summary>
        /// Four needles: the two lower ones sit on the big dials (speedometer left, rev counter right, as printed on
        /// the gauge texture), the two upper ones on the small fuel (left) and temperature (right) dials.
        /// </summary>
        static string WireGauges(GameObject root, Transform model, Transform eye)
        {
            var needles = model.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Needle_#"))
                .Select(t => (t, p: root.transform.InverseTransformPoint(t.position)))
                .ToList();
            // Unity's fake-null means ?? cannot be used with components.
            var gauges = root.GetComponent<DashboardGauges>();
            if (gauges == null)
                gauges = root.AddComponent<DashboardGauges>();
            if (needles.Count < 4)
            {
                gauges.Needles = null;
                return $"{needles.Count} needle(s) found - gauges not wired";
            }

            var byHeight = needles.OrderBy(n => n.p.y).ToList();
            var big = byHeight.Take(2).OrderBy(n => n.p.x).ToList();
            var small = byHeight.Skip(2).Take(2).OrderBy(n => n.p.x).ToList();

            DashboardGauges.Needle Make(Transform t, DashboardGauges.Source source, float max, float sweep, float fixedValue = 0f) => new DashboardGauges.Needle
            {
                needle = t,
                source = source,
                axis = DialAxis(t, eye),
                maxValue = max,
                degreesAtMax = sweep,
                fixedValue = fixedValue,
            };

            gauges.Needles = new[]
            {
                Make(big[0].t, DashboardGauges.Source.Speed, 260f, 260f),
                Make(big[1].t, DashboardGauges.Source.Rpm, 8000f, 260f),
                Make(small[0].t, DashboardGauges.Source.Fixed, 1f, 90f, 0.7f),  // fuel: 70 %
                Make(small[1].t, DashboardGauges.Source.Fixed, 1f, 90f, 0.5f),  // temperature: normal
            };
            return $"gauges: speed={big[0].t.name}, rpm={big[1].t.name}, fuel={small[0].t.name}, temp={small[1].t.name}";
        }

        /// <summary>The needle's local axis pointing at the driver, snapped to the nearest clean axis of the model.</summary>
        static Vector3 DialAxis(Transform needle, Transform eye)
        {
            var toEye = needle.InverseTransformDirection((eye.position - needle.position).normalized);
            var best = Vector3.forward;
            var bestDot = -1f;
            foreach (var axis in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var dot = Vector3.Dot(axis, toEye);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = axis;
                }
            }

            return bestDot > 0.8f ? best : toEye;
        }

        /// <summary>
        /// The DriverController spins the steering transform around one local axis, keeping its rest rotation.
        /// If the wheel mesh is not pivoted at its hub, a pivot is inserted at the hub so it spins in place.
        /// </summary>
        static Transform SetupSteeringPivot(Transform wheel, Transform root, out Vector3 axis)
        {
            var filter = wheel.GetComponentInChildren<MeshFilter>();
            var meshBounds = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

            // A steering wheel is a thin disc: its spin axis is the mesh's thinnest dimension.
            var s = meshBounds.size;
            var local = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
            var meshT = filter != null ? filter.transform : wheel;
            var worldAxis = meshT.TransformDirection(local);

            var hubWorld = meshT.TransformPoint(meshBounds.center);
            Transform steering = wheel;
            if (Vector3.Distance(hubWorld, wheel.position) > 0.02f || meshT != wheel)
            {
                var pivot = new GameObject("SteeringWheel Pivot").transform;
                pivot.SetParent(wheel.parent, false);
                pivot.SetPositionAndRotation(hubWorld, meshT.rotation);
                wheel.SetParent(pivot, true);
                steering = pivot;
            }

            // Match the greybox convention: the axis points forward along the car.
            axis = steering.InverseTransformDirection(worldAxis).normalized;
            if (Vector3.Dot(worldAxis, root.forward) < 0f)
                axis = -axis;
            return steering;
        }

        static float Wheelbase(Transform model, Transform root, float length)
        {
            var front = FindDeep(model, "Wheel_FL_LOD0");
            var rear = FindDeep(model, "Wheel_RL_LOD0");
            if (front == null || rear == null)
                return length * 0.6f;
            return Mathf.Abs(root.InverseTransformPoint(RendererCenter(front)).z - root.InverseTransformPoint(RendererCenter(rear)).z);
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
                if (!r.name.Contains("_LOD") || r.name.EndsWith("_LOD0"))
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
