using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RoadReady.Player
{
    /// <summary>
    /// Steering wheel you hold with one or both hands (grip button). Each frame the hands' movement round the wheel
    /// axis turns the wheel; the wheel angle becomes the car's steering input (full lock = ±450°, the same ratio the
    /// rim already uses). When released it hands control back to the thumbstick, so nothing jumps.
    /// </summary>
    public class SteeringWheelGrab : XRBaseInteractable
    {
        [Tooltip("The rotating wheel (its spin axis is Local Axis).")]
        [SerializeField] Transform m_Hub;
        [SerializeField] Vector3 m_LocalAxis = Vector3.forward;
        [Tooltip("Wheel rotation for full steering lock, in degrees.")]
        [SerializeField] float m_LockDegrees = 450f;

        readonly Dictionary<IXRSelectInteractor, Vector3> m_LastHand = new Dictionary<IXRSelectInteractor, Vector3>();
        float m_Angle;
        bool m_AtLock;

        public bool IsHeld => isSelected;
        /// <summary>Wheel rotation in degrees, positive = turned right (clockwise as the driver sees it).</summary>
        public float WheelAngle => m_Angle;
        public float SteerInput => Mathf.Clamp(m_Angle / m_LockDegrees, -1f, 1f);

        public void Configure(Transform hub, Vector3 localAxis, float lockDegrees)
        {
            m_Hub = hub;
            m_LocalAxis = localAxis;
            m_LockDegrees = lockDegrees;
        }

        /// <summary>Keeps the wheel in step with thumbstick steering while nobody holds it.</summary>
        public void FollowSteering(float steer)
        {
            if (!isSelected)
                m_Angle = Mathf.Clamp(steer, -1f, 1f) * m_LockDegrees;
        }

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Multiple;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_LastHand[args.interactorObject] = HandOnWheelPlane(args.interactorObject);
            Buzz(args.interactorObject, 0.25f, 0.04f);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            m_LastHand.Remove(args.interactorObject);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !isSelected || m_Hub == null)
                return;

            // Average the rotation of every hand on the wheel since last frame.
            var axis = Axis;
            var sum = 0f;
            var count = 0;
            foreach (var interactor in interactorsSelecting)
            {
                var now = HandOnWheelPlane(interactor);
                if (m_LastHand.TryGetValue(interactor, out var before) && before.sqrMagnitude > 1e-4f && now.sqrMagnitude > 1e-4f)
                {
                    sum += Vector3.SignedAngle(before, now, axis);
                    count++;
                }

                m_LastHand[interactor] = now;
            }

            if (count == 0)
                return;
            // SignedAngle is positive anticlockwise as the driver sees it; turning right is clockwise.
            m_Angle = Mathf.Clamp(m_Angle - sum / count, -m_LockDegrees, m_LockDegrees);
            var atLock = Mathf.Abs(m_Angle) >= m_LockDegrees - 0.5f;
            if (atLock && !m_AtLock)
                foreach (var interactor in interactorsSelecting)
                    Buzz(interactor, 0.5f, 0.08f);
            m_AtLock = atLock;
        }

        Vector3 Axis => m_Hub.TransformDirection(m_LocalAxis).normalized;

        Vector3 HandOnWheelPlane(IXRSelectInteractor interactor)
        {
            var hand = interactor.GetAttachTransform(this).position;
            return Vector3.ProjectOnPlane(hand - m_Hub.position, Axis);
        }

        static void Buzz(IXRSelectInteractor interactor, float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }
    }
}
