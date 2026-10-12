using RoadReady.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RoadReady.Player
{
    /// <summary>
    /// Automatic gear lever (P-R-N-D, front to back). Grab the knob with the grip button and move your hand forward or
    /// back: the lever follows the hand, the gear changes the moment it passes a detent (with a haptic click), and on
    /// release it snaps into that detent. The G key / DriverController.SetGear move it too.
    /// </summary>
    public class GearShifter : XRBaseInteractable
    {
        [SerializeField] DriverController m_Driver;
        [Tooltip("Lever pivot at the base; it tilts about its local X axis.")]
        [SerializeField] Transform m_Lever;
        [Tooltip("Lever tilt for P, R, N, D in degrees (positive = knob forward).")]
        [SerializeField] float[] m_Detents = { 24f, 9f, -6f, -21f };
        [SerializeField] float m_SnapSpeed = 400f;
        [Tooltip("P, R, N, D letters beside the lever; the engaged gear is lit.")]
        [SerializeField] TMP_Text[] m_Labels = System.Array.Empty<TMP_Text>();
        [SerializeField] Color m_LabelOn = new Color(0.98f, 0.82f, 0.004f);
        [SerializeField] Color m_LabelOff = new Color(0.5f, 0.53f, 0.56f);

        float m_Angle;

        public void Configure(DriverController driver, Transform lever, float[] detents, TMP_Text[] labels)
        {
            m_Driver = driver;
            m_Lever = lever;
            m_Detents = detents;
            m_Labels = labels;
        }

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Single;
        }

        void Start()
        {
            if (m_Driver != null)
                m_Angle = Detent(m_Driver.Gear);
            Apply();
            UpdateLabels(m_Driver != null ? m_Driver.Gear : Gear.Park);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (m_Driver != null) m_Driver.GearChanged += UpdateLabels;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (m_Driver != null) m_Driver.GearChanged -= UpdateLabels;
        }

        void UpdateLabels(Gear gear)
        {
            for (var i = 0; i < m_Labels.Length; i++)
                if (m_Labels[i] != null)
                    m_Labels[i].color = i == (int)gear ? m_LabelOn : m_LabelOff;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            Buzz(args.interactorObject, 0.25f, 0.04f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || m_Driver == null || m_Lever == null)
                return;

            if (isSelected)
            {
                // Hand position in the (non-tilting) base frame -> lever angle.
                var interactor = interactorsSelecting[0];
                var hand = m_Lever.parent.InverseTransformPoint(interactor.GetAttachTransform(this).position);
                var angle = Mathf.Atan2(hand.z, Mathf.Max(0.05f, hand.y)) * Mathf.Rad2Deg;
                m_Angle = Mathf.Clamp(angle, m_Detents[m_Detents.Length - 1] - 4f, m_Detents[0] + 4f);
                var gear = Nearest(m_Angle);
                if (gear != m_Driver.Gear)
                {
                    m_Driver.SetGear(gear);
                    Buzz(interactor, 0.7f, 0.06f);
                }
            }
            else
            {
                m_Angle = Mathf.MoveTowards(m_Angle, Detent(m_Driver.Gear), m_SnapSpeed * Time.deltaTime);
            }

            Apply();
        }

        float Detent(Gear gear) => m_Detents[Mathf.Clamp((int)gear, 0, m_Detents.Length - 1)];

        Gear Nearest(float angle)
        {
            var best = 0;
            for (var i = 1; i < m_Detents.Length; i++)
                if (Mathf.Abs(angle - m_Detents[i]) < Mathf.Abs(angle - m_Detents[best]))
                    best = i;
            return (Gear)best;
        }

        void Apply()
        {
            if (m_Lever != null)
                m_Lever.localRotation = Quaternion.Euler(m_Angle, 0f, 0f);
        }

        static void Buzz(IXRSelectInteractor interactor, float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }
    }
}
