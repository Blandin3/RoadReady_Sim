using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>Box volume helper shared by rule zones.</summary>
    public abstract class BoxVolume : MonoBehaviour
    {
        [SerializeField] protected Vector3 m_Size = new Vector3(8f, 3f, 20f);

        public Vector3 Size { get => m_Size; set => m_Size = value; }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var half = m_Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && local.y >= -1f && local.y <= m_Size.y;
        }

        protected void DrawBox(Color color)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;
            Gizmos.DrawWireCube(new Vector3(0f, m_Size.y * 0.5f, 0f), m_Size);
        }
    }

    /// <summary>Overrides the scenario's default speed limit inside its volume (e.g. 30 km/h school zone).</summary>
    public class SpeedLimitZone : BoxVolume
    {
        static readonly List<SpeedLimitZone> s_Zones = new List<SpeedLimitZone>();

        [SerializeField] float m_LimitKmh = 30f;

        public float LimitKmh { get => m_LimitKmh; set => m_LimitKmh = value; }

        void OnEnable() => s_Zones.Add(this);

        void OnDisable() => s_Zones.Remove(this);

        public static float GetLimitAt(Vector3 position, float defaultLimit)
        {
            var limit = defaultLimit;
            foreach (var zone in s_Zones)
                if (zone.Contains(position))
                    limit = Mathf.Min(limit, zone.m_LimitKmh);
            return limit;
        }

        void OnDrawGizmos() => DrawBox(new Color(1f, 0.3f, 0.3f, 0.8f));
    }

    /// <summary>
    /// Where a driver must check a blind spot / mirror before a manoeuvre - typically where moto-taxis
    /// filter up the inside before a turn. Evaluated by the BlindSpotCheckRule using head yaw.
    /// </summary>
    public class BlindSpotCheckZone : BoxVolume
    {
        static readonly List<BlindSpotCheckZone> s_Zones = new List<BlindSpotCheckZone>();

        [SerializeField] TurnSide m_Side = TurnSide.Right;
        [SerializeField] string m_Title = "Blind-spot check before turning";

        public static IReadOnlyList<BlindSpotCheckZone> All => s_Zones;
        public TurnSide Side { get => m_Side; set => m_Side = value; }
        public string Title { get => m_Title; set => m_Title = value; }
        public string ZoneId => name;

        void OnEnable() => s_Zones.Add(this);

        void OnDisable() => s_Zones.Remove(this);

        void OnDrawGizmos() => DrawBox(new Color(1f, 0.9f, 0.1f, 0.8f));
    }

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
