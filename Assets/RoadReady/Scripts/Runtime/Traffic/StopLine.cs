using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    public enum StopLineType
    {
        /// <summary>Signal-controlled stop line.</summary>
        Signal,
        /// <summary>Line before an unsignalised zebra crossing: yield to pedestrians on or waiting at it.</summary>
        ZebraYield,
        /// <summary>Minor-road give-way line at an unsignalised junction.</summary>
        GiveWay
    }

    /// <summary>Pedestrian agents expose their crossing intent so drivers (NPC and rules) can yield.</summary>
    public interface IPedestrianAgent : IRoadAgent
    {
        /// <summary>The crossing this pedestrian is waiting at the kerb to use, or null.</summary>
        RoadZone WaitingAtCrossing { get; }
    }

    /// <summary>
    /// A directional line across one approach. Its transform forward is the direction of travel; a vehicle
    /// "crosses" the line when its signed distance goes from negative to positive.
    /// </summary>
    public class StopLine : MonoBehaviour
    {
        static readonly List<StopLine> s_Lines = new List<StopLine>();

        [SerializeField] StopLineType m_Type = StopLineType.Signal;
        [Tooltip("Width of the approach the line covers (m).")]
        [SerializeField] float m_Width = 3.5f;

        [Header("Signal")]
        [SerializeField] TrafficSignalController m_Signal;
        [SerializeField] string m_SignalGroup = "ns";

        [Header("Zebra")]
        [SerializeField] RoadZone m_Crossing;

        [Header("Give way")]
        [Tooltip("Point where the minor approach meets priority traffic.")]
        [SerializeField] Transform m_ConflictPoint;
        [SerializeField] float m_ConflictRadius = 45f;

        public static IReadOnlyList<StopLine> All => s_Lines;

        public StopLineType Type => m_Type;
        public float Width => m_Width;
        public TrafficSignalController Signal => m_Signal;
        public string SignalGroup => m_SignalGroup;
        public RoadZone Crossing => m_Crossing;
        public Transform ConflictPoint => m_ConflictPoint;

        public void Configure(StopLineType type, float width, TrafficSignalController signal = null, string group = null, RoadZone crossing = null, Transform conflictPoint = null)
        {
            m_Type = type;
            m_Width = width;
            m_Signal = signal;
            if (group != null) m_SignalGroup = group;
            m_Crossing = crossing;
            m_ConflictPoint = conflictPoint;
        }

        void OnEnable() => s_Lines.Add(this);

        void OnDisable() => s_Lines.Remove(this);

        public Vector3 Forward
        {
            get
            {
                var f = transform.forward;
                f.y = 0f;
                return f.normalized;
            }
        }

        public float SignedDistance(Vector3 position) => Vector3.Dot(position - transform.position, Forward);

        public bool WithinWidth(Vector3 position, float margin = 0.5f)
        {
            var right = new Vector3(Forward.z, 0f, -Forward.x);
            return Mathf.Abs(Vector3.Dot(position - transform.position, right)) <= m_Width * 0.5f + margin;
        }

        /// <summary>True when heading the same way as the line (not an oncoming vehicle).</summary>
        public bool AppliesTo(Vector3 heading) => Vector3.Dot(heading, Forward) > 0.5f;

        public SignalState SignalStateNow => m_Signal != null ? m_Signal.GetState(m_SignalGroup) : SignalState.Off;

        /// <summary>Zebra: is any pedestrian on the crossing, or waiting at its kerb to cross?</summary>
        public bool HasPedestrianDemand(IRoadAgent exclude = null)
        {
            if (m_Crossing == null)
                return false;
            foreach (var agent in RoadAgentRegistry.All)
            {
                if (agent == exclude || agent.Kind != RoadUserKind.Pedestrian)
                    continue;
                if (m_Crossing.Contains(agent.Position, 0.3f))
                    return true;
                // At a signalised crossing, pedestrians waiting at the kerb (on red) are not demand.
                if (m_Crossing.Signal == null && agent is IPedestrianAgent ped && ped.WaitingAtCrossing == m_Crossing)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Give way: the smallest time-to-arrival at the conflict point among priority vehicles, or infinity.
        /// Vehicles on this line's own approach (queued behind) are ignored.
        /// </summary>
        public float PriorityTrafficTimeToArrival(IRoadAgent exclude, out IRoadAgent threat)
        {
            threat = null;
            if (m_ConflictPoint == null)
                return float.PositiveInfinity;
            var best = float.PositiveInfinity;
            var cp = m_ConflictPoint.position;
            foreach (var agent in RoadAgentRegistry.All)
            {
                if (agent == exclude || !agent.Kind.IsVehicle() || agent.IsYielding)
                    continue;
                var toConflict = cp - agent.Position;
                toConflict.y = 0f;
                var distance = toConflict.magnitude;
                if (distance > m_ConflictRadius || agent.Speed < 0.5f)
                    continue;
                if (Vector3.Dot(agent.Velocity, toConflict) <= 0f)
                    continue; // moving away
                if (SignedDistance(agent.Position) < 0f && WithinWidth(agent.Position) && AppliesTo(agent.Forward))
                    continue; // queued on our own approach
                var tta = distance / agent.Speed;
                if (tta < best)
                {
                    best = tta;
                    threat = agent;
                }
            }

            return best;
        }

        /// <summary>Decision used by NPC vehicles approaching the line.</summary>
        public bool NpcShouldStop(IRoadAgent vehicle, float distanceToLine, bool obeysSignals, bool yieldsToPedestrians, float giveWayGap)
        {
            switch (m_Type)
            {
                case StopLineType.Signal:
                    if (!obeysSignals)
                        return false;
                    var state = SignalStateNow;
                    if (state == SignalState.Red)
                        return true;
                    if (state == SignalState.Amber)
                        return distanceToLine > vehicle.Speed * vehicle.Speed / (2f * 3.5f); // can stop comfortably
                    return false;
                case StopLineType.ZebraYield:
                    return yieldsToPedestrians && HasPedestrianDemand();
                case StopLineType.GiveWay:
                    return PriorityTrafficTimeToArrival(vehicle, out _) < giveWayGap;
            }

            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = m_Type switch
            {
                StopLineType.Signal => Color.red,
                StopLineType.ZebraYield => Color.white,
                _ => Color.yellow,
            };
            var right = new Vector3(Forward.z, 0f, -Forward.x) * (m_Width * 0.5f);
            var p = transform.position + Vector3.up * 0.05f;
            Gizmos.DrawLine(p - right, p + right);
            Gizmos.DrawLine(p, p + Forward * 1.5f);
            if (m_ConflictPoint != null)
            {
                Gizmos.DrawWireSphere(m_ConflictPoint.position, 0.5f);
                Gizmos.DrawLine(p, m_ConflictPoint.position);
            }
        }
    }
}
