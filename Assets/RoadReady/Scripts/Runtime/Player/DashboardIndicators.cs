using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// The two turn-signal arrows on the instrument panel: dark when off, flashing amber in step with the tick of the
    /// indicator. They are placed in front of the driver, just above the steering wheel's rim, from
    /// the eye point, so they work on any car model; nudge Pitch / Distance / Spacing if they sit in the dashboard.
    /// </summary>
    public class DashboardIndicators : MonoBehaviour
    {
        static readonly Color k_Off = new Color(0.12f, 0.12f, 0.13f);
        static readonly Color k_On = new Color(1f, 0.55f, 0f);

        [SerializeField] DriverController m_Driver;
        [SerializeField] Transform m_EyePoint;
        [Tooltip("Degrees below the eye's forward direction.")]
        [SerializeField] float m_Pitch = 16f;
        [SerializeField] float m_Distance = 0.78f;
        [Tooltip("Gap from the middle to each arrow, metres.")]
        [SerializeField] float m_Spacing = 0.075f;
        [SerializeField] float m_Size = 0.05f;

        Material m_Left, m_Right;
        Transform m_Root;

        /// <summary>Adds the arrows to a player car that does not have them yet.</summary>
        public static DashboardIndicators EnsureOn(DriverController driver)
        {
            var indicators = driver.GetComponent<DashboardIndicators>();
            if (indicators == null)
            {
                indicators = driver.gameObject.AddComponent<DashboardIndicators>();
                indicators.m_Driver = driver;
                indicators.m_EyePoint = driver.EyePoint;
                indicators.Build();
            }

            return indicators;
        }

        void Build()
        {
            if (m_EyePoint == null || m_Root != null)
                return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m_Left = new Material(shader) { name = "Dash Indicator Left", color = k_Off };
            m_Right = new Material(shader) { name = "Dash Indicator Right", color = k_Off };
            m_Left.SetFloat("_Cull", 0f);
            m_Right.SetFloat("_Cull", 0f);

            m_Root = new GameObject("Dashboard Indicators").transform;
            m_Root.SetParent(transform, false);
            var eye = transform.InverseTransformPoint(m_EyePoint.position);
            var direction = Quaternion.Euler(m_Pitch, 0f, 0f) * Vector3.forward;
            m_Root.localPosition = eye + direction * m_Distance;
            m_Root.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            Arrow("Left Arrow", -m_Spacing, true, m_Left);
            Arrow("Right Arrow", m_Spacing, false, m_Right);
        }

        void Arrow(string name, float x, bool left, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Root, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh(left);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A flat arrow in the XY plane (shaft and head), pointing right, or left when mirrored.</summary>
        Mesh ArrowMesh(bool left)
        {
            var s = m_Size;
            var sign = left ? -1f : 1f;
            // Shaft: x -0.5..0.1, y -0.15..0.15; head: a triangle from x 0.1 to the tip at 0.5.
            var points = new[]
            {
                new Vector3(-0.5f, -0.15f), new Vector3(-0.5f, 0.15f), new Vector3(0.1f, 0.15f), new Vector3(0.1f, -0.15f),
                new Vector3(0.1f, 0.4f), new Vector3(0.5f, 0f), new Vector3(0.1f, -0.4f),
            };
            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector3(points[i].x * sign * s * 1.4f, points[i].y * s * 1.4f, 0f);

            // The winding is clockwise as the driver sees it for a right-pointing arrow; mirrored it flips.
            var shaft = left ? new[] { 0, 3, 2, 0, 2, 1 } : new[] { 0, 1, 2, 0, 2, 3 };
            var head = left ? new[] { 4, 6, 5 } : new[] { 4, 5, 6 };
            var triangles = new int[shaft.Length + head.Length];
            shaft.CopyTo(triangles, 0);
            head.CopyTo(triangles, shaft.Length);
            var mesh = new Mesh { name = left ? "Indicator Left" : "Indicator Right", vertices = points, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Update()
        {
            if (m_Driver == null || m_Left == null)
                return;
            var blinkOn = Mathf.Repeat(Time.time, 0.7f) < 0.35f;
            m_Left.color = m_Driver.Indicator == Core.TurnSide.Left && blinkOn ? k_On : k_Off;
            m_Right.color = m_Driver.Indicator == Core.TurnSide.Right && blinkOn ? k_On : k_Off;
        }

        void OnDestroy()
        {
            if (m_Left != null) Destroy(m_Left);
            if (m_Right != null) Destroy(m_Right);
        }
    }
}
