using System;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// The warning-light icons of an imported car's instrument cluster, lit from the DriverController: parking brake
    /// while the handbrake is on, the left and right turn arrows in step with the indicator, the dipped-beam lamp while the engine runs.
    /// </summary>
    [RequireComponent(typeof(DriverController))]
    public class DashboardWarningLights : MonoBehaviour
    {
        public enum Condition { Never, EngineOn, HandbrakeOn, IndicatorLeft, IndicatorRight }

        [Serializable]
        public class Lamp
        {
            public Renderer renderer;
            public Condition condition;
            public Color onColor = new Color(0.1f, 0.9f, 0.2f);
        }

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Lamp[] m_Lamps = Array.Empty<Lamp>();

        DriverController m_Driver;
        MaterialPropertyBlock m_Block;
        bool[] m_Shown;

        public Lamp[] Lamps
        {
            get => m_Lamps;
            set => m_Lamps = value ?? Array.Empty<Lamp>();
        }

        void Awake()
        {
            m_Driver = GetComponent<DriverController>();
            m_Block = new MaterialPropertyBlock();
            m_Shown = new bool[m_Lamps.Length];
            for (var i = 0; i < m_Lamps.Length; i++)
                Show(i, false, true);
        }

        void Update()
        {
            var blinkOn = Mathf.Repeat(Time.time, 0.7f) < 0.35f;
            for (var i = 0; i < m_Lamps.Length; i++)
            {
                var on = m_Lamps[i].condition switch
                {
                    Condition.EngineOn => m_Driver.EngineOn,
                    Condition.HandbrakeOn => m_Driver.Handbrake,
                    Condition.IndicatorLeft => m_Driver.Indicator == Core.TurnSide.Left && blinkOn,
                    Condition.IndicatorRight => m_Driver.Indicator == Core.TurnSide.Right && blinkOn,
                    _ => false,
                };
                Show(i, on, false);
            }
        }

        void Show(int index, bool on, bool force)
        {
            var lamp = m_Lamps[index];
            if (lamp.renderer == null || (!force && m_Shown[index] == on))
                return;
            m_Shown[index] = on;
            var colour = on ? lamp.onColor : new Color(0.12f, 0.12f, 0.13f);
            lamp.renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(k_BaseColor, colour);
            m_Block.SetColor(k_EmissionColor, on ? lamp.onColor * 1.5f : Color.black);
            lamp.renderer.SetPropertyBlock(m_Block);
        }
    }
}
