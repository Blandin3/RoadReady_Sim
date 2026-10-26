using System.Collections.Generic;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// Left and right wing mirrors plus an interior rear-view mirror for the player car. Each one is a small screen
    /// showing a rear-facing camera (mirror-flipped), so the learner can check traffic behind and reverse into a bay.
    /// The screens sit where the parking rules expect the driver to look: left mirror at yaw -28..-60, right mirror
    /// at 30..70, interior mirror up and to the right (see ParkingRule).
    /// Positions are given as a direction and distance from the eye point, so they follow the seat on any car model.
    /// </summary>
    public class CarMirrors : MonoBehaviour
    {
        public enum Shape { Wing, Bar }

        [System.Serializable]
        public class Mirror
        {
            public string name = "Mirror";
            [Tooltip("Outline of the glass: a wing mirror (rounded, tapering to the outer edge) or a rounded bar (interior mirror).")]
            public Shape shape = Shape.Wing;
            [Tooltip("Fitted onto the car model's own mirror glass: its exact position, angle and outline (car space), so the screen fills the real mirror.")]
            public bool exact;
            public Vector3 exactPosition;
            public Quaternion exactRotation = Quaternion.identity;
            [Tooltip("Outline of the real glass in a unit box (x right, y up, counter-clockwise, as the driver sees it).")]
            public Vector2[] exactOutline;
            [Tooltip("Direction from the driver's eye, degrees: + yaw = right, + pitch = up.")]
            public float yaw;
            public float pitch;
            [Tooltip("Metres from the eye.")]
            public float distance = 1f;
            [Tooltip("Screen size in metres.")]
            public Vector2 size = new Vector2(0.21f, 0.14f);
            [Tooltip("Where the camera looks, degrees from straight back (+ = towards the car's right).")]
            public float lookOutward;
            [Tooltip("Camera position in car space. Side mirrors use the screen position; the interior mirror uses the rear window.")]
            public bool cameraAtScreen = true;
            public float verticalFov = 38f;
            public Vector2Int resolution = new Vector2Int(384, 256);
        }

        [SerializeField] Transform m_EyePoint;
        [Tooltip("Unlit material for the screens (a copy is made per mirror). Falls back to the URP Unlit shader if empty.")]
        [SerializeField] Material m_ScreenMaterial;
        [SerializeField] Color m_FrameColor = new Color(0.04f, 0.04f, 0.05f);
        [SerializeField] float m_FrameThickness = 0.012f;
        [SerializeField] float m_FarClip = 120f;
        [SerializeField] Mirror[] m_Mirrors =
        {
            new Mirror { name = "Left Mirror", yaw = -44.5f, pitch = -9.8f, distance = 0.73f, size = new Vector2(0.17f, 0.12f), lookOutward = -9f },
            new Mirror { name = "Right Mirror", yaw = 65.9f, pitch = -5.6f, distance = 1.35f, size = new Vector2(0.21f, 0.15f), lookOutward = 9f },
            new Mirror
            {
                name = "Rear View Mirror", shape = Shape.Bar, yaw = 32f, pitch = 5.5f, distance = 0.58f, size = new Vector2(0.22f, 0.07f),
                cameraAtScreen = false, verticalFov = 24f, resolution = new Vector2Int(768, 256),
            },
        };

        readonly List<Mesh> m_Meshes = new List<Mesh>();
        Camera[] m_Cameras;
        RenderTexture[] m_Textures;
        Material[] m_Materials;
        Transform m_Root;
        float m_RearOffset;
        bool m_Built;

        public enum Glance { None, Left, Right, Rear }

        /// <summary>Which mirror the head is turned towards, from its forward direction in car space (same windows as the parking rule).</summary>
        public static Glance Classify(Vector3 headForwardInCar)
        {
            var yaw = Mathf.Atan2(headForwardInCar.x, headForwardInCar.z) * Mathf.Rad2Deg;
            var pitch = Mathf.Asin(Mathf.Clamp(headForwardInCar.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (yaw >= 5f && yaw <= 40f && pitch > 0f) return Glance.Rear;
            if (yaw >= -60f && yaw <= -28f) return Glance.Left;
            if (yaw >= 30f && yaw <= 70f) return Glance.Right;
            return Glance.None;
        }

        public Mirror[] Mirrors => m_Mirrors;

        public bool Visible { get; private set; } = true;

        public void Configure(Transform eyePoint, Material screenMaterial)
        {
            m_EyePoint = eyePoint;
            m_ScreenMaterial = screenMaterial;
        }

        /// <summary>Adds the mirrors to a player car that does not have them yet.</summary>
        public static CarMirrors EnsureOn(DriverController driver)
        {
            var mirrors = driver.GetComponent<CarMirrors>();
            if (mirrors == null)
                mirrors = driver.gameObject.AddComponent<CarMirrors>();
            if (mirrors.m_EyePoint == null)
                mirrors.m_EyePoint = driver.EyePoint;
            mirrors.Build();
            return mirrors;
        }

        void OnEnable()
        {
            Build();
            SetVisible(true);
        }

        void OnDisable() => SetVisible(false);

        void OnDestroy()
        {
            if (m_Textures != null)
                foreach (var texture in m_Textures)
                    if (texture != null) texture.Release();
            foreach (var mesh in m_Meshes)
                if (mesh != null) Destroy(mesh);
        }

        /// <summary>Cameras only render while the mirrors are in use (saves a lot of GPU in the menus).</summary>
        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (m_Root != null)
                m_Root.gameObject.SetActive(visible);
        }

        public void Build()
        {
            if (m_Built || m_EyePoint == null)
                return;
            m_Built = true;

            var driver = GetComponent<DriverController>();
            m_RearOffset = driver != null ? driver.Length * 0.5f + 0.05f : 2.3f;
            m_Root = new GameObject("Mirrors").transform;
            m_Root.SetParent(transform, false);

            m_Cameras = new Camera[m_Mirrors.Length];
            m_Textures = new RenderTexture[m_Mirrors.Length];
            m_Materials = new Material[m_Mirrors.Length];
            for (var i = 0; i < m_Mirrors.Length; i++)
                BuildMirror(i, m_Mirrors[i]);
        }

        void BuildMirror(int index, Mirror mirror)
        {
            var eye = transform.InverseTransformPoint(m_EyePoint.position);
            var direction = Quaternion.Euler(-mirror.pitch, mirror.yaw, 0f) * Vector3.forward;
            var screenPosition = eye + direction * mirror.distance;

            var holder = new GameObject(mirror.name).transform;
            holder.SetParent(m_Root, false);
            if (mirror.exact)
                screenPosition = mirror.exactPosition;
            holder.localPosition = screenPosition;
            // A Quad is seen from its -Z side: its +Z points away from the eye.
            holder.localRotation = mirror.exact ? mirror.exactRotation : Quaternion.LookRotation(direction, Vector3.up);

            // A frame behind the glass (not where the screen fills the car's own mirror, which has its own rim).
            var outline = Outline(mirror);
            GameObject frame = null;
            if (!mirror.exact)
            {
                frame = ShapeObject("Frame", outline);
                frame.transform.SetParent(holder, false);
                frame.transform.localPosition = new Vector3(0f, 0f, m_FrameThickness);
                frame.transform.localScale = new Vector3(mirror.size.x + 0.025f, mirror.size.y + 0.025f, 1f);
                var frameMaterial = NewMaterial();
                frameMaterial.color = m_FrameColor;
                frame.GetComponent<MeshRenderer>().sharedMaterial = frameMaterial;
                frame.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Glass: the rear camera's image, flipped left-right like a real mirror.
            var texture = new RenderTexture(mirror.resolution.x, mirror.resolution.y, 16) { name = mirror.name, antiAliasing = 1 };
            texture.Create();
            var screen = ShapeObject("Glass", outline);
            screen.transform.SetParent(holder, false);
            screen.transform.localScale = new Vector3(mirror.size.x, mirror.size.y, 1f);
            var material = NewMaterial();
            material.color = Color.white;
            material.mainTexture = texture;
            material.mainTextureScale = new Vector2(-1f, 1f);
            material.mainTextureOffset = new Vector2(1f, 0f);
            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Rear-facing camera.
            var cameraHolder = new GameObject(mirror.name + " Camera").transform;
            cameraHolder.SetParent(m_Root, false);
            cameraHolder.localPosition = mirror.cameraAtScreen
                ? screenPosition + Vector3.right * Mathf.Sign(mirror.lookOutward == 0f ? 1f : mirror.lookOutward) * 0.08f
                : new Vector3(0f, screenPosition.y, -m_RearOffset);
            cameraHolder.localRotation = Quaternion.Euler(0f, 180f - mirror.lookOutward, 0f);
            var camera = cameraHolder.gameObject.AddComponent<Camera>();
            camera.targetTexture = texture;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.fieldOfView = mirror.verticalFov;
            camera.nearClipPlane = 0.25f;
            camera.farClipPlane = m_FarClip;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -10f; // before the headset camera
            camera.clearFlags = CameraClearFlags.Skybox;

            m_Cameras[index] = camera;
            m_Textures[index] = texture;
            m_Materials[index] = material;
        }

        Material NewMaterial()
        {
            if (m_ScreenMaterial != null)
                return new Material(m_ScreenMaterial);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            return new Material(shader);
        }


        // ------------------------------------------------------------------ glass outline

        // Control polygons in a unit box (x right, y up, counter-clockwise), rounded off by smoothing.
        // A left wing mirror like the Bentley's: a blunt inner end, tapering to the outer tip, with a flatter bottom.
        static readonly Vector2[] k_Wing =
        {
            new Vector2(0.50f, 0.42f), new Vector2(0.15f, 0.50f), new Vector2(-0.35f, 0.32f), new Vector2(-0.50f, 0.02f),
            new Vector2(-0.30f, -0.28f), new Vector2(0.10f, -0.50f), new Vector2(0.50f, -0.40f),
        };

        static readonly Vector2[] k_Bar =
        {
            new Vector2(0.50f, 0.34f), new Vector2(0.40f, 0.50f), new Vector2(-0.40f, 0.50f), new Vector2(-0.50f, 0.34f),
            new Vector2(-0.50f, -0.34f), new Vector2(-0.40f, -0.50f), new Vector2(0.40f, -0.50f), new Vector2(0.50f, -0.34f),
        };

        /// <summary>The glass outline for a mirror, smoothed. Right-hand wing mirrors are the left one turned round.</summary>
        static List<Vector2> Outline(Mirror mirror)
        {
            if (mirror.exact && mirror.exactOutline != null && mirror.exactOutline.Length >= 3)
                return new List<Vector2>(mirror.exactOutline);
            var control = new List<Vector2>(mirror.shape == Shape.Bar ? k_Bar : k_Wing);
            if (mirror.shape == Shape.Wing && mirror.yaw > 0f)
            {
                for (var i = 0; i < control.Count; i++)
                    control[i] = new Vector2(-control[i].x, control[i].y);
                control.Reverse(); // keeps it counter-clockwise
            }

            // Chaikin corner cutting: every corner becomes a smooth curve.
            for (var pass = 0; pass < 3; pass++)
            {
                var next = new List<Vector2>(control.Count * 2);
                for (var i = 0; i < control.Count; i++)
                {
                    var a = control[i];
                    var b = control[(i + 1) % control.Count];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }

                control = next;
            }

            return control;
        }

        /// <summary>A flat mesh of the outline, seen from -Z, with UVs over the unit box (so a render texture fits it).</summary>
        GameObject ShapeObject(string name, List<Vector2> outline)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            foreach (var p in outline)
            {
                vertices.Add(new Vector3(p.x, p.y, 0f));
                uvs.Add(new Vector2(p.x + 0.5f, p.y + 0.5f));
            }

            var triangles = new List<int>();
            for (var i = 0; i < outline.Count; i++)
            {
                // Clockwise seen from -Z: that is the front face.
                triangles.Add(0);
                triangles.Add(1 + (i + 1) % outline.Count);
                triangles.Add(1 + i);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            m_Meshes.Add(mesh);

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }
    }
}
