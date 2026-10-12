using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Road tiles, trees and street lights from "Modular Lowpoly Streets (Free)". The pack is modelled small: its
    /// two-lane carriageway is 2 units wide, so tiles are scaled 3.5x to give our 7 m road (3.5 m lanes) and 5.25 m
    /// pavements. A slightly smaller vertical scale puts the road surface at y = 0 and the pavement at 12 cm, the
    /// same heights the traffic, pedestrians and rules already use. Rules never read these meshes (they use
    /// RoadZones), so the art can change without touching the scenarios.
    /// </summary>
    public static class StreetKit
    {
        public const string Pack = "Assets/ModularLowpolyStreetsFree";
        const float k_Scale = 3.5f;
        const float k_VerticalScale = 1.2f;
        /// <summary>Raises a tile so its road surface (modelled at -0.1) sits at y = 0.</summary>
        const float k_Lift = 0.1f * k_VerticalScale;

        /// <summary>Length of one straight tile along the road (metres).</summary>
        public const float TileLength = 2.5f * k_Scale;
        /// <summary>Half the width of a tile: road plus pavement on each side (metres).</summary>
        public const float HalfTile = 2.5f * k_Scale;
        /// <summary>Radius of the corner tile's centre line (metres).</summary>
        public const float TurnRadius = 2.5f * k_Scale;

        static readonly Vector3 k_TileScale = new Vector3(k_Scale, k_VerticalScale, k_Scale);
        static bool s_Converted;

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/FBX/Roads.fbx") != null;

        static GameObject Fbx(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/FBX/{name}.fbx");

        // ================================================================ road tiles

        /// <summary>
        /// Places a piece of Roads.fbx. <paramref name="meshAnchor"/> is a point in the piece's own (unscaled) mesh
        /// space that should land on <paramref name="world"/>; the piece is turned by <paramref name="yaw"/>.
        /// </summary>
        static GameObject Tile(Transform parent, string piece, Vector3 world, float yaw, Vector3 meshAnchor)
        {
            EnsureUrpMaterials();
            var source = Fbx("Roads").transform.Find(piece);
            var holder = Point(parent, piece, world, yaw);
            var tile = Object.Instantiate(source.gameObject, holder);
            tile.name = piece + " (mesh)";
            tile.transform.localRotation = Quaternion.identity;
            tile.transform.localScale = k_TileScale;
            tile.transform.localPosition = -Vector3.Scale(k_TileScale, meshAnchor) + Vector3.up * k_Lift;

            // The pack ships a simplified "_mc" mesh per piece for collisions.
            var collision = Fbx("Roads").transform.Find(piece + "_mc");
            if (collision != null && collision.TryGetComponent<MeshFilter>(out var mf))
                tile.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return holder.gameObject;
        }

        /// <summary><paramref name="count"/> straight tiles from <paramref name="start"/> (centre of the first tile's near edge) along <paramref name="direction"/>.</summary>
        public static void Straight(Transform parent, Vector3 start, Vector3 direction, int count)
        {
            direction.y = 0f;
            direction.Normalize();
            var group = Group(parent, "Road tiles");
            for (var i = 0; i < count; i++)
                Tile(group, "Road_1_line", start + direction * (i * TileLength), Yaw(direction), new Vector3(-2.5f, 0f, 0f));
        }

        /// <summary>Straight tiles covering a road from <paramref name="from"/> to <paramref name="to"/> (rounded up to whole tiles, centred).</summary>
        public static void StraightBetween(Transform parent, Vector3 from, Vector3 to)
        {
            var length = Vector3.Distance(from, to);
            var count = Mathf.CeilToInt(length / TileLength - 0.01f);
            var direction = (to - from).normalized;
            var overhang = (count * TileLength - length) * 0.5f;
            Straight(parent, from - direction * overhang, direction, count);
        }

        /// <summary>Four-way crossroads with a zebra on every arm, centred on <paramref name="centre"/>.</summary>
        public static GameObject Crossroads(Transform parent, Vector3 centre, float yaw = 0f) =>
            Tile(parent, "Crossroads_1_lines_walk", centre, yaw, new Vector3(-2.5f, 0f, 2.5f));

        /// <summary>
        /// T-junction centred on <paramref name="centre"/>. Unturned, the through road runs along +z and the branch
        /// leaves towards +x; yaw 90 gives a through road along x with the branch to the south (-z).
        /// </summary>
        public static GameObject TJunction(Transform parent, Vector3 centre, float yaw) =>
            Tile(parent, "Crossroads_1_lines_2_walk", centre, yaw, new Vector3(-2.5f, 0f, 2.5f));

        /// <summary>
        /// 90-degree corner whose arc is centred on <paramref name="arcCentre"/>. Unturned, the road bends round the
        /// arc centre joining an arm pointing -z with an arm pointing +x (yaw 90/180/270 rotate that clockwise).
        /// </summary>
        public static GameObject Corner(Transform parent, Vector3 arcCentre, float yaw) =>
            Tile(parent, "Road_1_line_turn", arcCentre, yaw, Vector3.zero);

        /// <summary>
        /// Covers painted markings with plain asphalt cut from the middle of the crossroads texture (used to remove
        /// the zebras of the T-junction tile where the scenario has no crossing).
        /// </summary>
        public static void PlainAsphalt(Transform parent, string name, Vector3 centre, float sizeAlong, float sizeAcross, float yaw)
        {
            EnsureUrpMaterials();
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Pack}/Materials/Roads/Crossroads-1-lines-walk.mat");
            var holder = Point(parent, name, centre + Vector3.up * 0.004f, yaw);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = name + " (mesh)";
            quad.transform.SetParent(holder, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(sizeAcross, sizeAlong, 1f);
            var mesh = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            // Sample the marking-free middle of the junction texture at the tile's own texel density (the texture spans
            // one whole 17.5 m tile), so the patch matches the surrounding asphalt grain.
            var tileSize = 2f * HalfTile;
            var u = Mathf.Min(0.3f, sizeAcross / tileSize) * 0.5f;
            var v = Mathf.Min(0.3f, sizeAlong / tileSize) * 0.5f;
            mesh.uv = new[] { new Vector2(0.5f - u, 0.5f - v), new Vector2(0.5f + u, 0.5f - v), new Vector2(0.5f - u, 0.5f + v), new Vector2(0.5f + u, 0.5f + v) };
            quad.GetComponent<MeshFilter>().sharedMesh = mesh;
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ================================================================ street furniture

        /// <summary>Tree with its pavement grate, about 5.8 m tall.</summary>
        public static void Tree(Transform parent, Vector3 position)
        {
            EnsureUrpMaterials();
            var holder = Point(parent, "Tree", position, Random.Range(0f, 360f));
            var nature = Fbx("Nature").transform;
            Part(holder, nature.Find("Tree_1"), nature.Find("Tree_1").localPosition, 4f, true);
            Part(holder, nature.Find("Tree_area_1"), nature.Find("Tree_area_1").localPosition, 4f, false);
        }

        /// <summary>Street light about 6.5 m tall, its lamp arm reaching towards <paramref name="towardsRoad"/>.</summary>
        public static void StreetLight(Transform parent, Vector3 position, Vector3 towardsRoad)
        {
            EnsureUrpMaterials();
            var holder = Point(parent, "Street light", position, Yaw(towardsRoad));
            var poles = Fbx("Poles").transform;
            var pivot = poles.Find("Pole_1").localPosition;
            foreach (var part in new[] { "Pole_1", "Pole_base", "Pole_light" })
                Part(holder, poles.Find(part), pivot, 6f, part == "Pole_1");
        }

        static void Part(Transform holder, Transform source, Vector3 pivot, float scale, bool collider)
        {
            if (source == null) return;
            var go = Object.Instantiate(source.gameObject, holder);
            go.name = source.name;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = (source.localPosition - pivot) * scale;
            if (collider && go.TryGetComponent<MeshFilter>(out var mf))
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        // ================================================================ materials

        /// <summary>
        /// The pack ships Built-in RP "Standard" materials (pink in URP). Switch them to URP/Lit once, carrying
        /// over albedo, normal, metallic and AO maps, smoothness-in-albedo-alpha and emission.
        /// </summary>
        public static void EnsureUrpMaterials()
        {
            if (s_Converted)
                return;
            s_Converted = true;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Pack + "/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader == null || !mat.shader.name.StartsWith("Standard"))
                    continue;

                Texture Tex(string n) => mat.HasProperty(n) ? mat.GetTexture(n) : null;
                var albedo = Tex("_MainTex");
                var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                var normal = Tex("_BumpMap");
                var metallicMap = Tex("_MetallicGlossMap");
                var occlusion = Tex("_OcclusionMap");
                var smoothness = mat.HasProperty("_GlossMapScale") ? mat.GetFloat("_GlossMapScale") : mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.3f;
                var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                var smoothFromAlbedo = mat.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                var emissionOn = mat.IsKeywordEnabled("_EMISSION");
                var emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                var emissionMap = Tex("_EmissionMap");

                mat.shader = lit;
                mat.SetTexture("_BaseMap", albedo);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                if (metallicMap != null) { mat.SetTexture("_MetallicGlossMap", metallicMap); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (occlusion != null) { mat.SetTexture("_OcclusionMap", occlusion); mat.EnableKeyword("_OCCLUSIONMAP"); }
                if (smoothFromAlbedo)
                {
                    mat.SetFloat("_SmoothnessTextureChannel", 1f);
                    mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                }

                if (emissionOn)
                {
                    mat.SetColor("_EmissionColor", emission);
                    mat.SetTexture("_EmissionMap", emissionMap);
                    mat.EnableKeyword("_EMISSION");
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
