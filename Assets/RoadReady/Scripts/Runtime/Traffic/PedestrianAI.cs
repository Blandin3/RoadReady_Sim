using System;
using RoadReady.Core;
using UnityEngine;
using UnityEngine.Events;

namespace RoadReady.Traffic
{
    /// <summary>
    /// NPC pedestrian walking a <see cref="WaypointPath"/>. Automatically detects kerb edges (where the path
    /// enters the roadway) and waits for a gap or the walk signal - unless it is a jaywalker or a hazard
    /// orders it to step out.
    /// </summary>
    public class PedestrianAI : MonoBehaviour, IPedestrianAgent, IHazardActor
    {
        enum State
        {
            Walking,
            WaitingAtKerb,
            Crossing,
            Stopped
        }

        [SerializeField] WaypointPath m_Path;
        [SerializeField] float m_StartDistance;
        [SerializeField] float m_WalkSpeed = 1.3f;
        [Tooltip("Extra seconds of gap this pedestrian wants beyond the crossing time.")]
        [SerializeField] float m_GapMargin = 1.5f;
        [SerializeField] bool m_ObeysSignals = true;
        [Tooltip("Steps out without checking traffic.")]
        [SerializeField] bool m_Reckless;
        [SerializeField] bool m_StartDormant;
        [SerializeField] bool m_HiddenWhileDormant;
        [SerializeField] bool m_DisableAtPathEnd = true;
        [SerializeField] bool m_Scripted;

        [Header("Visuals (optional)")]
        [SerializeField] Transform m_FocusPoint;
        [SerializeField] Animator m_Animator;
        [SerializeField] UnityEvent m_OnHit;

        Renderer[] m_Renderers;
        State m_State;
        float m_Distance;
        float m_Speed;
        float m_SpeedMultiplier = 1f;
        bool m_Dormant;
        bool m_Registered;
        bool m_ForceCross;
        RoadZone m_WaitingAt;
        Vector3 m_Velocity;

        public event Action<PedestrianAI> ReachedPathEnd;

        public RoadUserKind Kind => RoadUserKind.Pedestrian;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => m_Velocity;
        public Vector3 Forward => transform.forward;
        public float Speed => m_Speed;
        public float Length => 0.5f;
        public float Width => 0.5f;
        public bool IsPlayer => false;
        public bool IsYielding => m_State == State.WaitingAtKerb;
        public Transform Transform => transform;
        public RoadZone WaitingAtCrossing => m_State == State.WaitingAtKerb ? m_WaitingAt : null;
        public Transform FocusPoint => m_FocusPoint != null ? m_FocusPoint : transform;
        public IRoadAgent Agent => this;
        public bool IsCrossing => m_State == State.Crossing;
        public bool IsScripted => m_Scripted;

        public void Configure(WaypointPath path, float startDistance, float walkSpeed, bool reckless, bool obeysSignals)
        {
            m_Path = path;
            m_StartDistance = startDistance;
            m_WalkSpeed = walkSpeed;
            m_Reckless = reckless;
            m_ObeysSignals = obeysSignals;
            ResetToStart();
        }

        public void SetDormant(bool dormant, bool hidden)
        {
            m_StartDormant = dormant;
            m_HiddenWhileDormant = hidden;
        }

        public void SetScripted(bool scripted) => m_Scripted = scripted;

        void Awake() => m_Renderers = GetComponentsInChildren<Renderer>(true);

        void OnEnable() => ResetToStart();

        void OnDisable() => SetRegistered(false);

        public void ResetToStart()
        {
            m_Distance = m_StartDistance;
            m_Down = false;
            m_NoHitUntil = Time.time + k_SpawnGraceSeconds;
            if (m_Animator != null)
                m_Animator.enabled = true;
            m_State = State.Walking;
            m_ForceCross = false;
            m_SpeedMultiplier = 1f;
            m_Dormant = m_StartDormant;
            m_WaitingAt = null;
            var hidden = m_Dormant && m_HiddenWhileDormant;
            if (m_Renderers != null)
                foreach (var r in m_Renderers) if (r != null) r.enabled = !hidden;
            SetRegistered(!hidden);
            Place();
        }

        void SetRegistered(bool registered)
        {
            if (registered == m_Registered || !Application.isPlaying)
                return;
            m_Registered = registered;
            if (registered) RoadAgentRegistry.Register(this);
            else RoadAgentRegistry.Unregister(this);
        }

        void Update()
        {
            if (m_Path == null)
                return;
            var dt = Time.deltaTime;

            if (m_Down)
            {
                UpdateFall(dt);
                return;
            }

            if (!m_Dormant)
                CheckHitByTraffic();

            if (m_Dormant || m_State == State.Stopped)
            {
                m_Speed = 0f;
                m_Velocity = Vector3.zero;
                if (m_Dormant && m_Path != null)
                    FaceAlongPath();
                UpdateAnimator();
                return;
            }

            var here = m_Path.GetPoint(m_Distance);
            var ahead = m_Path.GetPoint(m_Distance + 0.6f);
            var onRoad = RoadZone.IsOnRoadway(here);
            var roadAhead = RoadZone.IsOnRoadway(ahead);

            switch (m_State)
            {
                case State.Walking:
                    if (!onRoad && roadAhead)
                    {
                        m_WaitingAt = RoadZone.FindCrossingAt(ahead, 0.5f);
                        m_State = State.WaitingAtKerb;
                    }

                    break;
                case State.WaitingAtKerb:
                    if (ShouldStartCrossing(here))
                    {
                        m_State = State.Crossing;
                        m_WaitingAt = null;
                    }

                    break;
                case State.Crossing:
                    if (!onRoad && !roadAhead)
                    {
                        m_State = State.Walking;
                        m_SpeedMultiplier = 1f;
                    }

                    break;
            }

            var target = m_State == State.WaitingAtKerb ? 0f : m_WalkSpeed * m_SpeedMultiplier;
            m_Speed = Mathf.MoveTowards(m_Speed, target, 3f * dt);
            m_Distance += m_Speed * dt;

            if (!m_Path.Loop && m_Distance >= m_Path.Length - 0.05f)
            {
                ReachedPathEnd?.Invoke(this);
                if (m_DisableAtPathEnd)
                {
                    gameObject.SetActive(false);
                    return;
                }

                m_Distance = m_Path.Length - 0.05f;
                m_State = State.Stopped;
            }

            var previous = transform.position;
            Place();
            m_Velocity = dt > 0f ? (transform.position - previous) / dt : Vector3.zero;
            UpdateAnimator();
        }

        bool ShouldStartCrossing(Vector3 here)
        {
            if (m_ForceCross || m_Reckless)
                return true;

            if (m_WaitingAt != null && m_WaitingAt.Signal != null)
            {
                var state = m_WaitingAt.PedestrianSignalState;
                if (m_ObeysSignals)
                    return state == SignalState.Green;
            }

            var crossingLength = m_WaitingAt != null ? m_WaitingAt.CrossingLength : 7f;
            var required = crossingLength / Mathf.Max(0.5f, m_WalkSpeed) + m_GapMargin;
            float tta;
            if (m_WaitingAt != null)
            {
                tta = CrossingGap.MinTimeToArrival(m_WaitingAt, this, out _);
            }
            else
            {
                var across = m_Path.GetTangent(m_Distance + 1f);
                tta = CrossingGap.MinTimeToArrivalAt(here, across, crossingLength, this, out _);
            }

            return tta >= required;
        }

        void Place()
        {
            if (m_Path == null)
                return;
            transform.position = m_Path.GetPoint(m_Distance);
            FaceAlongPath();
        }

        void FaceAlongPath()
        {
            var tangent = m_Path.GetTangent(m_Distance);
            tangent.y = 0f;
            if (tangent.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }

        void UpdateAnimator()
        {
            if (m_Animator != null)
                m_Animator.SetFloat("Speed", m_Speed);
        }

        // Knocked down by a vehicle: tips over in the direction of the impact and slides a little along the road.
        bool m_Down;
        float m_FallTime;
        Vector3 m_FallPush;
        float m_FallSlide;
        Quaternion m_FallStartRotation;
        const float k_FallSeconds = 0.55f;
        const float k_FallDegrees = 85f;
        const float k_MinHitSpeed = 1.5f; // m/s: a vehicle creeping past does not knock anyone over

        public bool IsDown => m_Down;

        /// <summary>Called when a vehicle hits this pedestrian: it stops and falls where the vehicle was heading.</summary>
        public void OnHitByVehicle(Vector3 vehicleVelocity)
        {
            if (m_Down)
                return;
            vehicleVelocity.y = 0f;
            m_FallPush = vehicleVelocity.sqrMagnitude > 0.01f ? vehicleVelocity.normalized : transform.forward;
            m_FallSlide = Mathf.Clamp(vehicleVelocity.magnitude * 0.12f, 0.3f, 1.8f);
            m_FallStartRotation = transform.rotation;
            m_FallSlideDone = 0f;
            m_FallTime = 0f;
            m_Down = true;
            m_State = State.Stopped;
            m_Speed = 0f;
            m_Velocity = Vector3.zero;
            if (m_Animator != null)
                m_Animator.enabled = false; // freeze the walk cycle so the body falls as one piece
            m_OnHit?.Invoke();
        }

        void UpdateFall(float dt)
        {
            m_FallTime = Mathf.Min(k_FallSeconds, m_FallTime + dt);
            var t = m_FallTime / k_FallSeconds;
            var eased = 1f - (1f - t) * (1f - t);
            var axis = Vector3.Cross(Vector3.up, m_FallPush);
            transform.rotation = Quaternion.AngleAxis(k_FallDegrees * eased, axis) * m_FallStartRotation;
            transform.position += m_FallPush * (m_FallSlide * (eased - m_FallSlideDone));
            m_FallSlideDone = eased;
        }

        float m_FallSlideDone;
        float m_NoHitUntil;
        const float k_SpawnGraceSeconds = 3f;

        /// <summary>Any moving vehicle that overlaps a walking pedestrian knocks them down (not only the player's car).</summary>
        void CheckHitByTraffic()
        {
            // Right after (re)spawning, a pedestrian may overlap a passing vehicle; never start a scenario already knocked down.
            if (Time.time < m_NoHitUntil)
                return;
            foreach (var other in RoadAgentRegistry.All)
            {
                if (other == (IRoadAgent)this || other.Kind == RoadUserKind.Pedestrian || other.Speed < k_MinHitSpeed)
                    continue;
                if (RoadAgentRegistry.Overlaps(this, other, 0.05f))
                {
                    OnHitByVehicle(other.Velocity);
                    return;
                }
            }
        }

        public void PerformHazard(HazardActorMode mode, float value)
        {
            switch (mode)
            {
                case HazardActorMode.Activate:
                    m_Dormant = false;
                    if (m_Renderers != null)
                        foreach (var r in m_Renderers) if (r != null) r.enabled = true;
                    SetRegistered(true);
                    break;
                case HazardActorMode.CrossNow:
                    m_Dormant = false;
                    m_ForceCross = true;
                    m_SpeedMultiplier = value > 0f ? value : 1.3f;
                    SetRegistered(true);
                    break;
                case HazardActorMode.IgnoreSignals:
                    m_ObeysSignals = false;
                    break;
                case HazardActorMode.SetSpeed:
                    m_WalkSpeed = value;
                    break;
                case HazardActorMode.Reset:
                    ResetToStart();
                    break;
            }
        }
    }
}
