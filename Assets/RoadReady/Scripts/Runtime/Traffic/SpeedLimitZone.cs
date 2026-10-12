using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
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

}
