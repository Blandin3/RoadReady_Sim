using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// A world-space UI Toolkit panel (Unity 6.2+ <see cref="PanelRenderMode.WorldSpace"/>). XR rays reach it through
    /// XRI's XRUIToolkitManager, which requires a collider on the UIDocument's GameObject; this component makes
    /// sure one exists and handles placement in front of the learner.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class WorldSpacePanel : MonoBehaviour
    {
        [Tooltip("Panel size in UI pixels.")]
        [SerializeField] Vector2 m_SizePixels = new Vector2(1600f, 1000f);
        [Tooltip("World metres per UI pixel (0.001 = 1 mm per pixel).")]
        [SerializeField] float m_MetersPerPixel = 0.001f;
        [Tooltip("Receives XR ray / mouse input.")]
        [SerializeField] bool m_Interactive = true;
        [Tooltip("Flip if text appears mirrored after an engine upgrade.")]
        [SerializeField] bool m_FlipFacing;

        UIDocument m_Document;
        BoxCollider m_Collider;
        float m_TextScale = 1f;
        float m_ScaleMultiplier = 1f;
        bool m_Visible = true;

        public UIDocument Document => m_Document != null ? m_Document : m_Document = GetComponent<UIDocument>();
        public VisualElement Root => Document.rootVisualElement;
        public bool Visible => m_Visible;
        public Vector2 SizePixels => m_SizePixels;

        public void Configure(Vector2 sizePixels, float metersPerPixel, bool interactive)
        {
            m_SizePixels = sizePixels;
            m_MetersPerPixel = metersPerPixel;
            m_Interactive = interactive;
        }

        void Awake()
        {
            m_Document = GetComponent<UIDocument>();
            m_Document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            m_Document.worldSpaceSize = m_SizePixels;
            ApplyScale();
        }

        IEnumerator Start()
        {
            // UI Toolkit may create its own collider when the panel attaches; only add one if it did not.
            yield return null;
            if (!m_Interactive)
                yield break;
            if (!TryGetComponent<Collider>(out var existing))
            {
                m_Collider = gameObject.AddComponent<BoxCollider>();
                m_Collider.size = new Vector3(m_SizePixels.x, m_SizePixels.y, 1f);
                m_Collider.center = Vector3.zero;
                existing = m_Collider;
            }

            existing.enabled = m_Visible;
        }

        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            if (Root != null)
                Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (TryGetComponent<Collider>(out var col))
                col.enabled = visible && m_Interactive;
        }

        public void SetTextScale(float scale)
        {
            m_TextScale = Mathf.Clamp(scale, 0.8f, 1.6f);
            ApplyScale();
        }

        /// <summary>Extra size multiplier, e.g. smaller when shown close to the face inside the car.</summary>
        public void SetScaleMultiplier(float multiplier)
        {
            m_ScaleMultiplier = multiplier;
            ApplyScale();
        }

        void ApplyScale()
        {
            var s = m_MetersPerPixel * m_TextScale * m_ScaleMultiplier;
            transform.localScale = new Vector3(s, s, s);
        }

        /// <summary>Places the panel in front of the head (yaw only, so it stays upright).</summary>
        public void PlaceInFront(Transform head, float distance, float verticalOffset = -0.1f)
        {
            if (head == null)
                return;
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();
            var position = head.position + forward * distance + Vector3.up * verticalOffset;
            PlaceAt(position, head.position);
        }

        public void PlaceAt(Vector3 position, Vector3 viewerPosition)
        {
            var away = position - viewerPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
                away = Vector3.forward;
            var rotation = Quaternion.LookRotation(m_FlipFacing ? -away : away, Vector3.up);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
