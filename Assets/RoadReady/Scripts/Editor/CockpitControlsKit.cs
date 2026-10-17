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
    /// Makes the car model's own gear lever and handbrake work. Both are modelled into the body mesh, so their mesh
    /// islands are cut out into separate pivoting objects (the body keeps everything else), given grab colliders and
    /// wired to <see cref="GearShifter"/> / <see cref="HandbrakeLever"/>. P-R-N-D letters are laid on the console
    /// beside the gear lever. The island filters are for the PolyDrive Bavaria interior.
    /// </summary>
    static class CockpitControlsKit
    {
        public const string ShifterName = "Gear Shifter";
        public const string HandbrakeName = "Handbrake";
        const string k_MeshFolder = GeneratedRoot + "/Meshes";
        const string k_Font = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        static readonly string[] k_Letters = { "P", "R", "N", "D" };
        // Lever tilt per gear in degrees (positive = knob forward), P at the front as on a real automatic.
        static readonly float[] k_Detents = { 16f, 5.5f, -5.5f, -16f };
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

        public static string Build(Transform root, Transform model, DriverController driver)
        {
            var body = model.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f =>
                f.name.EndsWith("_Body_LOD0") && f.TryGetComponent<MeshRenderer>(out var r) && r.sharedMaterials.Length > 1 && r.sharedMaterials.All(m => m != null));
            if (body == null)
            {
                Debug.LogWarning("[RoadReady] Car body mesh not found; the gear lever and handbrake stay fixed (G / B keys still work).");
                return "no cockpit controls";
            }

            var islands = Islands(body, root);
            // Gear lever: knob and stick (with its boot) above the console ring, on the car's centre line.
            var gear = islands.Where(i => Mathf.Abs(i.bounds.center.x) < 0.06f && i.bounds.center.z > 0.15f && i.bounds.center.z < 0.4f
                                          && i.bounds.center.y > 0.66f && i.bounds.center.y < 0.8f && i.tris.Count > 50).ToList();
            // Handbrake: the long lever between the front seats, just left of the centre line.
            var brake = islands.Where(i => i.bounds.center.x < -0.02f && i.bounds.center.x > -0.12f && i.bounds.center.z > -0.3f && i.bounds.center.z < 0.1f
                                           && i.bounds.center.y > 0.55f && i.bounds.center.y < 0.65f && i.tris.Count > 50).ToList();

            var report = new List<string>();
            var renderer = body.GetComponent<MeshRenderer>();
            if (gear.Count > 0)
                report.Add(BuildGearLever(root, body, renderer, gear, driver));
            else
                Debug.LogWarning("[RoadReady] Gear lever not found in the car body mesh (G key still shifts).");
            if (brake.Count > 0)
                report.Add(BuildHandbrake(root, body, renderer, brake, driver));
            else
                Debug.LogWarning($"[RoadReady] Handbrake not found in {body.name} ({body.sharedMesh.name}, {islands.Count} islands, body pos {root.InverseTransformPoint(body.transform.position):F3}). Candidates: " +
                                 string.Join("; ", islands.Where(i => Mathf.Abs(i.bounds.center.x) < 0.3f && i.tris.Count > 100 && i.bounds.size.magnitude < 0.6f)
                                     .Select(i => $"{i.tris.Count} tris c={i.bounds.center:F3} s={i.bounds.size:F3}")));

            // The body keeps everything except the parts that now move.
            body.sharedMesh = SaveMesh(WithoutIslands(body.sharedMesh, gear.Concat(brake)), "PlayerCar_Body_Cockpit");
            return string.Join(", ", report);
        }

        // ------------------------------------------------------------------ gear lever

        static string BuildGearLever(Transform root, MeshFilter body, MeshRenderer bodyRenderer, List<Island> parts, DriverController driver)
        {
            var b = Encapsulate(parts);
            var shifter = new GameObject(ShifterName).transform;
            shifter.SetParent(root, false);
            shifter.localPosition = new Vector3(b.center.x, b.min.y, b.center.z);
            var lever = Group(shifter, "Lever");
            var visual = Group(lever, "Gear Lever (model)");
            AddPartMesh(visual, body, bodyRenderer, parts, "PlayerCar_GearLever");

            // Grab zone round the knob (the topmost island), a little larger than the knob so it is easy to catch.
            var knob = parts.OrderByDescending(p => p.bounds.center.y).First();
            var grab = Group(lever, "Knob Grab");
            grab.position = root.TransformPoint(knob.bounds.center);
            grab.gameObject.AddComponent<SphereCollider>().radius = 0.07f;

            // P R N D on the console on the driver's side of the lever, each where the knob sits in that gear.
            var height = b.size.y;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_Font);
            Box(shifter, "PRND plate", new Vector3(-0.062f, 0.003f, 0f), new Vector3(0.028f, 0.006f, 0.105f), "housing");
            var labels = new TMP_Text[k_Letters.Length];
            for (var i = 0; i < k_Letters.Length; i++)
            {
                var label = new GameObject("Label " + k_Letters[i]);
                label.transform.SetParent(shifter, false);
                label.transform.localPosition = new Vector3(-0.062f, 0.0075f, height * Mathf.Sin(k_Detents[i] * Mathf.Deg2Rad));
                label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat, reading from the seat
                var text = label.AddComponent<TextMeshPro>();
                if (font != null) text.font = font;
                text.text = k_Letters[i];
                text.fontSize = 0.17f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(0.03f, 0.02f);
                text.color = new Color(0.5f, 0.53f, 0.56f);
                labels[i] = text;
            }

            var shifterComponent = shifter.gameObject.AddComponent<GearShifter>();
            shifterComponent.Configure(driver, lever, k_Detents.ToArray(), labels);
            return $"model gear lever at {shifter.localPosition:F2} with P-R-N-D labels";
        }

        // ------------------------------------------------------------------ handbrake

        static string BuildHandbrake(Transform root, MeshFilter body, MeshRenderer bodyRenderer, List<Island> parts, DriverController driver)
        {
            var b = Encapsulate(parts);
            var handbrake = new GameObject(HandbrakeName).transform;
            handbrake.SetParent(root, false);
            // Pivot at the rear, low: the handle at the front end lifts when pulled.
            handbrake.localPosition = new Vector3(b.center.x, b.min.y + 0.02f, b.min.z + 0.02f);
            var lever = Group(handbrake, "Lever");
            var visual = Group(lever, "Handbrake Lever (model)");
            AddPartMesh(visual, body, bodyRenderer, parts, "PlayerCar_Handbrake");

            var grab = Group(lever, "Handle Grab");
            grab.position = root.TransformPoint(b.center);
            var box = grab.gameObject.AddComponent<BoxCollider>();
            box.size = b.size + new Vector3(0.06f, 0.08f, 0.04f);

            handbrake.gameObject.AddComponent<HandbrakeLever>().Configure(driver, lever, k_HandbrakeTravel);
            return $"model handbrake at {handbrake.localPosition:F2}";
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
