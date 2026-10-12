using System.Collections.Generic;
using RoadReady.Traffic;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Road surfaces built from the EasyRoads3D road textures. EasyRoads3D Free blocks its scripting API, so the
    /// roads are generated here instead: textured ribbons along a centreline (straight or looped), plain asphalt
    /// patches for junctions, and raised sidewalk strips. Rules never look at these meshes - they use RoadZones.
    /// </summary>
    public static class RoadMeshKit
    {
        const string k_Albedo = "Assets/EasyRoads3D/textures/roads/road2Lane_A.tga";
        const string k_Normal = "Assets/EasyRoads3D/textures/roads/road_N.png";

        /// <summary>One texture repeat covers this many metres of road (two 3 m centre dashes per repeat).</summary>
        public const float TileLength = 12f;
        /// <summary>Strip of the texture with no markings (between the edge line and the centre line).</summary>
        const float k_PlainUMin = 0.15f, k_PlainUMax = 0.38f;
        const float k_SurfaceLift = 0.003f;

        static Material s_Road;

        // ================================================================ material

        public static Material RoadMaterial()
        {
            if (s_Road != null)
                return s_Road;
            EnsureFolder(MaterialsFolder);
            var path = $"{MaterialsFolder}/M_road_textured.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(k_Albedo));
            mat.SetColor("_BaseColor", Color.white);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(k_Normal);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 0.6f);
                mat.EnableKeyword("_NORMALMAP");
            }

            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            s_Road = mat;
            return mat;
        }

        // ================================================================ road surfaces

        /// <summary>Textured road along a centreline. U runs across the road (edge lines at both sides), V along it.</summary>
        public static GameObject Ribbon(Transform parent, string name, IList<Vector3> centre, float width, bool closed, bool collider)
        {
            var points = new List<Vector3>(centre);
            var rights = Rights(points, closed);
            if (closed)
            {
                points.Add(points[0]);
                rights.Add(rights[0]);
            }

            var total = 0f;
            for (var i = 1; i < points.Count; i++)
                total += Vector3.Distance(points[i - 1], points[i]);
            // A closed loop needs a whole number of repeats so the dashes line up at the seam.
            var tile = closed ? total / Mathf.Max(1f, Mathf.Round(total / TileLength)) : TileLength;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var v = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    v += Vector3.Distance(points[i - 1], points[i]) / tile;
                var p = points[i] + Vector3.up * (RoadTop + k_SurfaceLift - points[i].y);
                verts.Add(p - rights[i] * width * 0.5f);
                verts.Add(p + rights[i] * width * 0.5f);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                if (i > 0)
                    Quad(tris, 2 * (i - 1), 2 * (i - 1) + 1, 2 * i, 2 * i + 1);
            }

            return MeshObject(parent, name, verts, uvs, tris, RoadMaterial(), collider);
        }

        /// <summary>Marking-free asphalt (junction boxes, under zebra stripes) that matches the textured road.</summary>
        public static GameObject PlainPatch(Transform parent, string name, Vector3 centre, float sizeX, float sizeZ)
        {
            var y = RoadTop + k_SurfaceLift + 0.001f;
            var hx = sizeX * 0.5f;
            var hz = sizeZ * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(centre.x - hx, y, centre.z - hz), new Vector3(centre.x + hx, y, centre.z - hz),
                new Vector3(centre.x - hx, y, centre.z + hz), new Vector3(centre.x + hx, y, centre.z + hz),
            };
            var vLen = Mathf.Max(sizeX, sizeZ) / TileLength;
            var uvs = new List<Vector2>
            {
                new Vector2(k_PlainUMin, 0f), new Vector2(k_PlainUMax, 0f),
                new Vector2(k_PlainUMin, vLen), new Vector2(k_PlainUMax, vLen),
            };
            var tris = new List<int>();
            Quad(tris, 0, 1, 2, 3);
            return MeshObject(parent, name, verts, uvs, tris, RoadMaterial(), false);
        }

        /// <summary>
        /// Raised sidewalk following a centreline, between two lateral offsets (positive = right of travel).
        /// Includes the kerb face on the road side so the step is visible.
        /// </summary>
        public static GameObject Sidewalk(Transform parent, string name, IList<Vector3> centre, bool closed, float lateralA, float lateralB)
        {
            var lo = Mathf.Min(lateralA, lateralB);
            var hi = Mathf.Max(lateralA, lateralB);
            var roadSide = Mathf.Abs(lo) < Mathf.Abs(hi) ? lo : hi;
            var points = new List<Vector3>(centre);
            var rights = Rights(points, closed);
            if (closed)
            {
                points.Add(points[0]);
                rights.Add(rights[0]);
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // Each strip gets its own vertices; flipped strips must not share vertices or their normals cancel (black).
            void Strip(System.Func<int, (Vector3 a, Vector3 b)> edge, bool flip)
            {
                var start = verts.Count;
                for (var i = 0; i < points.Count; i++)
                {
                    var (a, b) = edge(i);
                    verts.Add(a);
                    verts.Add(b);
                    uvs.Add(new Vector2(a.x, a.z) * 0.5f);
                    uvs.Add(new Vector2(b.x, b.z) * 0.5f + new Vector2(0f, (b.y - a.y) * 0.5f));
                    if (i == 0) continue;
                    var k = start + 2 * (i - 1);
                    if (flip) Quad(tris, k + 1, k, k + 3, k + 2);
                    else Quad(tris, k, k + 1, k + 2, k + 3);
                }
            }

            var ground = new Vector3(0f, RoadTop - 0.1f, 0f);
            var top = new Vector3(0f, SidewalkTop, 0f);
            Vector3 Flat(int i) => new Vector3(points[i].x, 0f, points[i].z);
            Strip(i => (Flat(i) + rights[i] * lo + top, Flat(i) + rights[i] * hi + top), false);
            // Kerb on the road side and the edge on the far side, visible from either direction.
            foreach (var lateral in new[] { roadSide, roadSide == lo ? hi : lo })
            {
                System.Func<int, (Vector3, Vector3)> face = i => (Flat(i) + rights[i] * lateral + ground, Flat(i) + rights[i] * lateral + top);
                Strip(face, false);
                Strip(face, true);
            }
            return MeshObject(parent, name, verts, uvs, tris, Mat("sidewalk"), true);
        }

        // ================================================================ zones along a curve

        /// <summary>Rotated RoadZone boxes along a centreline at a lateral offset (curves become short boxes).</summary>
        public static void ZonesAlong(Transform parent, string name, RoadZoneType type, IList<Vector3> centre, bool closed, float lateral, float width)
        {
            var rights = Rights(new List<Vector3>(centre), closed);
            var count = closed ? centre.Count : centre.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % centre.Count;
                var a = centre[i] + rights[i] * lateral;
                var b = centre[j] + rights[j] * lateral;
                var dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) continue;
                var t = Point(parent, $"{name} {i}", (a + b) * 0.5f, Yaw(dir));
                t.position = new Vector3(t.position.x, 0f, t.position.z);
                t.gameObject.AddComponent<RoadZone>().Configure(type, new Vector3(width, 3f, dir.magnitude + 0.6f));
            }
        }

        // ================================================================ geometry helpers

        /// <summary>Unit vectors to the right of travel at each point (averaged across the neighbouring segments).</summary>
        public static List<Vector3> Rights(List<Vector3> points, bool closed)
        {
            var rights = new List<Vector3>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                Vector3 prev, next;
                if (closed)
                {
                    prev = points[(i - 1 + points.Count) % points.Count];
                    next = points[(i + 1) % points.Count];
                }
                else
                {
                    prev = points[Mathf.Max(0, i - 1)];
                    next = points[Mathf.Min(points.Count - 1, i + 1)];
                }

                var t = next - prev;
                t.y = 0f;
                t.Normalize();
                rights.Add(new Vector3(t.z, 0f, -t.x));
            }

            return rights;
        }

        static void Quad(List<int> tris, int l0, int r0, int l1, int r1)
        {
            tris.Add(l0); tris.Add(l1); tris.Add(r0);
            tris.Add(r0); tris.Add(l1); tris.Add(r1);
        }

        static GameObject MeshObject(Transform parent, string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris, Material material, bool collider)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
