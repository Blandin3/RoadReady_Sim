using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadReady.Player
{
    /// <summary>
    /// Driving hands built from the XR Hands sample hand models (the same meshes the hand-tracking prefabs use).
    /// While driving, the controller models are hidden and a hand sits on each controller, palm on the grip; its
    /// finger joints curl with the grip value, so grabbing the wheel or gear lever closes the hand. A hand is only
    /// shown while its controller is tracked (put the controllers down and the hand-tracking hands take over).
    /// </summary>
    public class DriverHands : MonoBehaviour
    {
        class Joint
        {
            public Transform bone;
            public Quaternion rest;
            public float maxDegrees;
        }

        class Hand
        {
            public Transform root;
            public readonly List<Joint> joints = new List<Joint>();
            public InputAction grip, tracked;
            public float curl;
        }

        // Controller grip pose (OpenXR): origin in the palm, +Z along the fingers. The hand model has its palm
        // facing down, so it is rolled 90° to face the grip, and moved so its palm joint sits at the origin.
        static readonly Vector3 k_PalmOffset = new Vector3(0.025f, 0f, 0f);
        const float k_RestCurl = 0.15f;
        const float k_Pitch = 45f;

        readonly List<Hand> m_Hands = new List<Hand>();
        readonly List<Renderer> m_ControllerRenderers = new List<Renderer>();
        bool m_Shown;

        public void Setup(Transform leftController, Transform rightController, GameObject leftModel, GameObject rightModel, Material material)
        {
            if (m_Hands.Count > 0)
                return;
            foreach (var controller in new[] { leftController, rightController })
            {
                if (controller == null) continue;
                foreach (var r in controller.GetComponentsInChildren<Renderer>(true))
                    if (r is MeshRenderer || r is SkinnedMeshRenderer)
                        m_ControllerRenderers.Add(r);
            }

            if (leftController != null && leftModel != null)
                m_Hands.Add(Build(leftController, leftModel, material, -1f, "LeftHand"));
            if (rightController != null && rightModel != null)
                m_Hands.Add(Build(rightController, rightModel, material, 1f, "RightHand"));
            Show(false);
        }

        public void Show(bool show)
        {
            m_Shown = show;
            foreach (var hand in m_Hands)
            {
                hand.root.gameObject.SetActive(show);
                if (show) { hand.grip.Enable(); hand.tracked.Enable(); }
                else { hand.grip.Disable(); hand.tracked.Disable(); }
            }

            foreach (var r in m_ControllerRenderers)
                if (r != null) r.enabled = !show;
        }

        void Update()
        {
            if (!m_Shown)
                return;
            foreach (var hand in m_Hands)
            {
                // Desktop / simulator: no tracking flag -> keep the hand visible.
                var trackedControl = hand.tracked.activeControl;
                var visible = trackedControl == null || hand.tracked.ReadValue<float>() > 0.5f;
                if (hand.root.gameObject.activeSelf != visible)
                    hand.root.gameObject.SetActive(visible);

                var target = Mathf.Lerp(k_RestCurl, 1f, hand.grip.ReadValue<float>());
                hand.curl = Mathf.MoveTowards(hand.curl, target, Time.deltaTime * 8f);
                foreach (var joint in hand.joints)
                    joint.bone.localRotation = joint.rest * Quaternion.Euler(joint.maxDegrees * hand.curl, 0f, 0f);
            }
        }

        Hand Build(Transform controller, GameObject model, Material material, float side, string device)
        {
            var hand = new Hand();
            var instance = Instantiate(model, controller, false);
            instance.name = side > 0f ? "Right XR Hand" : "Left XR Hand";
            hand.root = instance.transform;
            // Pitched down so the handle (grip +Z) runs across the fingers, the way a controller is held.
            hand.root.localRotation = Quaternion.Euler(k_Pitch, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f * side);

            var prefix = side > 0f ? "R_" : "L_";
            Transform palm = null;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == prefix + "Palm") palm = t;
                var max = t.name.Contains("Thumb")
                    ? t.name.EndsWith("Proximal") ? 25f : t.name.EndsWith("Distal") ? 35f : 0f
                    : t.name.EndsWith("Proximal") ? 70f : t.name.EndsWith("Intermediate") ? 85f : t.name.EndsWith("Distal") ? 45f : 0f;
                if (max > 0f && t.name.StartsWith(prefix))
                    hand.joints.Add(new Joint { bone = t, rest = t.localRotation, maxDegrees = max });
            }

            // Put the palm joint on the grip pose origin, slightly outside the handle.
            var palmLocal = palm != null ? controller.InverseTransformPoint(palm.position) - hand.root.localPosition : Vector3.zero;
            hand.root.localPosition = new Vector3(k_PalmOffset.x * side, k_PalmOffset.y, k_PalmOffset.z) - palmLocal;

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (material != null) r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            foreach (var c in instance.GetComponentsInChildren<Collider>(true))
                Destroy(c); // the hands must not block the grab casts

            hand.grip = new InputAction(instance.name + " grip", InputActionType.Value, $"<XRController>{{{device}}}/{{Grip}}", expectedControlType: "Axis");
            hand.tracked = new InputAction(instance.name + " tracked", InputActionType.Value, $"<XRController>{{{device}}}/isTracked", expectedControlType: "Button");
            return hand;
        }
    }
}
