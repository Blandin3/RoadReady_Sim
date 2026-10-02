using System.Collections.Generic;
using UnityEngine;

namespace RoadReady.Traffic
{
    public enum RoadZoneType
    {
        Sidewalk,
        Carriageway,
        ZebraCrossing,
        SignalizedCrossing,
        Junction,
        Median
    }

    /// <summary>
    /// Box-shaped semantic area of the road network. Rules query these geometrically (no physics triggers),
    /// so they keep working when the greybox is replaced by final art.
    /// </summary>
    public class RoadZone : MonoBehaviour
    {
        static readonly List<RoadZone> s_Zones = new List<RoadZone>();

        [SerializeField] RoadZoneType m_Type = RoadZoneType.Carriageway;
        [Tooltip("Local-space size of the zone (x = width, y = height, z = length).")]
        [SerializeField] Vector3 m_Size = new Vector3(7f, 3f, 20f);

        [Header("Crossings only")]
        [Tooltip("Kerb end the pedestrian starts from / arrives at.")]
        [SerializeField] Transform m_KerbA;
        [SerializeField] Transform m_KerbB;
        [Tooltip("Signal controller for the pedestrian signal (signalised crossings).")]
        [SerializeField] TrafficSignalController m_Signal;
        [SerializeField] string m_PedestrianSignalGroup = "ped";

        public static IReadOnlyList<RoadZone> All => s_Zones;

        public RoadZoneType Type => m_Type;
        public Vector3 Size { get => m_Size; set => m_Size = value; }
        public Transform KerbA { get => m_KerbA; set => m_KerbA = value; }
        public Transform KerbB { get => m_KerbB; set => m_KerbB = value; }
        public TrafficSignalController Signal { get => m_Signal; set => m_Signal = value; }
        public string PedestrianSignalGroup { get => m_PedestrianSignalGroup; set => m_PedestrianSignalGroup = value; }

        public bool IsCrossing => m_Type == RoadZoneType.ZebraCrossing || m_Type == RoadZoneType.SignalizedCrossing;
        public bool IsDrivable => m_Type == RoadZoneType.Carriageway || m_Type == RoadZoneType.Junction || IsCrossing;
        /// <summary>Areas where a pedestrian is exposed to traffic.</summary>
        public bool IsRoadway => IsDrivable;

        /// <summary>Crossing length from kerb to kerb (falls back to zone width).</summary>
        public float CrossingLength => m_KerbA != null && m_KerbB != null ? Vector3.Distance(m_KerbA.position, m_KerbB.position) : m_Size.x;

        public void Configure(RoadZoneType type, Vector3 size)
        {
            m_Type = type;
            m_Size = size;
        }

        void OnEnable() => s_Zones.Add(this);

        void OnDisable() => s_Zones.Remove(this);

        public bool Contains(Vector3 worldPosition, float margin = 0f)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var half = m_Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x + margin && Mathf.Abs(local.z) <= half.z + margin && local.y >= -1f && local.y <= m_Size.y;
        }

        public SignalState PedestrianSignalState =>
            m_Signal != null ? m_Signal.GetState(m_PedestrianSignalGroup) : SignalState.Off;

        /// <summary>The kerb end farther from <paramref name="position"/> - where a crossing from here ends.</summary>
        public Transform FarKerb(Vector3 position)
        {
            if (m_KerbA == null || m_KerbB == null)
                return null;
            return (m_KerbA.position - position).sqrMagnitude > (m_KerbB.position - position).sqrMagnitude ? m_KerbA : m_KerbB;
        }

        public Transform NearKerb(Vector3 position)
        {
            var far = FarKerb(position);
            return far == m_KerbA ? m_KerbB : m_KerbA;
        }

        // ------------------------------------------------------------ static queries

        public static RoadZone FindFirst(Vector3 position, System.Predicate<RoadZone> filter = null)
        {
            // Crossings and junctions take precedence over the carriageway they overlap.
            RoadZone fallback = null;
            foreach (var zone in s_Zones)
            {
                if (!zone.Contains(position) || (filter != null && !filter(zone)))
                    continue;
                if (zone.IsCrossing || zone.m_Type == RoadZoneType.Junction)
                    return zone;
                fallback ??= zone;
            }

            return fallback;
        }

        public static bool IsOnRoadway(Vector3 position) => FindFirst(position, z => z.IsRoadway) != null;

        public static bool IsOnDrivable(Vector3 position) => FindFirst(position, z => z.IsDrivable) != null;

        public static RoadZone FindCrossingAt(Vector3 position, float margin = 0f)
        {
            foreach (var zone in s_Zones)
                if (zone.IsCrossing && zone.Contains(position, margin))
                    return zone;
            return null;
        }

        /// <summary>Nearest crossing whose kerb end is within <paramref name="radius"/> of the position.</summary>
        public static RoadZone FindCrossingNear(Vector3 position, float radius)
        {
            RoadZone best = null;
            var bestDistance = radius;
            foreach (var zone in s_Zones)
            {
                if (!zone.IsCrossing || zone.m_KerbA == null || zone.m_KerbB == null)
                    continue;
                var d = Mathf.Min(Vector3.Distance(position, zone.m_KerbA.position), Vector3.Distance(position, zone.m_KerbB.position));
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = zone;
                }
            }

            return best;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = m_Type switch
            {
                RoadZoneType.Sidewalk => new Color(0.6f, 0.6f, 0.6f, 0.15f),
                RoadZoneType.Carriageway => new Color(0.2f, 0.2f, 0.9f, 0.12f),
                RoadZoneType.ZebraCrossing => new Color(1f, 1f, 1f, 0.3f),
                RoadZoneType.SignalizedCrossing => new Color(0.2f, 1f, 0.3f, 0.3f),
                RoadZoneType.Junction => new Color(1f, 0.6f, 0f, 0.2f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.15f),
            };
            var center = new Vector3(0f, m_Size.y * 0.5f - 1f + 1f, 0f);
            Gizmos.DrawCube(center, m_Size);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.8f);
            Gizmos.DrawWireCube(center, m_Size);
        }
    }
}
