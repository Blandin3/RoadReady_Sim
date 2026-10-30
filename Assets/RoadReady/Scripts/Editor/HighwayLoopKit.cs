using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Reads the imported highway loop (ThirdParty/Highway/ROAD1.fbx): finds which material slot is the asphalt,
    /// the kerb and the pavement, and traces the road's centre line and width all the way round by sweeping
    /// angles from the middle of the loop. The tutorial is built on this loop.
    /// </summary>
    public static class HighwayLoopKit
    {
        public const string ModelPath = "Assets/ThirdParty/Highway/ROAD1.fbx";
        const int k_Bins = 720;

        public class SlotInfo
        {
            public int index;
            public float flatArea, verticalArea, minY, maxY;
            public Vector2 radialRange; // median inner / outer radius from the loop centre
        }

        public class Loop
        {
            public Vector3 centre;
            public List<Vector3> centreLine = new List<Vector3>(); // closed, evenly spaced
            public List<float> halfWidths = new List<float>();
            public float meanHalfWidth;
            public int roadSlot = -1;
            public List<SlotInfo> slots = new List<SlotInfo>();
            public float length;
        }

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;

        /// <summary>Analyses the mesh as placed under <paramref name="instance"/> (world space).</summary>
        public static Loop Analyse(GameObject instance)
        {
            var filter = instance.GetComponentInChildren<MeshFilter>();
            var mesh = filter.sharedMesh;
            var t = filter.transform;
            var vertices = mesh.vertices.Select(v => t.TransformPoint(v)).ToArray();
            var loop = new Loop();

            var all = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices) all.Encapsulate(v);
            loop.centre = new Vector3(all.center.x, 0f, all.center.z);

            // Per slot: flat vs vertical area, height range and radial spread from the centre.
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var info = new SlotInfo { index = s, minY = float.MaxValue, maxY = float.MinValue };
                var radii = new List<float>();
                var tris = mesh.GetTriangles(s);
                for (var i = 0; i < tris.Length; i += 3)
                {
                    var a = vertices[tris[i]];
                    var b = vertices[tris[i + 1]];
                    var c = vertices[tris[i + 2]];
                    var n = Vector3.Cross(b - a, c - a);
                    var area = n.magnitude * 0.5f;
                    if (area < 1e-6f) continue;
                    if (Mathf.Abs(n.normalized.y) > 0.8f) info.flatArea += area;
                    else info.verticalArea += area;
                    foreach (var p in new[] { a, b, c })
                    {
                        info.minY = Mathf.Min(info.minY, p.y);
                        info.maxY = Mathf.Max(info.maxY, p.y);
                    }
                }

                loop.slots.Add(info);
            }

            // The asphalt: the slot with the most flat area.
            loop.roadSlot = loop.slots.OrderByDescending(x => x.flatArea).First().index;

            // Sweep: per angle bin, the inner and outer radius of the asphalt's flat triangles.
            var inner = Enumerable.Repeat(float.MaxValue, k_Bins).ToArray();
            var outer = Enumerable.Repeat(float.MinValue, k_Bins).ToArray();
            var height = new float[k_Bins];
            var count = new int[k_Bins];
            var roadTris = mesh.GetTriangles(loop.roadSlot);
            for (var i = 0; i < roadTris.Length; i += 3)
            {
                var a = vertices[roadTris[i]];
                var b = vertices[roadTris[i + 1]];
                var c = vertices[roadTris[i + 2]];
                if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) < 0.8f) continue;
                foreach (var p in new[] { a, b, c, (a + b + c) / 3f })
                {
                    var d = p - loop.centre;
                    var angle = Mathf.Atan2(d.z, d.x);
                    var bin = Mathf.Clamp((int)((angle + Mathf.PI) / (2f * Mathf.PI) * k_Bins), 0, k_Bins - 1);
                    var r = new Vector2(d.x, d.z).magnitude;
                    inner[bin] = Mathf.Min(inner[bin], r);
                    outer[bin] = Mathf.Max(outer[bin], r);
                    height[bin] += p.y;
                    count[bin]++;
                }
            }

            // Centre line from the bins that were hit, then resampled every 2 m.
            var raw = new List<Vector3>();
            var widths = new List<float>();
            for (var bin = 0; bin < k_Bins; bin++)
            {
                if (count[bin] == 0) continue;
                var angle = (bin + 0.5f) / k_Bins * 2f * Mathf.PI - Mathf.PI;
                var r = (inner[bin] + outer[bin]) * 0.5f;
                raw.Add(loop.centre + new Vector3(Mathf.Cos(angle) * r, height[bin] / count[bin], Mathf.Sin(angle) * r));
                widths.Add((outer[bin] - inner[bin]) * 0.5f);
            }

            var smooth = Smooth(raw, 3);
            Resample(smooth, widths, 2f, loop);

            // Rays from the middle cross the road at an angle on bends, so re-centre every point by measuring straight
            // across the road (perpendicular to the direction of travel) on a 0.2 m grid of the asphalt.
            var grid = new AsphaltGrid(vertices, roadTris);
            for (var pass = 0; pass < 3; pass++)
            {
                var centred = new List<Vector3>();
                var halfWidths = new List<float>();
                for (var i = 0; i < loop.centreLine.Count; i++)
                {
                    var p = loop.centreLine[i];
                    var dir = Direction(loop, i);
                    var left = new Vector3(-dir.z, 0f, dir.x);
                    var a = grid.Reach(p, left);
                    var b = grid.Reach(p, -left);
                    if (a + b < 1f)
                    {
                        centred.Add(p);
                        halfWidths.Add(loop.halfWidths[i]);
                        continue;
                    }

                    var c = p + left * ((a - b) * 0.5f);
                    c.y = p.y;
                    centred.Add(c);
                    halfWidths.Add((a + b) * 0.5f);
                }

                var next = new Loop { centre = loop.centre, roadSlot = loop.roadSlot, slots = loop.slots };
                Resample(Smooth(centred, 2), halfWidths, 2f, next);
                loop = next;
            }

            loop.meanHalfWidth = Median(loop.halfWidths);
            return loop;
        }

        /// <summary>The asphalt's footprint rasterised on a 0.2 m grid (x/z).</summary>
        class AsphaltGrid
        {
            const float k_Cell = 0.2f;
            readonly bool[,] m_Cells;
            readonly float m_MinX, m_MinZ;

            public AsphaltGrid(Vector3[] vertices, int[] tris)
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var i in tris)
                {
                    minX = Mathf.Min(minX, vertices[i].x); maxX = Mathf.Max(maxX, vertices[i].x);
                    minZ = Mathf.Min(minZ, vertices[i].z); maxZ = Mathf.Max(maxZ, vertices[i].z);
                }

                m_MinX = minX - 1f;
                m_MinZ = minZ - 1f;
                m_Cells = new bool[Mathf.CeilToInt((maxX - minX + 2f) / k_Cell), Mathf.CeilToInt((maxZ - minZ + 2f) / k_Cell)];
                for (var t = 0; t < tris.Length; t += 3)
                {
                    Vector2 a = Flat(vertices[tris[t]]), b = Flat(vertices[tris[t + 1]]), c = Flat(vertices[tris[t + 2]]);
                    var x0 = Cell(Mathf.Min(a.x, b.x, c.x), m_MinX); var x1 = Cell(Mathf.Max(a.x, b.x, c.x), m_MinX);
                    var z0 = Cell(Mathf.Min(a.y, b.y, c.y), m_MinZ); var z1 = Cell(Mathf.Max(a.y, b.y, c.y), m_MinZ);
                    for (var x = x0; x <= x1; x++)
                    for (var z = z0; z <= z1; z++)
                        if (Inside(new Vector2(m_MinX + (x + 0.5f) * k_Cell, m_MinZ + (z + 0.5f) * k_Cell), a, b, c))
                            Set(x, z);
                }
            }

            static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
            static int Cell(float v, float min) => Mathf.FloorToInt((v - min) / k_Cell);

            void Set(int x, int z)
            {
                if (x >= 0 && z >= 0 && x < m_Cells.GetLength(0) && z < m_Cells.GetLength(1)) m_Cells[x, z] = true;
            }

            public bool IsAsphalt(Vector3 p)
            {
                int x = Cell(p.x, m_MinX), z = Cell(p.z, m_MinZ);
                return x >= 0 && z >= 0 && x < m_Cells.GetLength(0) && z < m_Cells.GetLength(1) && m_Cells[x, z];
            }

            /// <summary>Distance from p along dir to the edge of the asphalt (max 15 m).</summary>
            public float Reach(Vector3 p, Vector3 dir)
            {
                var d = 0f;
                while (d < 15f && IsAsphalt(p + dir * (d + 0.1f))) d += 0.1f;
                return d;
            }

            static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
                var d1 = Sign(p, a, b); var d2 = Sign(p, b, c); var d3 = Sign(p, c, a);
                var neg = d1 < 0 || d2 < 0 || d3 < 0;
                var pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }
        }

        static List<Vector3> Smooth(List<Vector3> points, int radius)
        {
            var n = points.Count;
            var result = new List<Vector3>(n);
            for (var i = 0; i < n; i++)
            {
                var sum = Vector3.zero;
                for (var k = -radius; k <= radius; k++) sum += points[(i + k + n) % n];
                result.Add(sum / (2 * radius + 1));
            }

            return result;
        }

        static void Resample(List<Vector3> points, List<float> widths, float step, Loop loop)
        {
            var n = points.Count;
            var total = 0f;
            for (var i = 0; i < n; i++) total += Vector3.Distance(points[i], points[(i + 1) % n]);
            loop.length = total;
            var segments = Mathf.Max(8, Mathf.RoundToInt(total / step));
            var spacing = total / segments;
            var index = 0;
            var travelled = 0f;
            var next = 0f;
            for (var s = 0; s < segments; s++, next += spacing)
            {
                while (travelled + Vector3.Distance(points[index], points[(index + 1) % n]) < next)
                {
                    travelled += Vector3.Distance(points[index], points[(index + 1) % n]);
                    index = (index + 1) % n;
                }

                var segment = Vector3.Distance(points[index], points[(index + 1) % n]);
                var f = segment > 1e-4f ? (next - travelled) / segment : 0f;
                loop.centreLine.Add(Vector3.Lerp(points[index], points[(index + 1) % n], f));
                loop.halfWidths.Add(Mathf.Lerp(widths[index], widths[(index + 1) % n], f));
            }
        }

        static float Median(List<float> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0f : sorted[sorted.Count / 2];
        }

        // ------------------------------------------------------------------ placing it in a scene

        const string k_Folder = GreyboxKit.GeneratedRoot + "/Highway";
        // Material slots of ROAD1.fbx (found by Analyse: 0 = kerb faces, 1 = pavement, 2 = asphalt).
        const int k_KerbSlot = 0, k_PavementSlot = 1;

        /// <summary>
        /// Puts the loop in the scene: a corrected copy of the mesh (the pavement faces point down in the FBX, so they
        /// were invisible and could not be walked on), textured materials and a mesh collider for driving / walking.
        /// </summary>
        public static Loop Place(Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var go = (GameObject)Object.Instantiate(model, parent, false);
            go.name = "Highway loop";
            var filter = go.GetComponentInChildren<MeshFilter>();
            var renderer = filter.GetComponent<MeshRenderer>();
            filter.sharedMesh = FixedMesh(filter.sharedMesh);
            renderer.sharedMaterials = new[]
            {
                LoopMaterial("Highway_Kerb", "1651401712471.jpg"),
                LoopMaterial("Highway_Pavement", "1651401712485.jpg"),
                LoopMaterial("Highway_Road", "Road_001_basecolor.jpg"),
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            return Analyse(go);
        }

        static Mesh FixedMesh(Mesh source)
        {
            GreyboxKit.EnsureFolder(k_Folder);
            var path = k_Folder + "/HighwayLoop_Fixed.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;
            var mesh = Object.Instantiate(source);
            mesh.name = "HighwayLoop_Fixed";
            var tris = mesh.GetTriangles(k_PavementSlot);
            for (var i = 0; i < tris.Length; i += 3)
                (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]); // flip the pavement faces up
            mesh.SetTriangles(tris, k_PavementSlot);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material LoopMaterial(string name, string texture)
        {
            GreyboxKit.EnsureFolder(k_Folder);
            var path = k_Folder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/Highway/" + texture));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", name == "Highway_Road" ? 0.15f : 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Height of the surface (road or pavement) under a point, from the mesh collider.</summary>
        public static float SurfaceY(Vector3 p, float fallback = 0f)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 10f) ? hit.point.y : fallback;
        }

        /// <summary>Index of the straightest stretch (least heading change over +/- <paramref name="span"/> points).</summary>
        public static int StraightestIndex(Loop loop, int span = 10)
        {
            var best = 0;
            var bestTurn = float.MaxValue;
            var n = loop.centreLine.Count;
            for (var i = 0; i < n; i++)
            {
                var turn = Vector3.Angle(Direction(loop, (i - span + n) % n), Direction(loop, (i + span) % n));
                if (turn < bestTurn) { bestTurn = turn; best = i; }
            }

            return best;
        }

        /// <summary>Point on the loop <paramref name="offset"/> metres to the right of the centre line (negative = left).</summary>
        public static Vector3 Lateral(Loop loop, int i, float offset)
        {
            var n = loop.centreLine.Count;
            i = ((i % n) + n) % n;
            var dir = Direction(loop, i);
            var right = new Vector3(dir.z, 0f, -dir.x);
            return loop.centreLine[i] + right * offset;
        }

        /// <summary>Unit direction of travel at centre-line index <paramref name="i"/> (counter-clockwise from above).</summary>
        public static Vector3 Direction(Loop loop, int i)
        {
            var n = loop.centreLine.Count;
            var d = loop.centreLine[(i + 1) % n] - loop.centreLine[(i - 1 + n) % n];
            d.y = 0f;
            return d.normalized;
        }
    }
}
