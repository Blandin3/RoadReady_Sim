using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Road signs from the imported pack (ThirdParty/KoreanRoad Signs). The pack's sign plates have no artwork
    /// assigned, so each sign used here gets a material with its picture. A sign is made once into a prefab (pole
    /// base at its origin, face towards +Z) and then placed at the roadside facing the drivers who must read it.
    /// </summary>
    public static class RoadSigns
    {
        const string Pack = "Assets/ThirdParty/KoreanRoad Signs";
        const string Model = Pack + "/Korea roadsigns.fbx";
        const string Folder = GreyboxKit.GeneratedRoot + "/Signs";
        const float k_Scale = 1.3f; // the pack's 0.54 m plates are small for a headset; 0.7 m is a typical urban sign

        // Plate material name in the model -> picture in the pack.
        static readonly Dictionary<string, string> k_Pictures = new Dictionary<string, string>
        {
            { "SL30", "speed limit 30kmh.png" },
            { "SL40", "Speed limit 40kmh.png" },
            { "SL50", "Speed limit 50kmh.png" },
            { "SL60", "Speed liimit 60kmh.png" },
            { "Slow", "Slow down_2.jpeg" },
            { "Parking lot", "Parking lot.png" },
            { "No parking", "No parking.png" },
            { "No stopping or parking", "No stopping or parking.png" },
            { "No U turn", "No U turn.png" },
            { "No entry", "No entry.png" },
            { "Stop", "Stop.png" },
            { "Other dangers", "Other dangers.jpeg" },
            { "One way", "One way.png" },
        };

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(Model) != null;

        /// <summary>
        /// Places a sign with its pole at <paramref name="groundPosition"/> and its face pointing along
        /// <paramref name="facing"/> (i.e. towards the drivers who should read it). Returns null if unavailable.
        /// </summary>
        public static GameObject Place(Transform parent, string sign, Vector3 groundPosition, Vector3 facing)
        {
            var prefab = Prefab(sign);
            if (prefab == null)
                return null;
            facing.y = 0f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = "Sign - " + sign;
            go.transform.SetPositionAndRotation(groundPosition, Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward));
            return go;
        }

        static GameObject Prefab(string sign)
        {
            if (!Available || !k_Pictures.ContainsKey(sign))
            {
                if (Available) Debug.LogWarning($"[RoadReady] No picture mapped for road sign '{sign}'.");
                return null;
            }

            GreyboxKit.EnsureFolder(Folder);
            var path = $"{Folder}/Sign_{sign.Replace(" ", "_")}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            try
            {
                var plate = model.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name == sign);
                if (plate == null)
                {
                    Debug.LogWarning($"[RoadReady] Road sign '{sign}' not found in {Model}.");
                    return null;
                }

                // The sign's own group: the plate's parent holds the plate, its support and its pole.
                var group = plate.transform.parent;
                var pole = group.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.StartsWith("Pole"));
                var footing = pole != null ? new Vector3(pole.bounds.center.x, pole.bounds.min.y, pole.bounds.center.z) : group.position;

                var root = new GameObject("Sign_" + sign);
                root.transform.position = footing;
                group.SetParent(root.transform, true);
                root.transform.position = Vector3.zero;
                group.localScale *= k_Scale;
                group.localPosition *= k_Scale;
                plate.sharedMaterial = PlateMaterial(sign);
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // A thin collider on the pole so cars stop at it and pedestrians walk round it.
                if (pole != null)
                {
                    var collider = root.AddComponent<CapsuleCollider>();
                    collider.center = new Vector3(0f, pole.bounds.size.y * k_Scale * 0.5f, 0f);
                    collider.height = pole.bounds.size.y * k_Scale;
                    collider.radius = 0.06f;
                }

                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        static Material PlateMaterial(string sign)
        {
            var path = $"{Folder}/M_Sign_{sign.Replace(" ", "_")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pack}/{k_Pictures[sign]}"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.35f);
            // Slightly self-lit so the sign reads well in shade and at dusk (real signs are retro-reflective).
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.18f, 0.18f, 0.18f));
            mat.SetTexture("_EmissionMap", mat.GetTexture("_BaseMap"));
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
