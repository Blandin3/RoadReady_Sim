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
            [Tooltip("When ticked the needle turns about Pivot (in its parent's space, the dial's centre) instead of its own origin.")]
            public bool usePivot;
            public Vector3 pivot;
            [Tooltip("Value at the start of the scale (0 for speed and rpm, 50 for a temperature gauge that starts at 50).")]
            public float minValue;
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
            [NonSerialized] public Vector3 restPosition;
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
                {
                    n.rest = n.needle.localRotation;
                    n.restPosition = n.needle.localPosition;
                }
        }

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            foreach (var n in m_Needles)
            {
                if (n.needle == null || n.maxValue <= n.minValue)
                    continue;
                var value = n.source switch
                {
                    Source.Speed => m_Driver.SpeedKmh,
                    Source.Rpm => m_Driver.Rpm,
                    _ => n.fixedValue,
                };
                n.shown = Mathf.Lerp(n.shown, Mathf.Clamp(value, n.minValue, n.maxValue), 1f - Mathf.Exp(-n.response * dt));
                Pose(n, n.shown);
            }
        }

        /// <summary>Turns a needle to read <paramref name="value"/>, from its rest pose.</summary>
        public static void Pose(Needle n, float value)
        {
            // Unity is left-handed: a positive rotation about an axis that points at the viewer turns clockwise.
            var angle = (value - n.minValue) / (n.maxValue - n.minValue) * n.degreesAtMax + n.zeroOffset;
            var rotation = n.rest * Quaternion.AngleAxis(angle, n.axis);
            if (n.usePivot)
                n.needle.localPosition = n.pivot + rotation * Quaternion.Inverse(n.rest) * (n.restPosition - n.pivot);
            n.needle.localRotation = rotation;
        }
    }
}
