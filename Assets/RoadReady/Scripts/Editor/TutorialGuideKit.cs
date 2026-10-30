using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Build Tutorial Guide Prefab: makes Assets/RoadReady/Resources/RoadReadyGuide.prefab, the artwork the
    /// tutorial uses to show where to head. The scrolling arrow strips come from the Animation_Textures pack (their scroll
    /// scripts keep running), the ring from ThirdParty/Circle. The pack's materials are for the built-in render pipeline,
    /// so they are replaced by URP unlit ones with the same texture. Children: Circle, Straight, TurnLeft, TurnRight,
    /// each lying flat on the ground with its forward (+Z) along the way to go.
    /// </summary>
    public static class TutorialGuideKit
    {
        const string k_Pack = "Assets/Animation_Textures/Prefabs/1st Animation";
        const string k_ArrowTexture = "Assets/Animation_Textures/Textures/28578ec2d0967642b42c32789829eeed copy.png";
        const string k_CircleModel = "Assets/ThirdParty/Circle/thecircle2ANIMATION.fbx";
        const string k_Folder = "Assets/RoadReady/Resources";
        const float k_ArrowLength = 7.5f;   // metres along the road
        const float k_CircleDiameter = 7f;  // the learner has to drive into it
        const float k_TurnAngle = 35f;

        [MenuItem("RoadReady/Build Tutorial Guide Prefab", priority = 40)]
        public static void Build()
        {
            EnsureFolder(k_Folder);
            var arrow = ArrowMaterial();
            var root = new GameObject("RoadReadyGuide");
            try
            {
                // The "Right" strip's chevrons point along +X, "Left" along -X.
                Strip(root.transform, "Straight", k_Pack + "/Right.prefab", arrow, 0f);
                Strip(root.transform, "TurnLeft", k_Pack + "/Left.prefab", arrow, -k_TurnAngle);
                Strip(root.transform, "TurnRight", k_Pack + "/Right.prefab", arrow, k_TurnAngle);
                MakeCircle(root.transform);
                var path = k_Folder + "/RoadReadyGuide.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                Debug.Log($"[RoadReady] Tutorial guide prefab built at {path}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>One arrow strip from the pack, laid flat, scaled to the road and turned to point <paramref name="bend"/> degrees off straight on.</summary>
        static void Strip(Transform parent, string name, string prefabPath, Material material, float bend)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
                throw new System.InvalidOperationException($"[RoadReady] {prefabPath} not found.");
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            var art = (GameObject)PrefabUtility.InstantiatePrefab(source, holder);
            PrefabUtility.UnpackPrefabInstance(art, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            art.name = "Art";
            foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial = material;

            // Lie flat, long side along X, centred.
            var flat = LayFlat(art.transform);
            art.transform.localRotation = flat * art.transform.localRotation;
            var b = WorldBounds(art);
            var length = Mathf.Max(b.size.x, b.size.z);
            art.transform.localScale = Vector3.one * (k_ArrowLength / length);
            b = WorldBounds(art);
            art.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);

            // The chevrons point along +X once laid flat: turn them to point forward, then bend.
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(holder, false);
            pivot.localRotation = Quaternion.Euler(0f, -90f + bend, 0f);
            art.transform.SetParent(pivot, false);
            holder.gameObject.SetActive(false);
        }

        /// <summary>The rotation that lays a model flat (its thinnest side up) with its longest side along X.</summary>
        static Quaternion LayFlat(Transform art)
        {
            var b = WorldBounds(art.gameObject);
            var sizes = new[] { b.size.x, b.size.y, b.size.z };
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var thin = System.Array.IndexOf(sizes, sizes.Min());
            var q1 = Quaternion.FromToRotation(axes[thin], Vector3.up);
            // Longest side after the first turn.
            var longIndex = Enumerable.Range(0, 3).Where(i => i != thin).OrderByDescending(i => sizes[i]).First();
            var along = q1 * axes[longIndex];
            along.y = 0f;
            var q2 = Quaternion.AngleAxis(Vector3.SignedAngle(along, Vector3.right, Vector3.up), Vector3.up);
            return q2 * q1;
        }

        /// <summary>Lays a tilted ring flat: its normal (the direction it varies least in) points up.</summary>
        static Quaternion LayFlatRing(Transform art)
        {
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var filter in art.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    foreach (var v in filter.sharedMesh.vertices)
                        points.Add(filter.transform.TransformPoint(v));
            var mean = points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Count;
            var c = new float[3, 3];
            foreach (var p in points)
            {
                var d = p - mean;
                for (var x = 0; x < 3; x++)
                    for (var y = 0; y < 3; y++)
                        c[x, y] += d[x] * d[y];
            }

            var trace = c[0, 0] + c[1, 1] + c[2, 2];
            var n = new Vector3(0.3f, 0.5f, 0.8f).normalized;
            for (var i = 0; i < 300; i++)
            {
                n = new Vector3(
                    (trace - c[0, 0]) * n.x - c[0, 1] * n.y - c[0, 2] * n.z,
                    -c[1, 0] * n.x + (trace - c[1, 1]) * n.y - c[1, 2] * n.z,
                    -c[2, 0] * n.x - c[2, 1] * n.y + (trace - c[2, 2]) * n.z).normalized;
            }

            return Quaternion.FromToRotation(n, Vector3.up);
        }

        static void MakeCircle(Transform parent)
        {
            var holder = new GameObject("Circle").transform;
            holder.SetParent(parent, false);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(k_CircleModel);
            if (source == null)
                throw new System.InvalidOperationException($"[RoadReady] {k_CircleModel} not found.");
            var art = (GameObject)PrefabUtility.InstantiatePrefab(source, holder);
            PrefabUtility.UnpackPrefabInstance(art, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            art.name = "Ring";
            foreach (var r in art.GetComponentsInChildren<MeshRenderer>(true))
                r.sharedMaterial = FlatMaterial("GuideCircle", new Color(0.1f, 0.9f, 1f, 1f));

            art.transform.localRotation = LayFlatRing(art.transform) * art.transform.localRotation;
            var b = WorldBounds(art);
            art.transform.localScale = Vector3.one * (k_CircleDiameter / Mathf.Max(b.size.x, b.size.z));
            b = WorldBounds(art);
            art.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);

            // A soft disc inside the ring so the circle reads from a distance.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.name = "Disc";
            disc.transform.SetParent(holder, false);
            disc.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            disc.transform.localScale = new Vector3(k_CircleDiameter - 0.2f, 0.004f, k_CircleDiameter - 0.2f);
            var disk = FlatMaterial("GuideDisc", new Color(0.1f, 0.9f, 1f, 0.22f));
            disc.GetComponent<MeshRenderer>().sharedMaterial = disk;
            disc.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            holder.gameObject.SetActive(false);
        }

        /// <summary>Bounds from the mesh vertices through the current transforms (Renderer.bounds can be stale right after a transform change).</summary>
        static Bounds WorldBounds(GameObject go)
        {
            var first = true;
            var bounds = new Bounds();
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                    continue;
                foreach (var v in filter.sharedMesh.vertices)
                {
                    var p = filter.transform.TransformPoint(v);
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

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// The pack's chevron texture on a crisp material. The pack imports it at 2048 px (it is 4372 px wide), with
        /// non-power-of-two rescaling, compression and no anisotropic filtering, which is what makes the arrows blurry
        /// on the road. So the guide gets its own copy at full size, sampled with 16x anisotropy, and the edges are
        /// alpha-cut (mipmaps keep the line coverage) instead of blended.
        /// </summary>
        static Material ArrowMaterial()
        {
            var texturePath = k_Folder + "/GuideArrows.png";
            if (!File.Exists(texturePath))
                File.Copy(k_ArrowTexture, texturePath);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.4f;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 16;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();

            var path = k_Folder + "/GuideArrow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "GuideArrow" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.1f, 1f));
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.4f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material FlatMaterial(string name, Color color)
        {
            var path = k_Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            if (color.a < 0.99f)
                MakeTransparent(material);
            else
                material.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
