using System;
using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    public enum HazardActorMode
    {
        /// <summary>Start moving / appear (actor was dormant).</summary>
        Activate,
        IgnoreSignals,
        DoNotYield,
        /// <summary>Brake hard to a stop for <c>value</c> seconds (moto-taxi pickup).</summary>
        SuddenStop,
        /// <summary>Set cruise speed to <c>value</c> km/h.</summary>
        SetSpeed,
        StartWeaving,
        /// <summary>Pedestrian: start crossing now, regardless of traffic.</summary>
        CrossNow,
        Reset
    }

    /// <summary>Scene actors a HazardTrigger can command.</summary>
    public interface IHazardActor
    {
        void PerformHazard(HazardActorMode mode, float value);
        Transform FocusPoint { get; }
        IRoadAgent Agent { get; }
    }

    /// <summary>
    /// Deterministic, physics-free NPC vehicle: follows a <see cref="WaypointPath"/> with the Intelligent
    /// Driver Model for longitudinal control, obeys (or not) signals, zebra crossings and give-way lines.
    /// </summary>
    public class TrafficVehicleAI : MonoBehaviour, IRoadAgent, IHazardActor
    {
        [Header("Identity")]
        [SerializeField] RoadUserKind m_Kind = RoadUserKind.Car;
        [SerializeField] float m_Length = 4.4f;
        [SerializeField] float m_Width = 1.8f;

        [Header("Route")]
        [SerializeField] WaypointPath m_Path;
        [SerializeField] float m_StartDistance;
        [SerializeField] bool m_DisableAtPathEnd = true;

        [Header("Driving (IDM)")]
        [SerializeField] float m_CruiseSpeedKmh = 35f;
        [SerializeField] float m_MaxAcceleration = 2.5f;
        [SerializeField] float m_ComfortDeceleration = 3f;
        [SerializeField] float m_MaxDeceleration = 8f;
        [SerializeField] float m_MinGap = 2f;
        [SerializeField] float m_TimeHeadway = 1.4f;
        [SerializeField] float m_LookAhead = 45f;
        [SerializeField] float m_GiveWayGapSeconds = 4f;

        [Header("Behaviour")]
        [SerializeField] bool m_ObeysSignals = true;
        [SerializeField] bool m_YieldsToPedestrians = true;
        [Tooltip("Dormant actors wait (hidden or parked) until a HazardTrigger activates them.")]
        [SerializeField] bool m_StartDormant;
        [SerializeField] bool m_HiddenWhileDormant = true;
        [Tooltip("Moto-taxi style lateral weaving within the lane.")]
        [SerializeField] bool m_Weaving;
        [SerializeField] float m_WeaveAmplitude = 0.9f;
        [SerializeField] float m_WeaveFrequency = 0.35f;
        [Tooltip("Exempt from spawner yield/signal rolls - used for scripted counterpart / hazard vehicles.")]
        [SerializeField] bool m_Scripted;

        [Header("Visuals (optional)")]
        [SerializeField] Transform m_FocusPoint;
        [SerializeField] Transform[] m_Wheels = Array.Empty<Transform>();
        [SerializeField] float m_WheelRadius = 0.33f;
        [SerializeField] Renderer[] m_BrakeLights = Array.Empty<Renderer>();
        [SerializeField] AudioSource m_EngineAudio;
        [SerializeField] AudioSource m_HornAudio;
        [SerializeField] Animator m_Animator;

        readonly List<(float distance, StopLine line)> m_StopLines = new List<(float, StopLine)>();
        readonly HashSet<StopLine> m_Committed = new HashSet<StopLine>();
        Renderer[] m_Renderers;
        MaterialPropertyBlock m_Block;
        bool m_StopLinesCached;
        bool m_Dormant;
        bool m_Registered;
        float m_Distance;
        float m_Speed;
        float m_SuddenStopUntil = -1f;
        float m_WeavePhase;
        float m_LastAcceleration;
        Vector3 m_Velocity;

        public event Action<TrafficVehicleAI> ReachedPathEnd;

        public RoadUserKind Kind => m_Kind;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => m_Velocity;
        public Vector3 Forward => transform.forward;
        public float Speed => m_Speed;
        public float Length => m_Length;
        public float Width => m_Width;
        public bool IsPlayer => false;
        public bool IsYielding { get; private set; }
        public Transform Transform => transform;
        public Transform FocusPoint => m_FocusPoint != null ? m_FocusPoint : transform;
        public IRoadAgent Agent => this;
        public bool IsScripted => m_Scripted;
        public bool IsDormant => m_Dormant;
        public WaypointPath Path => m_Path;
        public float DistanceAlongPath => m_Distance;

        public void Configure(WaypointPath path, float startDistance, float cruiseKmh, bool obeysSignals, bool yieldsToPedestrians)
        {
            m_Path = path;
            m_StartDistance = startDistance;
            m_CruiseSpeedKmh = cruiseKmh;
            m_ObeysSignals = obeysSignals;
            m_YieldsToPedestrians = yieldsToPedestrians;
            m_StopLinesCached = false;
            m_Committed.Clear();
            ResetToStart();
        }

        public void SetShape(RoadUserKind kind, float length, float width)
        {
            m_Kind = kind;
            m_Length = length;
            m_Width = width;
            if (kind == RoadUserKind.MotoTaxi || kind == RoadUserKind.Bicycle)
            {
                m_TimeHeadway = 0.8f;
                m_MinGap = 1.2f;
                m_MaxAcceleration = kind == RoadUserKind.MotoTaxi ? 3.5f : 1.2f;
            }
            else if (kind == RoadUserKind.Bus || kind == RoadUserKind.Truck)
            {
                m_TimeHeadway = 1.8f;
                m_MaxAcceleration = 1.4f;
            }
        }

        public void SetWeaving(bool weaving) => m_Weaving = weaving;

        public void SetDormant(bool dormant, bool hidden)
        {
            m_StartDormant = dormant;
            m_HiddenWhileDormant = hidden;
        }

        public void SetScripted(bool scripted) => m_Scripted = scripted;

        void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_WeavePhase = RoadReadyRandom.Range(0f, 10f);
        }

        void OnEnable()
        {
            ResetToStart();
        }

        void OnDisable()
        {
            SetRegistered(false);
        }

        public void ResetToStart()
        {
            m_Distance = m_StartDistance;
            m_Speed = m_StartDormant ? 0f : m_CruiseSpeedKmh * Units.KmhToMs;
            m_SuddenStopUntil = -1f;
            m_Committed.Clear();
            m_Dormant = m_StartDormant;
            SetVisible(!(m_Dormant && m_HiddenWhileDormant));
            SetRegistered(!(m_Dormant && m_HiddenWhileDormant));
            Place(0f);
        }

        void SetRegistered(bool registered)
        {
            if (registered == m_Registered || !Application.isPlaying)
                return;
            m_Registered = registered;
            if (registered) RoadAgentRegistry.Register(this);
            else RoadAgentRegistry.Unregister(this);
        }

        void SetVisible(bool visible)
        {
            if (m_Renderers == null)
                return;
            foreach (var r in m_Renderers)
                if (r != null) r.enabled = visible;
        }

        void CacheStopLines()
        {
            m_StopLinesCached = true;
            m_StopLines.Clear();
            if (m_Path == null)
                return;
            foreach (var line in StopLine.All)
            {
                var d = m_Path.GetClosestDistance(line.transform.position, out var lateral);
                if (lateral > line.Width * 0.5f + 1f)
                    continue;
                if (!line.AppliesTo(m_Path.GetTangent(d)))
                    continue;
                m_StopLines.Add((d, line));
            }

            m_StopLines.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        void Update()
        {
            if (m_Path == null)
                return;
            if (!m_StopLinesCached)
                CacheStopLines();

            var dt = Time.deltaTime;
            if (m_Dormant)
            {
                m_Velocity = Vector3.zero;
                return;
            }

            var accel = ComputeAcceleration(out var yielding);
            IsYielding = yielding;
            m_LastAcceleration = accel;
            m_Speed = Mathf.Max(0f, m_Speed + accel * dt);
            m_Distance += m_Speed * dt;

            if (!m_Path.Loop && m_Distance >= m_Path.Length - 0.1f)
            {
                ReachedPathEnd?.Invoke(this);
                if (m_DisableAtPathEnd)
                {
                    gameObject.SetActive(false);
                    return;
                }

                m_Distance = m_Path.Length - 0.1f;
                m_Speed = 0f;
            }

            var previous = transform.position;
            Place(dt);
            m_Velocity = dt > 0f ? (transform.position - previous) / dt : Vector3.zero;
            UpdateVisuals(dt);
        }

        void Place(float dt)
        {
            if (m_Path == null)
                return;
            var ahead = m_Path.GetPoint(m_Distance + Mathf.Max(2f, m_Length * 0.6f));
            var behind = m_Path.GetPoint(m_Distance - m_Length * 0.3f);
            var pos = m_Path.GetPoint(m_Distance);
            var dir = ahead - behind;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = m_Path.GetTangent(m_Distance);

            if (m_Weaving && m_Speed > 1f)
            {
                m_WeavePhase += dt * m_WeaveFrequency * Mathf.PI * 2f;
                var right = Vector3.Cross(Vector3.up, dir.normalized);
                pos += right * (Mathf.Sin(m_WeavePhase) * m_WeaveAmplitude);
                dir += right * (Mathf.Cos(m_WeavePhase) * m_WeaveAmplitude * 0.6f);
            }

            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized, Vector3.up));
        }

        float ComputeAcceleration(out bool yielding)
        {
            yielding = false;
            var v = m_Speed;
            var v0 = m_CruiseSpeedKmh * Units.KmhToMs;

            if (Time.time < m_SuddenStopUntil)
                return v > 0.05f ? -m_MaxDeceleration * 0.8f : 0f;

            // Free-road term.
            var accel = m_MaxAcceleration * (1f - Mathf.Pow(v / Mathf.Max(0.1f, v0), 4f));

            // Leader (vehicles, and pedestrians unless this driver does not yield).
            var leader = RoadAgentRegistry.FindLeader(this, transform.forward, m_LookAhead, m_Path.LaneHalfWidth * 0.55f, out var gap, includePedestrians: m_YieldsToPedestrians);
            if (leader != null)
            {
                var interaction = InteractionTerm(v, v - Vector3.Dot(leader.Velocity, transform.forward), gap);
                accel = Mathf.Min(accel, interaction);
                if (leader.Kind == RoadUserKind.Pedestrian)
                    yielding = true;
            }

            // Emergency braking for anything about to be hit, even by "reckless" drivers.
            foreach (var other in RoadAgentRegistry.All)
            {
                if (other == (IRoadAgent)this)
                    continue;
                if (other.Kind.IsVehicle() && Vector3.Dot(other.Forward, transform.forward) < -0.7f)
                    continue; // oncoming traffic passes in its own lane
                var ttc = RoadAgentRegistry.TimeToCollision(this, other);
                if (ttc < 0.9f && Vector3.Dot(other.Position - Position, transform.forward) > 0f)
                {
                    accel = Mathf.Min(accel, -m_MaxDeceleration);
                    break;
                }
            }

            // Virtual obstacles at stop lines.
            foreach (var (lineDistance, line) in m_StopLines)
            {
                var toLine = lineDistance - m_Distance - m_Length * 0.5f;
                if (toLine < -0.5f || m_Committed.Contains(line))
                    continue;
                if (toLine > m_LookAhead)
                    break;

                var stop = line.NpcShouldStop(this, toLine, m_ObeysSignals, m_YieldsToPedestrians, m_GiveWayGapSeconds);
                if (!stop)
                {
                    // Once the vehicle is close and has decided to go, never flip-flop on amber / gaps.
                    if (toLine < Mathf.Max(3f, v * 1.2f))
                        m_Committed.Add(line);
                    continue;
                }

                accel = Mathf.Min(accel, InteractionTerm(v, v, Mathf.Max(0.1f, toLine)));
                if (line.Type != StopLineType.Signal)
                    yielding = true;
                break;
            }

            return Mathf.Clamp(accel, -m_MaxDeceleration, m_MaxAcceleration);
        }

        float InteractionTerm(float v, float approachRate, float gap)
        {
            var a = m_MaxAcceleration;
            var b = m_ComfortDeceleration;
            var sStar = m_MinGap + Mathf.Max(0f, v * m_TimeHeadway + v * approachRate / (2f * Mathf.Sqrt(a * b)));
            var ratio = sStar / Mathf.Max(0.1f, gap);
            return a * (1f - Mathf.Pow(v / Mathf.Max(0.1f, m_CruiseSpeedKmh * Units.KmhToMs), 4f) - ratio * ratio);
        }

        void UpdateVisuals(float dt)
        {
            if (m_Wheels.Length > 0 && m_WheelRadius > 0f)
            {
                var degrees = m_Speed * dt / m_WheelRadius * Mathf.Rad2Deg;
                foreach (var wheel in m_Wheels)
                    if (wheel != null) wheel.Rotate(degrees, 0f, 0f, Space.Self);
            }

            if (m_BrakeLights.Length > 0)
            {
                m_Block ??= new MaterialPropertyBlock();
                var on = m_LastAcceleration < -1f || m_Speed < 0.2f;
                foreach (var light in m_BrakeLights)
                {
                    if (light == null) continue;
                    light.GetPropertyBlock(m_Block);
                    m_Block.SetColor("_EmissionColor", on ? Color.red * 2f : Color.black);
                    light.SetPropertyBlock(m_Block);
                }
            }

            if (m_EngineAudio != null)
                m_EngineAudio.pitch = 0.8f + m_Speed / 15f;
            if (m_Animator != null)
                m_Animator.SetFloat("Speed", m_Speed);
        }

        public void Honk()
        {
            if (m_HornAudio != null && !m_HornAudio.isPlaying)
                m_HornAudio.Play();
        }

        public void PerformHazard(HazardActorMode mode, float value)
        {
            switch (mode)
            {
                case HazardActorMode.Activate:
                    m_Dormant = false;
                    if (m_Speed < 0.1f)
                        m_Speed = value > 0f ? value * Units.KmhToMs : 0f;
                    SetVisible(true);
                    SetRegistered(true);
                    break;
                case HazardActorMode.IgnoreSignals:
                    m_ObeysSignals = false;
                    m_Committed.Clear();
                    break;
                case HazardActorMode.DoNotYield:
                    m_YieldsToPedestrians = false;
                    break;
                case HazardActorMode.SuddenStop:
                    m_SuddenStopUntil = Time.time + Mathf.Max(0.5f, value);
                    break;
                case HazardActorMode.SetSpeed:
                    m_CruiseSpeedKmh = value;
                    break;
                case HazardActorMode.StartWeaving:
                    m_Weaving = true;
                    break;
                case HazardActorMode.Reset:
                    ResetToStart();
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.75f, new Vector3(m_Width, 1.5f, m_Length));
        }
    }
}
