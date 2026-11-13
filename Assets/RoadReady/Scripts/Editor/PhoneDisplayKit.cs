using RoadReady.Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Add Phone To Player Car: mounts Assets/ThirdParty/pHONE1.fbx on the dashboard, in the middle of the car
    /// and tilted towards the driver's eyes, with a screen that shows the traffic fine (PhoneFineDisplay) whenever the
    /// driver breaks a rule. Also run by "Use Imported Car For Driver (Bentley)", so the phone survives rebuilding the car.
    /// </summary>
    public static class PhoneDisplayKit
    {
        public const string GroupName = "Dashboard Phone";
        const string k_ModelPath = "Assets/ThirdParty/pHONE1.fbx";
        const float k_PhoneHeight = 0.16f;      // metres, long edge of the phone
        const float k_AheadOfWheel = 0.30f;     // metres in front of the steering wheel hub (towards the windscreen)
        const float k_BelowHub = 0.02f;         // metres below the hub height, so it sits on top of the dash
        static readonly Vector3 k_BesideShifter = new Vector3(0.10f, 0.13f, 0.10f); // from the gear shifter: to the right, up above the console, a little ahead
        const float k_ScreenInset = 0.88f;      // share of the phone's face covered by the screen

        [MenuItem("RoadReady/Add Phone To Player Car", priority = 33)]
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

        /// <summary>Replaces any earlier phone on the (open) player car root. <paramref name="hub"/> is the steering wheel hub in root space.</summary>
        public static string Build(GameObject root, Vector3 hub)
        {
            var rootT = root.transform;
            Transform old;
            while ((old = rootT.Find(GroupName)) != null)
                Object.DestroyImmediate(old.gameObject);
            var oldDisplay = root.GetComponent<PhoneFineDisplay>();
            if (oldDisplay != null)
                Object.DestroyImmediate(oldDisplay);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelPath);
            if (model == null)
                return $"Phone not added: {k_ModelPath} not found.";

            EnsureFolder(PrefabsFolder);

            // Beside the gear shifter, on the centre console, turned to face the driver's eyes.
            var eye = rootT.Find("Eye point");
            var shifter = rootT.Find("Gear Shifter");
            var position = shifter != null
                ? shifter.localPosition + k_BesideShifter
                : new Vector3(0f, hub.y - k_BelowHub, hub.z + k_AheadOfWheel);
            var facing = Quaternion.Euler(20f, 0f, 0f);
            if (eye != null)
                facing = Quaternion.LookRotation((position - eye.localPosition).normalized, Vector3.up); // the screen (-Z) looks back at the eyes

            var group = new GameObject(GroupName).transform;
            group.SetParent(rootT, false);
            group.localPosition = position;
            group.localRotation = facing;

            var phone = (GameObject)PrefabUtility.InstantiatePrefab(model, group);
            phone.name = "Phone";
            foreach (var collider in phone.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            // Orient by shape: the longest edge up, the thinnest edge towards the driver (-Z).
            var local = MeshBounds(phone.transform, phone.transform);
            var size = local.size;
            var longAxis = LargestAxis(size);
            var thinAxis = SmallestAxis(size);
            if (longAxis == thinAxis)
                thinAxis = (longAxis + 1) % 3;
            phone.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(Quaternion.LookRotation(AxisVector(thinAxis), AxisVector(longAxis)));
            phone.transform.localScale = Vector3.one * (k_PhoneHeight / Mathf.Max(size[longAxis], 1e-4f));
            phone.transform.localPosition = Vector3.zero;

            // Centre the model on the group origin.
            var bounds = MeshBounds(phone.transform, group);
            phone.transform.localPosition = -bounds.center;
            bounds = MeshBounds(phone.transform, group);

            UrpMaterials(phone);

            // Dark screen panel on the driver-facing face, with the fine text on top.
            var width = bounds.size.x * k_ScreenInset;
            var height = bounds.size.y * k_ScreenInset;
            var faceZ = bounds.min.z - 0.0008f;

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(group, false);
            screen.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, faceZ);
            screen.transform.localScale = new Vector3(width, height, 1f);
            screen.GetComponent<MeshRenderer>().sharedMaterial = ScreenMaterial(new Color(0.02f, 0.03f, 0.05f), "PlayerCar_PhoneScreen");

            var glow = new GameObject("Fine Glow");
            glow.transform.SetParent(group, false);
            glow.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, faceZ - 0.0004f);
            glow.transform.localScale = new Vector3(width * 0.97f, height * 0.985f, 1f);
            var glowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glowQuad.name = "Glow Quad";
            Object.DestroyImmediate(glowQuad.GetComponent<Collider>());
            glowQuad.transform.SetParent(glow.transform, false);
            glowQuad.GetComponent<MeshRenderer>().sharedMaterial = ScreenMaterial(new Color(0.10f, 0.14f, 0.24f), "PlayerCar_PhoneFine");

            var textObject = new GameObject("Fine Text", typeof(RectTransform));
            textObject.transform.SetParent(glow.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.0004f / Mathf.Max(glow.transform.localScale.z, 1e-4f));
            var text = textObject.AddComponent<TextMeshPro>();
            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(width * 0.94f, height * 0.94f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 0.5f;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = new Color(0.95f, 0.97f, 1f);
            text.richText = false;
            text.text = string.Empty;
            // The glow object is scaled to the screen; counter-scale the text so its rect is in metres.
            textObject.transform.localScale = new Vector3(1f / glow.transform.localScale.x, 1f / glow.transform.localScale.y, 1f);
            text.rectTransform.sizeDelta = new Vector2(width * 0.94f, height * 0.94f);

            var display = root.AddComponent<PhoneFineDisplay>();
            display.Configure(text, glow);
            EditorUtility.SetDirty(display);

            return $"Phone added at {position} ({(shifter != null ? "beside the gear shifter" : "on the dash")}), model size {size}, phone face {bounds.size}; it shows a traffic fine whenever the driver breaks a rule.";
        }

        // ================================================================ helpers

        static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        static int LargestAxis(Vector3 v) => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;

        static int SmallestAxis(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : v.y <= v.z ? 1 : 2;

        /// <summary>Bounds of every mesh under <paramref name="model"/>, in <paramref name="space"/>'s local space (the same wherever the model is posed).</summary>
        static Bounds MeshBounds(Transform model, Transform space)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                    mesh = skinned.sharedMesh;
                else if (renderer.TryGetComponent<MeshFilter>(out var filter))
                    mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                var matrix = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = matrix.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }

            return bounds;
        }

        /// <summary>The FBX's embedded materials may not be URP; copy each into the project as a URP Lit material (pink otherwise).</summary>
        static void UrpMaterials(GameObject phone)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
                return;
            var index = 0;
            foreach (var renderer in phone.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var source = mats[i];
                    if (source != null && source.shader == lit)
                        continue;
                    var path = $"{PrefabsFolder}/PlayerCar_Phone_{index++}.mat";
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null)
                    {
                        copy = new Material(lit) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                        AssetDatabase.CreateAsset(copy, path);
                    }

                    var colour = source != null && source.HasProperty("_Color") ? source.GetColor("_Color") : new Color(0.08f, 0.08f, 0.09f);
                    var texture = source != null ? source.mainTexture : null;
                    copy.SetColor("_BaseColor", texture != null ? Color.white : colour);
                    copy.SetTexture("_BaseMap", texture);
                    copy.SetFloat("_Smoothness", 0.6f);
                    EditorUtility.SetDirty(copy);
                    mats[i] = copy;
                }

                renderer.sharedMaterials = mats;
            }
        }

        static Material ScreenMaterial(Color colour, string name)
        {
            var path = $"{PrefabsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
