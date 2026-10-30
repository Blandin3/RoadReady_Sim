using System.Collections.Generic;
using System.Linq;
using RoadReady.Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Makes the car model's steering wheel, gear lever and handbrake work. The BMW interior is a single mesh, so the
    /// wheel and the shifter knob are cut out of it by region into separate pivoting objects (the cabin keeps everything
    /// else), given grab colliders and wired to <see cref="SteeringWheelGrab"/> / <see cref="GearShifter"/>. The handbrake is
    /// a simple lever (the car has an electronic one) wired to <see cref="HandbrakeLever"/>. P-R-N-D letters are laid on
    /// the console beside the gear lever.
    /// </summary>
    static class CockpitControlsKit
    {
        public const string ShifterName = "Gear Shifter";
        public const string HandbrakeName = "Handbrake";
        const string k_MeshFolder = GeneratedRoot + "/Meshes";
        const string k_Font = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // Lever tilt per gear in degrees (positive = knob forward), in the order P, R, N, D. The Bentley engraves its
        // positions front to back as R, N, P, D.
        static readonly float[] k_Detents = { -5.5f, 16f, 5.5f, -16f };
        const float k_HandbrakeTravel = 18f;

        struct Tri
        {
            public int sub, a, b, c;
        }

        class Island
        {
            public readonly List<Tri> tris = new List<Tri>();
            public Bounds bounds;
        }

        // The interior is one big welded mesh (plus loose pieces), so its moving parts are cut out of it: the steering
        // wheel by region, the gear lever and the ENGINE START STOP button as pieces of their own. Coordinates are in
        // the mesh's own space (metres, front = +Z, left-hand drive); measured on the Bentley.
        static readonly Vector3 k_WheelHub = new Vector3(-0.39f, 0.87f, 0.358f);
        static readonly Vector3 k_WheelToDriver = new Vector3(0f, 0.328f, -0.945f).normalized; // steering column, pointing at the driver
        const float k_WheelRadius = 0.21f;
        const float k_WheelFront = 0.07f; // how far in front of the rim plane the hub boss reaches
        const float k_WheelBack = 0.06f;  // and behind it (airbag cover, paddles)
        static readonly Vector3 k_Lever = new Vector3(0f, 0.755f, 0.273f);       // centre of the gear lever piece
        static readonly Vector3 k_StartButton = new Vector3(0f, 0.687f, 0.184f); // the ENGINE START STOP button
        static readonly Vector3 k_ExitSpot = new Vector3(-0.69f, 0.80f, 0.02f);  // on the driver's door panel
        const float k_ShifterDrop = 0f;
        static readonly Vector2 k_HandbrakeSpot = new Vector2(0f, -0.14f); // x, z of the lever's pivot on the centre console

        public const string StartButtonName = "Engine Start Button";
        public const string ExitButtonName = "Exit Button";

        public static string Build(Transform root, Transform model, DriverController driver, out Transform steering, out Vector3 steeringAxis)
        {
            var body = model.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name.Contains("Interior") && f.sharedMesh != null)
                .OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
            if (body == null)
                throw new System.InvalidOperationException("[RoadReady] No interior mesh in the car model.");

            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { CabinMaterial(renderer.sharedMaterial) };

            var wheel = Region(body, root, c =>
            {
                var d = c - k_WheelHub;
                var along = Vector3.Dot(d, k_WheelToDriver);
                return d.magnitude <= k_WheelRadius && along >= -k_WheelFront && along <= k_WheelBack;
            });

            var islands = Islands(body, root);
            Vector3 ToRoot(Vector3 p) => root.InverseTransformPoint(body.transform.TransformPoint(p));
            var lever = islands.Where(i => i.tris.Count > 300 && i.bounds.size.magnitude < 0.3f)
                .OrderBy(i => Vector3.Distance(i.bounds.center, ToRoot(k_Lever))).FirstOrDefault();
            if (lever != null && Vector3.Distance(lever.bounds.center, ToRoot(k_Lever)) > 0.03f)
                lever = null;
            var buttonParts = islands.Where(i => i.tris.Count >= 20 && i.bounds.size.magnitude < 0.1f
                                                 && Vector3.Distance(i.bounds.center, ToRoot(k_StartButton)) < 0.025f).ToList();

            var report = new List<string>();
            if (wheel.tris.Count == 0)
                throw new System.InvalidOperationException("[RoadReady] Steering wheel not found in the interior mesh.");
            steering = BuildSteeringWheel(root, body, renderer, wheel, out steeringAxis);
            report.Add($"steering wheel cut out ({wheel.tris.Count} triangles)");

            var cut = new List<Island> { wheel };
            if (lever != null)
            {
                report.Add(BuildGearLever(root, body, renderer, new List<Island> { lever }, driver));
                cut.Add(lever);
            }
            else
            {
                Debug.LogWarning("[RoadReady] Gear lever not found in the interior mesh (G key still shifts).");
            }

            if (buttonParts.Count > 0)
            {
                report.Add(BuildStartButton(root, body, renderer, buttonParts, driver));
                cut.AddRange(buttonParts);
            }
            else
            {
                Debug.LogWarning("[RoadReady] ENGINE START STOP button not found in the interior mesh (O key still starts the engine).");
            }

            report.Add(BuildHandbrake(root, body, driver));
            report.Add(BuildExitButton(root, body));

            // The cabin keeps everything except the parts that now move.
            body.sharedMesh = SaveMesh(WithoutIslands(body.sharedMesh, cut), "PlayerCar_Cabin");
            return string.Join(", ", report);
        }

        /// <summary>Triangles of the body mesh whose centre (mesh space) satisfies the test; bounds are in the root's space.</summary>
        static Island Region(MeshFilter body, Transform root, System.Func<Vector3, bool> contains)
        {
            var mesh = body.sharedMesh;
            var vertices = mesh.vertices;
            var island = new Island();
            var first = true;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (var i = 0; i < t.Length; i += 3)
                {
                    var a = vertices[t[i]];
                    var b = vertices[t[i + 1]];
                    var c = vertices[t[i + 2]];
                    if (!contains((a + b + c) / 3f))
                        continue;
                    island.tris.Add(new Tri { sub = s, a = t[i], b = t[i + 1], c = t[i + 2] });
                    foreach (var v in new[] { a, b, c })
                    {
                        var p = root.InverseTransformPoint(body.transform.TransformPoint(v));
                        if (first)
                        {
                            island.bounds = new Bounds(p, Vector3.zero);
                            first = false;
                        }
                        else
                        {
                            island.bounds.Encapsulate(p);
                        }
                    }
                }
            }

            return island;
        }

        /// <summary>Highest surface below head height at the given spot (mesh space): the console top the handbrake stands on.</summary>
        static float SurfaceY(MeshFilter body, float x, float z, float halfSize)
        {
            var best = float.MinValue;
            foreach (var v in body.sharedMesh.vertices)
                if (Mathf.Abs(v.x - x) < halfSize && Mathf.Abs(v.z - z) < halfSize && v.y < 0.85f)
                    best = Mathf.Max(best, v.y);
            return best > float.MinValue ? best : 0.7f;
        }

        // ------------------------------------------------------------------ steering wheel

        /// <summary>
        /// The wheel becomes a pivot on the steering column: its local Z is the column axis pointing away from the
        /// driver, which is what the DriverController and the hand grab spin it around.
        /// </summary>
        static Transform BuildSteeringWheel(Transform root, MeshFilter body, MeshRenderer bodyRenderer, Island wheel, out Vector3 axis)
        {
            var hub = root.InverseTransformPoint(body.transform.TransformPoint(k_WheelHub));
            var toDriver = root.InverseTransformDirection(body.transform.TransformDirection(k_WheelToDriver)).normalized;
            var pivot = new GameObject("SteeringWheel Pivot").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = hub;
            pivot.localRotation = Quaternion.LookRotation(-toDriver, Vector3.up);
            var visual = Group(pivot, "Steering Wheel (model)");
            AddPartMesh(visual, body, bodyRenderer, new List<Island> { wheel }, "PlayerCar_SteeringWheel");
            axis = Vector3.forward;
            return pivot;
        }

        // ------------------------------------------------------------------ cabin material

        /// <summary>
        /// A copy of the interior material that can be tuned without touching the imported model.
        /// </summary>
        static Material CabinMaterial(Material source)
        {
            var path = PrefabsFolder + "/PlayerCar_Cabin.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = "PlayerCar_Cabin" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.CopyPropertiesFromMaterial(source);
            // The model's emissive map is a colour map: it floods the whole cabin blue, so the cabin stays unlit.
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }


        // ------------------------------------------------------------------ gear lever

        static string BuildGearLever(Transform root, MeshFilter body, MeshRenderer bodyRenderer, List<Island> parts, DriverController driver)
        {
            var b = Encapsulate(parts);
            var shifter = new GameObject(ShifterName).transform;
            shifter.SetParent(root, false);
            shifter.localPosition = new Vector3(b.center.x, b.min.y - k_ShifterDrop, b.center.z);
            var lever = Group(shifter, "Lever");
            var visual = Group(lever, "Gear Lever (model)");
            AddPartMesh(visual, body, bodyRenderer, parts, "PlayerCar_GearLever");

            // Grab zone round the knob (the topmost island), a little larger than the knob so it is easy to catch.
            var knob = parts.OrderByDescending(p => p.bounds.center.y).First();
            var grab = Group(lever, "Knob Grab");
            grab.position = root.TransformPoint(knob.bounds.center);
            grab.gameObject.AddComponent<SphereCollider>().radius = 0.07f;


            var shifterComponent = shifter.gameObject.AddComponent<GearShifter>();
            // The car's own gear markings (engraved on the console) are used: no labels of ours.
            shifterComponent.Configure(driver, lever, k_Detents.ToArray(), new TMP_Text[0]);
            BuildGearLetters(shifter, driver);
            return $"model gear lever at {shifter.localPosition:F2} (car's own R-N-P-D layout, engaged letter lit yellow)";
        }

        // Where the car's engraved letters sit on the console, measured in the shifter's frame (metres, x right, z forward),
        // in the order P, R, N, D. A yellow copy of the engaged letter is laid over the engraving.
        static readonly string[] k_Letters = { "P", "R", "N", "D" };
        // x, height above the shifter's base (the console slopes down towards D; 1.5 mm above the surface), z.
        static readonly Vector3[] k_LetterSpots =
        {
            new Vector3(-0.0325f, 0.0145f, 0.0052f), new Vector3(-0.0315f, 0.0232f, 0.0295f), new Vector3(-0.0315f, 0.0175f, 0.0135f), new Vector3(-0.0345f, 0.0120f, -0.0017f),
        };

        static void BuildGearLetters(Transform shifter, DriverController driver)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_Font);
            var path = PrefabsFolder + "/PlayerCar_GearLetter.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null && font != null)
            {
                material = new Material(font.material) { name = "PlayerCar_GearLetter" };
                // The mobile variant cannot ignore depth; this one can, so the letter is never sunk into the console.
                var shader = Shader.Find("TextMeshPro/Distance Field");
                if (shader != null)
                    material.shader = shader;
                material.SetColor("_FaceColor", new Color(1f, 0.93f, 0.1f));
                if (material.HasProperty("_ZTestMode"))
                    material.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                AssetDatabase.CreateAsset(material, path);
            }

            var group = Group(shifter, "Gear Letters");
            var markers = new GameObject[k_Letters.Length];
            for (var i = 0; i < k_Letters.Length; i++)
            {
                var marker = new GameObject("Lit " + k_Letters[i]);
                marker.transform.SetParent(group, false);
                marker.transform.localPosition = k_LetterSpots[i];
                marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat, reading from the seat
                var text = marker.AddComponent<TextMeshPro>();
                if (font != null) text.font = font;
                if (material != null) text.fontSharedMaterial = material;
                text.text = k_Letters[i];
                text.fontSize = 0.06f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.color = new Color(1f, 0.93f, 0.1f);
                text.rectTransform.sizeDelta = new Vector2(0.012f, 0.008f);
                marker.SetActive(i == (int)Core.Gear.Park); // the car starts in P; GearIndicator follows the gear from then on
                markers[i] = marker;
            }

            group.gameObject.AddComponent<GearIndicator>().Configure(driver, markers);
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>
        /// The car's own ENGINE START STOP button becomes pressable: its cap dips when pressed and a ring lights while
        /// the engine runs. The car does not move until it has been pressed.
        /// </summary>
        static string BuildStartButton(Transform root, MeshFilter body, MeshRenderer bodyRenderer, List<Island> parts, DriverController driver)
        {
            var b = Encapsulate(parts);
            var button = new GameObject(StartButtonName).transform;
            button.SetParent(root, false);
            button.localPosition = new Vector3(b.center.x, b.min.y, b.center.z);
            var cap = Group(button, "Cap");
            AddPartMesh(cap, body, bodyRenderer, parts, "PlayerCar_StartButton");

            // A solid grab zone a little larger than the cap: XRI's near touch ignores triggers.
            var zone = button.gameObject.AddComponent<SphereCollider>();
            zone.center = new Vector3(0f, b.size.y * 0.6f, 0f);
            zone.radius = 0.04f;

            // The ring round the cap lights green while the engine is on.
            var radius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            var ring = new GameObject("Lamp ring");
            ring.transform.SetParent(button, false);
            ring.transform.localPosition = new Vector3(0f, b.size.y + 0.0015f, 0f);
            ring.AddComponent<MeshFilter>().sharedMesh = RingMesh(radius + 0.001f, radius + 0.006f);
            var lamp = ring.AddComponent<MeshRenderer>();
            lamp.sharedMaterial = LampMaterial();
            lamp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var carButton = button.gameObject.AddComponent<CarButton>();
            carButton.Configure(lamp, 0, cap);
            // A yellow arrow hovering over the button while the engine is off, so the driver sees it on getting in.
            var pointer = new GameObject("ON pointer");
            pointer.transform.SetParent(button, false);
            pointer.transform.localPosition = new Vector3(0f, b.size.y + 0.05f, 0f);
            pointer.transform.localScale = Vector3.one * 1.3f;
            pointer.AddComponent<MeshFilter>().sharedMesh = PointerMesh();
            var pointerRenderer = pointer.AddComponent<MeshRenderer>();
            pointerRenderer.sharedMaterial = PointerMaterial();
            pointerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            button.gameObject.AddComponent<EngineStartButton>().Configure(driver, pointer.transform);
            return $"ON button (engine start) at {button.localPosition:F2}";
        }

        /// <summary>A red round button with EXIT on the driver's door panel. Pressing it asks to leave the car.</summary>
        static string BuildExitButton(Transform root, MeshFilter body)
        {
            var exit = new GameObject(ExitButtonName).transform;
            exit.SetParent(root, false);
            exit.localPosition = root.InverseTransformPoint(body.transform.TransformPoint(k_ExitSpot));

            // The button faces into the cabin (+X).
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.name = "Button plate";
            plate.transform.SetParent(exit, false);
            plate.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            plate.transform.localScale = new Vector3(0.05f, 0.006f, 0.05f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = Mat("car_red");
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.name = "Button rim";
            rim.transform.SetParent(exit, false);
            rim.transform.localPosition = new Vector3(-0.004f, 0f, 0f);
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rim.transform.localScale = new Vector3(0.062f, 0.004f, 0.062f);
            rim.GetComponent<MeshRenderer>().sharedMaterial = Mat("housing");

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_Font);
            var label = new GameObject("Label EXIT");
            label.transform.SetParent(exit, false);
            label.transform.localPosition = new Vector3(0.0045f, 0f, 0f);
            label.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); // reads from the seat
            var text = label.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.text = "EXIT";
            text.fontSize = 0.22f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(0.05f, 0.03f);
            text.color = Color.white;

            var zone = exit.gameObject.AddComponent<SphereCollider>();
            zone.radius = 0.05f;
            exit.gameObject.AddComponent<CarButton>();
            exit.gameObject.AddComponent<ExitCarButton>();
            return $"exit button on the door at {exit.localPosition:F2}";
        }

        /// <summary>A downward arrow (pyramid head on a square shaft) with its tip at the origin.</summary>
        static Mesh PointerMesh()
        {
            const float headHalf = 0.024f, shaftHalf = 0.009f, headHeight = 0.034f, total = 0.085f;
            Vector3[] Ring(float half, float y) => new[]
            {
                new Vector3(-half, y, -half), new Vector3(half, y, -half), new Vector3(half, y, half), new Vector3(-half, y, half),
            };

            var vertices = new List<Vector3> { Vector3.zero }; // 0: the tip
            vertices.AddRange(Ring(headHalf, headHeight));      // 1-4: head base
            vertices.AddRange(Ring(shaftHalf, headHeight));     // 5-8: shaft bottom
            vertices.AddRange(Ring(shaftHalf, total));          // 9-12: shaft top
            var triangles = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                var n = (i + 1) % 4;
                triangles.AddRange(new[] { 0, 1 + i, 1 + n });                                       // head sides
                triangles.AddRange(new[] { 1 + i, 5 + i, 5 + n, 1 + i, 5 + n, 1 + n });              // underside of the head
                triangles.AddRange(new[] { 5 + i, 9 + i, 9 + n, 5 + i, 9 + n, 5 + n });              // shaft sides
            }

            triangles.AddRange(new[] { 9, 10, 11, 9, 11, 12 });                                       // top
            var mesh = new Mesh { name = "OnPointerArrow" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EnsureFolder(k_MeshFolder);
            var path = k_MeshFolder + "/OnPointerArrow.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material PointerMaterial()
        {
            var path = PrefabsFolder + "/PlayerCar_OnPointer.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PlayerCar_OnPointer" };
            material.SetColor("_BaseColor", new Color(1f, 0.82f, 0.05f));
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material LampMaterial()
        {
            var path = PrefabsFolder + "/PlayerCar_StartLamp.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlayerCar_StartLamp" };
            material.SetColor("_BaseColor", new Color(0.2f, 1f, 0.35f) * 0.25f);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>A flat ring (annulus) facing up, visible from both sides.</summary>
        static Mesh RingMesh(float inner, float outer)
        {
            const int segments = 40;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner));
                vertices.Add(new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer));
            }

            for (var i = 0; i < segments; i++)
            {
                var n = (i + 1) % segments;
                int a0 = i * 2, b0 = i * 2 + 1, a1 = n * 2, b1 = n * 2 + 1;
                triangles.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                triangles.AddRange(new[] { a0, a1, b0, a1, b1, b0 });
            }

            var mesh = new Mesh { name = "StartButtonRing" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            EnsureFolder(k_MeshFolder);
            var path = k_MeshFolder + "/StartButtonRing.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ handbrake

        /// <summary>
        /// The BMW has an electronic parking brake and no lever, so a simple lever stands on the centre console. It is
        /// pivoted at its rear end: the handle lifts when the brake is applied.
        /// </summary>
        static string BuildHandbrake(Transform root, MeshFilter body, DriverController driver)
        {
            var surface = SurfaceY(body, k_HandbrakeSpot.x, k_HandbrakeSpot.y, 0.05f);
            var handbrake = new GameObject(HandbrakeName).transform;
            handbrake.SetParent(root, false);
            handbrake.localPosition = root.InverseTransformPoint(body.transform.TransformPoint(new Vector3(k_HandbrakeSpot.x, surface, k_HandbrakeSpot.y)));
            Box(handbrake, "Base", new Vector3(0f, 0.004f, 0.06f), new Vector3(0.05f, 0.008f, 0.14f), "housing");

            var lever = Group(handbrake, "Lever");
            var visual = Group(lever, "Handbrake Lever (model)");
            Box(visual, "Shaft", new Vector3(0f, 0.014f, 0.085f), new Vector3(0.014f, 0.02f, 0.17f), "housing");
            Box(visual, "Grip", new Vector3(0f, 0.022f, 0.17f), new Vector3(0.034f, 0.034f, 0.07f), "housing");
            Box(visual, "Button", new Vector3(0f, 0.041f, 0.17f), new Vector3(0.016f, 0.004f, 0.03f), "car_red");

            var grab = Group(lever, "Handle Grab");
            grab.localPosition = new Vector3(0f, 0.025f, 0.15f);
            grab.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.09f, 0.14f);

            handbrake.gameObject.AddComponent<HandbrakeLever>().Configure(driver, lever, k_HandbrakeTravel);
            return $"handbrake lever on the console at {handbrake.localPosition:F2}";
        }


        // ------------------------------------------------------------------ mesh surgery

        /// <summary>Connected pieces of the mesh (vertices welded by position), with bounds in the root's space.</summary>
        static List<Island> Islands(MeshFilter body, Transform root)
        {
            var mesh = body.sharedMesh;
            var vertices = mesh.vertices;
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                // Weld in the car's space (0.5 mm): the mesh itself is stored at a different scale.
                var key = Vector3Int.RoundToInt(ToRoot(vertices[i]) * 2000f);
                if (!weld.TryGetValue(key, out var w))
                    weld[key] = w = weld.Count;
                id[i] = w;
            }

            var parent = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            var tris = new List<Tri>();
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (var i = 0; i < t.Length; i += 3)
                {
                    tris.Add(new Tri { sub = s, a = t[i], b = t[i + 1], c = t[i + 2] });
                    parent[Find(id[t[i]])] = Find(id[t[i + 1]]);
                    parent[Find(id[t[i + 1]])] = Find(id[t[i + 2]]);
                }
            }

            var islands = new Dictionary<int, Island>();
            foreach (var tri in tris)
            {
                var key = Find(id[tri.a]);
                if (!islands.TryGetValue(key, out var island))
                {
                    island = new Island { bounds = new Bounds(ToRoot(vertices[tri.a]), Vector3.zero) };
                    islands[key] = island;
                }

                island.tris.Add(tri);
                island.bounds.Encapsulate(ToRoot(vertices[tri.a]));
                island.bounds.Encapsulate(ToRoot(vertices[tri.b]));
                island.bounds.Encapsulate(ToRoot(vertices[tri.c]));
            }

            return islands.Values.ToList();

            Vector3 ToRoot(Vector3 v) => root.InverseTransformPoint(body.transform.TransformPoint(v));
        }

        static Mesh WithoutIslands(Mesh source, IEnumerable<Island> removed)
        {
            var cut = new HashSet<(int, int, int, int)>(removed.SelectMany(i => i.tris).Select(t => (t.sub, t.a, t.b, t.c)));
            var mesh = Object.Instantiate(source);
            for (var s = 0; s < source.subMeshCount; s++)
            {
                var t = source.GetTriangles(s);
                var keep = new List<int>(t.Length);
                for (var i = 0; i < t.Length; i += 3)
                    if (!cut.Contains((s, t[i], t[i + 1], t[i + 2])))
                        keep.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
                mesh.SetTriangles(keep, s);
            }

            return mesh;
        }

        /// <summary>Copies the islands into a mesh in <paramref name="target"/>'s space, keeping their materials.</summary>
        static void AddPartMesh(Transform target, MeshFilter body, MeshRenderer bodyRenderer, List<Island> parts, string assetName)
        {
            var source = body.sharedMesh;
            var vertices = source.vertices;
            var normals = source.normals;
            var uvs = source.uv;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var groups = parts.SelectMany(p => p.tris).GroupBy(t => t.sub).OrderBy(g => g.Key).ToList();

            int Map(int index)
            {
                if (map.TryGetValue(index, out var mapped))
                    return mapped;
                mapped = v.Count;
                map[index] = mapped;
                v.Add(target.InverseTransformPoint(body.transform.TransformPoint(vertices[index])));
                n.Add(normals.Length > index ? target.InverseTransformDirection(body.transform.TransformDirection(normals[index])).normalized : Vector3.up);
                uv.Add(uvs.Length > index ? uvs[index] : Vector2.zero);
                return mapped;
            }

            var triangles = groups.Select(g => g.SelectMany(t => new[] { Map(t.a), Map(t.b), Map(t.c) }).ToList()).ToList();
            var mesh = new Mesh { name = assetName };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = triangles.Count;
            for (var s = 0; s < triangles.Count; s++)
                mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateBounds();

            target.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, assetName);
            target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = groups.Select(g => bodyRenderer.sharedMaterials[g.Key]).ToArray();
        }

        static Mesh SaveMesh(Mesh mesh, string assetName)
        {
            EnsureFolder(k_MeshFolder);
            var path = $"{k_MeshFolder}/{assetName}.asset";
            AssetDatabase.DeleteAsset(path);
            mesh.name = assetName;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Bounds Encapsulate(List<Island> islands)
        {
            var b = islands[0].bounds;
            foreach (var i in islands)
                b.Encapsulate(i.bounds);
            return b;
        }
    }
}

