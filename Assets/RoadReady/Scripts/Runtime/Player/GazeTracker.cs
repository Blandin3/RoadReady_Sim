using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// Head-gaze tracking (Quest 2 has no eye tracking). Provides "is the learner looking at X" for hazard
    /// perception and a short yaw history for look-both-ways and blind-spot checks.
    /// </summary>
    public class GazeTracker : MonoBehaviour
    {
        const float k_HistorySeconds = 12f;

        struct Sample
        {
            public float time;
            public float worldYaw;
        }

        [SerializeField] Transform m_Head;

        readonly List<Sample> m_History = new List<Sample>(1024);

        public Transform Head { get => m_Head; set => m_Head = value; }
        public Vector3 HeadPosition => m_Head != null ? m_Head.position : transform.position;
        public Vector3 HeadForward => m_Head != null ? m_Head.forward : transform.forward;
        public float HeadWorldYaw => Mathf.Atan2(HeadForward.x, HeadForward.z) * Mathf.Rad2Deg;
        public float HeadPitch => -Mathf.Asin(Mathf.Clamp(HeadForward.y, -1f, 1f)) * Mathf.Rad2Deg;

        void Update()
        {
            if (m_Head == null)
                return;
            var now = Time.time;
            m_History.Add(new Sample { time = now, worldYaw = HeadWorldYaw });
            var cutoff = now - k_HistorySeconds;
            var remove = 0;
            while (remove < m_History.Count && m_History[remove].time < cutoff)
                remove++;
            if (remove > 0)
                m_History.RemoveRange(0, remove);
        }

        public void ClearHistory() => m_History.Clear();

        public float AngleTo(Vector3 worldTarget)
        {
            var toTarget = worldTarget - HeadPosition;
            return toTarget.sqrMagnitude < 1e-4f ? 0f : Vector3.Angle(HeadForward, toTarget);
        }

        public bool IsLookingAt(Vector3 worldTarget, float coneHalfAngleDegrees) => AngleTo(worldTarget) <= coneHalfAngleDegrees;

        /// <summary>Head yaw relative to a reference direction (positive = right), e.g. the car's forward.</summary>
        public float YawRelativeTo(Vector3 referenceForward) => Kinematics.SignedYaw(referenceForward, HeadForward);

        /// <summary>
        /// Largest yaw to each side of <paramref name="referenceForward"/> reached since <paramref name="sinceTime"/>
        /// (Time.time). Left is returned as a positive number.
        /// </summary>
        public void MaxYawSince(Vector3 referenceForward, float sinceTime, out float maxLeft, out float maxRight)
        {
            maxLeft = 0f;
            maxRight = 0f;
            referenceForward.y = 0f;
            if (referenceForward.sqrMagnitude < 1e-4f)
                return;
            var referenceYaw = Mathf.Atan2(referenceForward.x, referenceForward.z) * Mathf.Rad2Deg;
            foreach (var sample in m_History)
            {
                if (sample.time < sinceTime)
                    continue;
                var delta = Mathf.DeltaAngle(referenceYaw, sample.worldYaw);
                if (delta > maxRight) maxRight = delta;
                if (-delta > maxLeft) maxLeft = -delta;
            }
        }

        /// <summary>
        /// Did the learner look both ways (relative to the direction they are about to cross) within the window?
        /// </summary>
        public bool LookedBothWays(Vector3 crossingDirection, float yawThreshold, float windowSeconds, out bool lookedLeft, out bool lookedRight)
        {
            MaxYawSince(crossingDirection, Time.time - windowSeconds, out var left, out var right);
            lookedLeft = left >= yawThreshold;
            lookedRight = right >= yawThreshold;
            return lookedLeft && lookedRight;
        }
    }
}
