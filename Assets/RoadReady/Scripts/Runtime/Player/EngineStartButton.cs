using UnityEngine;
using RoadReady.Core;

namespace RoadReady.Player
{
    /// <summary>
    /// The ON button: starts the engine. The car does not move until it has been pressed (and it can only be switched
    /// off once the car is stopped). While the engine is off the button is pointed out so the driver sees it straight
    /// away: a bobbing arrow above it and a pulsing amber ring. Once the engine runs, the ring stays green.
    /// </summary>
    [RequireComponent(typeof(CarButton))]
    public class EngineStartButton : MonoBehaviour
    {
        static readonly Color k_Attention = new Color(1f, 0.6f, 0.05f);

        [SerializeField] DriverController m_Driver;
        [Tooltip("Arrow that points down at the button while the engine is off.")]
        [SerializeField] Transform m_Pointer;
        [SerializeField] float m_BobHeight = 0.01f;
        CarButton m_Button;
        Vector3 m_PointerRest;

        public void Configure(DriverController driver, Transform pointer = null)
        {
            m_Driver = driver;
            m_Pointer = pointer;
        }

        void Awake()
        {
            if (m_Pointer != null)
                m_PointerRest = m_Pointer.localPosition;
        }

        void OnEnable()
        {
            m_Button = GetComponent<CarButton>();
            m_Button.Pressed += OnPressed;
            if (m_Driver != null)
                m_Driver.EngineChanged += OnEngineChanged;
            Refresh();
        }

        void OnDisable()
        {
            m_Button.Pressed -= OnPressed;
            if (m_Driver != null)
                m_Driver.EngineChanged -= OnEngineChanged;
        }

        void OnPressed()
        {
            if (m_Driver == null)
                return;
            if (m_Driver.EngineOn && m_Driver.SpeedKmh > 1f)
            {
                RoadReadyEvents.Toast("Stop the car before switching the engine off", 2.5f);
                return;
            }

            m_Driver.SetEngine(!m_Driver.EngineOn);
        }

        void OnEngineChanged(bool on) => Refresh();

        void Refresh()
        {
            if (m_Button != null && m_Driver != null)
                m_Button.SetLit(m_Driver.EngineOn);
            UpdatePointer();
        }

        void Update()
        {
            if (m_Driver == null)
                return;
            if (!m_Driver.EngineOn)
            {
                // Ring breathes while the engine is off: this is the first thing to press.
                m_Button.SetLamp(k_Attention, 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f)));
                if (m_Pointer != null)
                    m_Pointer.localPosition = m_PointerRest + Vector3.up * (Mathf.Sin(Time.unscaledTime * 5f) * m_BobHeight);
            }
        }

        void UpdatePointer()
        {
            if (m_Pointer != null && m_Driver != null)
                m_Pointer.gameObject.SetActive(!m_Driver.EngineOn);
        }
    }
}
