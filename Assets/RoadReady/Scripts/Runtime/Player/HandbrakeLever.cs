using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RoadReady.Player
{
    /// <summary>
    /// The car's own handbrake lever. Grab the handle (grip button) and pull it up to apply the handbrake, or push it
    /// back down to release it: the lever follows the hand, the brake switches (with a click) half-way through the
    /// travel, and on release the lever settles fully up or fully down. The B key / DriverController.SetHandbrake
    /// move it too.
    /// </summary>
    public class HandbrakeLever : XRBaseInteractable
    {
        [SerializeField] DriverController m_Driver;
        [Tooltip("Pivot at the rear of the lever; it tilts about its local X axis (positive = handle up).")]
        [SerializeField] Transform m_Lever;
        [Tooltip("Lever travel in degrees from down (released) to up (applied).")]
        [SerializeField] float m_MaxAngle = 20f;
        [SerializeField] float m_SnapSpeed = 160f;

        float m_Angle;
        float m_GrabOffset;

        public void Configure(DriverController driver, Transform lever, float maxAngle)
        {
            m_Driver = driver;
            m_Lever = lever;
            m_MaxAngle = maxAngle;
        }

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Single;
        }

        void Start()
        {
            if (m_Driver != null)
                m_Angle = m_Driver.Handbrake ? m_MaxAngle : 0f;
            Apply();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            // Keep the lever where it is at the moment of grabbing (no jump to the hand).
            m_GrabOffset = m_Angle - HandAngle(args.interactorObject);
            Buzz(args.interactorObject, 0.25f, 0.04f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || m_Driver == null || m_Lever == null)
                return;

            if (isSelected)
            {
                var interactor = interactorsSelecting[0];
                m_Angle = Mathf.Clamp(HandAngle(interactor) + m_GrabOffset, 0f, m_MaxAngle);
                var applied = m_Angle > m_MaxAngle * 0.5f;
                if (applied != m_Driver.Handbrake)
                {
                    m_Driver.SetHandbrake(applied);
                    Buzz(interactor, 0.8f, 0.07f);
                }
            }
            else
            {
                m_Angle = Mathf.MoveTowards(m_Angle, m_Driver.Handbrake ? m_MaxAngle : 0f, m_SnapSpeed * Time.deltaTime);
            }

            Apply();
        }

        /// <summary>Angle of the hand above the lever's rest line, seen from the side (pivot frame, not tilting).</summary>
        float HandAngle(IXRInteractor interactor)
        {
            var hand = m_Lever.parent.InverseTransformPoint(interactor.GetAttachTransform(this).position) - m_Lever.localPosition;
            return Mathf.Atan2(hand.y, Mathf.Max(0.02f, hand.z)) * Mathf.Rad2Deg;
        }

        void Apply()
        {
            if (m_Lever != null)
                m_Lever.localRotation = Quaternion.Euler(-m_Angle, 0f, 0f);
        }

        static void Buzz(IXRInteractor interactor, float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }
    }
}
