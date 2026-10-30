using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RoadReady.UI
{
    /// <summary>
    /// A blue bar just above the menu panel that the player can grab with the controller (point the ray at it and
    /// hold the grip / trigger) to carry the menu wherever they see it best. The panel itself keeps taking ray clicks;
    /// only the bar is grabbable, so grabbing never clicks a button by accident. The panel always turns to face the
    /// head, and the spot is remembered (relative to the head) for the next time a menu opens.
    /// </summary>
    public class MenuGrabHandle : MonoBehaviour
    {
        const float k_Gap = 0.04f;        // metres between the panel's top edge and the bar
        const float k_BarHeight = 0.05f;
        const float k_BarDepth = 0.025f;

        WorldSpacePanel m_Panel;
        Func<Transform> m_Head;
        XRGrabInteractable m_Grab;
        Renderer m_Renderer;
        Collider m_Collider;
        Vector3 m_PanelInHandle;

        /// <summary>True once the player has moved the menu; <see cref="HeadOffset"/> is then where they want it.</summary>
        public bool HasCustomPosition { get; private set; }

        /// <summary>The wanted panel position, in the head's yaw-only frame (x right, y up, z forward).</summary>
        public Vector3 HeadOffset { get; private set; }

        public static MenuGrabHandle Create(WorldSpacePanel panel, Func<Transform> head)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Menu Grab Handle";
            var handle = bar.AddComponent<MenuGrabHandle>();
            handle.m_Panel = panel;
            handle.m_Head = head;
            return handle;
        }

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Collider = GetComponent<Collider>();
            m_Renderer.material.color = new Color32(0, 161, 222, 255);
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_Grab = gameObject.AddComponent<XRGrabInteractable>();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            m_Grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            m_Grab.trackRotation = false;
            m_Grab.throwOnDetach = false;
            m_Grab.selectEntered.AddListener(_ => m_PanelInHandle = transform.InverseTransformPoint(m_Panel.transform.position));
            m_Grab.selectExited.AddListener(_ => Remember());
            SetShown(false);
        }

        void LateUpdate()
        {
            if (m_Panel == null)
                return;
            var shown = m_Panel.Visible;
            if (!shown)
            {
                if (m_Renderer.enabled)
                    SetShown(false);
                return;
            }

            if (!m_Renderer.enabled)
                SetShown(true);

            var head = m_Head != null ? m_Head() : null;
            if (m_Grab.isSelected)
            {
                // The panel travels with the bar and keeps facing the player.
                m_Panel.PlaceAt(transform.TransformPoint(m_PanelInHandle), head != null ? head.position : transform.position);
                return;
            }

            // Resting: the bar sits above the panel's top edge, as wide as the panel's width allows.
            var panel = m_Panel.transform;
            var scale = panel.lossyScale.y;
            var half = m_Panel.SizePixels.y * scale * 0.5f;
            var width = Mathf.Min(0.45f, m_Panel.SizePixels.x * scale * 0.4f);
            transform.SetPositionAndRotation(panel.position + panel.up * (half + k_Gap + k_BarHeight * 0.5f), panel.rotation);
            transform.localScale = new Vector3(width, k_BarHeight, k_BarDepth);
        }

        void SetShown(bool shown)
        {
            m_Renderer.enabled = shown;
            m_Collider.enabled = shown;
        }

        void Remember()
        {
            var head = m_Head != null ? m_Head() : null;
            if (head == null)
                return;
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            HeadOffset = Quaternion.Inverse(yaw) * (m_Panel.transform.position - head.position);
            HasCustomPosition = true;
        }

        /// <summary>The place the menu should open at now, from the remembered offset and where the head faces.</summary>
        public Vector3 CustomPosition(Transform head)
        {
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            return head.position + Quaternion.LookRotation(forward.normalized, Vector3.up) * HeadOffset;
        }
    }
}
