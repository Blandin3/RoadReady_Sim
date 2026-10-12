using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>Scenario end point for the player (FR: attempt completes when reached).</summary>
    public class GoalZone : BoxVolume
    {
        [SerializeField] PerspectiveMask m_Perspectives = PerspectiveMask.Both;
        [Tooltip("Pedestrian goals only count after the learner has actually crossed.")]
        [SerializeField] bool m_RequiresCrossing = true;

        public PerspectiveMask Perspectives { get => m_Perspectives; set => m_Perspectives = value; }
        public bool RequiresCrossing { get => m_RequiresCrossing; set => m_RequiresCrossing = value; }

        void OnDrawGizmos() => DrawBox(new Color(0.1f, 1f, 0.4f, 0.9f));
    }
}
