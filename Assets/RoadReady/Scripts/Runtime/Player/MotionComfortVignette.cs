using RoadReady.Config;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace RoadReady.Player
{
    /// <summary>
    /// Feeds vehicle / assisted-walk motion into XRI's TunnelingVignetteController so the vignette closes in
    /// during acceleration, braking and turning (NFR safety and comfort).
    /// </summary>
    public class MotionComfortVignette : MonoBehaviour, ITunnelingVignetteProvider
    {
        [SerializeField] float m_AccelerationThreshold = 1.2f;
        [SerializeField] float m_YawRateThreshold = 15f;

        readonly VignetteParameters m_Parameters = new VignetteParameters();
        TunnelingVignetteController m_Controller;
        ComfortSettings m_Comfort;
        bool m_Active;

        public VignetteParameters vignetteParameters => m_Parameters;

        public DriverController Driver { get; set; }
        public PedestrianController Pedestrian { get; set; }

        public void Initialize(TunnelingVignetteController controller, ComfortSettings comfort)
        {
            m_Controller = controller;
            m_Comfort = comfort;
            m_Parameters.vignetteColor = Color.black;
            m_Parameters.vignetteColorBlend = Color.black;
            m_Parameters.apertureVerticalPosition = 0f;
            m_Parameters.easeOutDelayTime = 0f;
            ApplyComfort(comfort);
        }

        public void ApplyComfort(ComfortSettings comfort)
        {
            m_Comfort = comfort;
            m_Parameters.apertureSize = Mathf.Lerp(0.95f, 0.45f, comfort.vignetteStrength);
            m_Parameters.featheringEffect = 0.25f;
            m_Parameters.easeInTime = 0.25f;
            m_Parameters.easeOutTime = 0.4f;
        }

        void Update()
        {
            var wanted = m_Controller != null && m_Comfort != null && m_Comfort.vignetteEnabled && IsMoving();
            if (wanted == m_Active)
                return;
            m_Active = wanted;
            if (wanted) m_Controller.BeginTunnelingVignette(this);
            else m_Controller.EndTunnelingVignette(this);
        }

        bool IsMoving()
        {
            if (Driver != null && Driver.isActiveAndEnabled)
                return Mathf.Abs(Driver.Acceleration) > m_AccelerationThreshold || Mathf.Abs(Driver.YawRateDegrees) > m_YawRateThreshold;
            if (Pedestrian != null && Pedestrian.isActiveAndEnabled)
                return Pedestrian.IsAutoWalking;
            return false;
        }

        void OnDisable()
        {
            if (m_Active && m_Controller != null)
                m_Controller.EndTunnelingVignette(this);
            m_Active = false;
        }
    }
}
