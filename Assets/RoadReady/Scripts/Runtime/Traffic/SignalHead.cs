using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>
    /// Visual for one signal head. Lights each lamp renderer on/off from the controller state; swap the
    /// greybox spheres for real lamp meshes (keep one renderer per lamp) when final assets arrive.
    /// For pedestrian heads, use Red = "don't walk" and Green = "walk"; Amber flashes the green lamp.
    /// </summary>
    public class SignalHead : MonoBehaviour
    {
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] TrafficSignalController m_Controller;
        [SerializeField] string m_Group = "ns";
        [SerializeField] bool m_IsPedestrianHead;
        [SerializeField] Renderer m_RedLamp;
        [SerializeField] Renderer m_AmberLamp;
        [SerializeField] Renderer m_GreenLamp;
        [SerializeField] Color m_OffColor = new Color(0.08f, 0.08f, 0.08f);

        MaterialPropertyBlock m_Block;
        SignalState m_Last = (SignalState)(-1);
        bool m_FlashOn;

        public void Configure(TrafficSignalController controller, string group, bool pedestrian, Renderer red, Renderer amber, Renderer green)
        {
            m_Controller = controller;
            m_Group = group;
            m_IsPedestrianHead = pedestrian;
            m_RedLamp = red;
            m_AmberLamp = amber;
            m_GreenLamp = green;
        }

        void Update()
        {
            if (m_Controller == null)
                return;
            var state = m_Controller.GetState(m_Group);
            var flash = m_IsPedestrianHead && state == SignalState.Amber && Mathf.Repeat(Time.time, 1f) < 0.5f;
            if (state == m_Last && flash == m_FlashOn)
                return;
            m_Last = state;
            m_FlashOn = flash;

            if (m_IsPedestrianHead)
            {
                Set(m_RedLamp, state == SignalState.Red, Color.red);
                Set(m_GreenLamp, state == SignalState.Green || flash, Color.green);
                Set(m_AmberLamp, false, Color.yellow);
            }
            else
            {
                Set(m_RedLamp, state == SignalState.Red, Color.red);
                Set(m_AmberLamp, state == SignalState.Amber, new Color(1f, 0.6f, 0f));
                Set(m_GreenLamp, state == SignalState.Green, Color.green);
            }
        }

        void Set(Renderer lamp, bool on, Color color)
        {
            if (lamp == null)
                return;
            m_Block ??= new MaterialPropertyBlock();
            lamp.GetPropertyBlock(m_Block);
            m_Block.SetColor(k_BaseColor, on ? color : m_OffColor);
            m_Block.SetColor(k_EmissionColor, on ? color * 3f : Color.black);
            lamp.SetPropertyBlock(m_Block);
        }
    }
}
