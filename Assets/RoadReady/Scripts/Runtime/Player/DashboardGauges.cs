using System;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// Drives the needles of an imported car's instrument cluster from the DriverController. Each needle turns
    /// from its modelled rest pose (assumed to read zero) around the axis pointing out of the dial.
    /// Tune Degrees At Max / Zero Offset in the Inspector if a needle does not line up with the printed dial.
    /// </summary>
    [RequireComponent(typeof(DriverController))]
    public class DashboardGauges : MonoBehaviour
    {
        public enum Source { Speed, Rpm, Fixed }

        [Serializable]
        public class Needle
        {
            public Transform needle;
            public Source source;
            [Tooltip("Local axis pointing out of the dial, towards the driver.")]
            public Vector3 axis = Vector3.forward;
            [Tooltip("Value at the end of the scale (km/h for speed, rpm for the rev counter).")]
            public float maxValue = 250f;
            [Tooltip("Sweep from zero to Max Value, in degrees (clockwise as seen by the driver).")]
            public float degreesAtMax = 260f;
            [Tooltip("Extra rotation if the modelled rest pose is not exactly on zero.")]
            public float zeroOffset;
            [Tooltip("Value shown when Source = Fixed (e.g. fuel half full).")]
            public float fixedValue;
            [Tooltip("How quickly the needle follows the value (higher = snappier).")]
            public float response = 6f;

            [NonSerialized] public Quaternion rest;
            [NonSerialized] public float shown;
        }

        [SerializeField] Needle[] m_Needles = Array.Empty<Needle>();

        DriverController m_Driver;

        public Needle[] Needles
        {
            get => m_Needles;
            set => m_Needles = value ?? Array.Empty<Needle>();
        }

        void Awake()
        {
            m_Driver = GetComponent<DriverController>();
            foreach (var n in m_Needles)
                if (n.needle != null)
                    n.rest = n.needle.localRotation;
        }

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            foreach (var n in m_Needles)
            {
                if (n.needle == null || n.maxValue <= 0f)
                    continue;
                var value = n.source switch
                {
                    Source.Speed => m_Driver.SpeedKmh,
                    Source.Rpm => m_Driver.Rpm,
                    _ => n.fixedValue,
                };
                n.shown = Mathf.Lerp(n.shown, Mathf.Clamp(value, 0f, n.maxValue), 1f - Mathf.Exp(-n.response * dt));
                // Positive rotation about an axis that points at the viewer looks anticlockwise, hence the minus.
                var angle = -(n.shown / n.maxValue * n.degreesAtMax) - n.zeroOffset;
                n.needle.localRotation = n.rest * Quaternion.AngleAxis(angle, n.axis);
            }
        }
    }
}
