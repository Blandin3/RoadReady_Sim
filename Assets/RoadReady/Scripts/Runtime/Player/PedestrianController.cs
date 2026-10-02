using System;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;
using UnityEngine.XR;

namespace RoadReady.Player
{
    public struct CrossingEvent
    {
        public float time;
        public Vector3 position;
        /// <summary>Designated crossing used, or null when crossing elsewhere.</summary>
        public RoadZone crossing;
        public Vector3 direction;
        public float crossingLength;
        public bool viaCrossNowButton;
    }

    /// <summary>
    /// The learner on foot (FR4). Tracks kerb / roadway state from the head position, exposes the "Cross now"
    /// decision action, and optionally walks the learner across automatically (comfort option).
    /// </summary>
    public class PedestrianController : MonoBehaviour, IPedestrianAgent
    {
        const float k_KerbWaitRadius = 2.2f;
        const float k_CrossNowLinkSeconds = 3f;

        [SerializeField] float m_DesktopLookSensitivity = 0.15f;

        PlayerRig m_Rig;
        ComfortSettings m_Comfort;
        Vector3 m_LastPosition;
        Vector3 m_Velocity;
        bool m_Registered;
        bool m_OnRoadway;
        bool m_AutoWalking;
        Vector3 m_AutoTarget;
        Vector3 m_CrossStartPosition;
        float m_LastCrossNowTime = -100f;
        CrossingEvent m_CurrentCrossing;

        public event Action<CrossingEvent> CrossingStarted;
        public event Action<CrossingEvent> CrossingCompleted;
        public event Action<CrossingEvent> CrossingAbandoned;
        public event Action<float> CrossNowPressed;
        public event Action<float> StopPressed;

        public RoadUserKind Kind => RoadUserKind.Pedestrian;
        public Vector3 Position { get; private set; }
        public Vector3 Velocity => m_Velocity;
        public Vector3 Forward
        {
            get
            {
                var f = m_Rig != null ? m_Rig.HeadTransform.forward : transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            }
        }

        public float Speed => m_Velocity.magnitude;
        public float Length => 0.5f;
        public float Width => 0.5f;
        public bool IsPlayer => true;
        public bool IsYielding => false;
        public Transform Transform => transform;
        public RoadZone WaitingAtCrossing { get; private set; }

        public bool InputEnabled { get; set; }
        public bool IsOnRoadway => m_OnRoadway;
        public bool IsCrossing { get; private set; }
        public bool HasCompletedCrossing { get; private set; }
        public bool IsAutoWalking => m_AutoWalking;
        public float RoadwayEnterTime { get; private set; } = -1f;
        public CrossingEvent CurrentCrossing => m_CurrentCrossing;
        /// <summary>Contextual prompt for the HUD.</summary>
        public string Prompt { get; private set; }

        public void Initialize(PlayerRig rig, ComfortSettings comfort)
        {
            m_Rig = rig;
            m_Comfort = comfort;
        }

        public void ApplyComfort(ComfortSettings comfort) => m_Comfort = comfort;

        public void ResetState()
        {
            m_AutoWalking = false;
            IsCrossing = false;
            HasCompletedCrossing = false;
            RoadwayEnterTime = -1f;
            m_LastCrossNowTime = -100f;
            Position = SampleFeet();
            m_LastPosition = Position;
            m_OnRoadway = RoadZone.IsOnRoadway(Position);
        }

        void OnEnable()
        {
            if (!m_Registered)
            {
                RoadAgentRegistry.Register(this);
                m_Registered = true;
            }

            ResetState();
        }

        void OnDisable()
        {
            if (m_Registered)
            {
                RoadAgentRegistry.Unregister(this);
                m_Registered = false;
            }

            m_AutoWalking = false;
        }

        Vector3 SampleFeet()
        {
            if (m_Rig == null || m_Rig.Origin == null)
                return transform.position;
            var head = m_Rig.HeadTransform.position;
            return new Vector3(head.x, m_Rig.Origin.transform.position.y, head.z);
        }

        void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f || m_Rig == null)
                return;

            if (InputEnabled)
            {
                HandleButtons();
                HandleDesktopFallback(dt);
            }

            if (m_AutoWalking)
                StepAutoWalk(dt);

            Position = SampleFeet();
            var instantaneous = (Position - m_LastPosition) / dt;
            m_Velocity = Vector3.Lerp(m_Velocity, instantaneous, Mathf.Clamp01(dt * 8f));
            m_LastPosition = Position;

            UpdateRoadwayState();
            UpdatePrompt();
        }

        void HandleButtons()
        {
            var input = RoadReadyInput.Instance;
            if (input.CrossNow.WasPressedThisFrame())
            {
                m_LastCrossNowTime = Time.time;
                CrossNowPressed?.Invoke(SimClock.Now);
                if (!m_OnRoadway && m_Comfort != null && m_Comfort.assistedCrossing)
                    BeginAutoCross();
            }

            if (input.StopRetreat.WasPressedThisFrame())
            {
                StopPressed?.Invoke(SimClock.Now);
                if (m_AutoWalking || m_OnRoadway)
                {
                    // Step back to where the crossing started.
                    m_AutoTarget = m_CrossStartPosition;
                    m_AutoWalking = m_Comfort != null && m_Comfort.assistedCrossing;
                }
            }
        }

        void BeginAutoCross()
        {
            var crossing = RoadZone.FindCrossingNear(Position, 3f);
            Vector3 target;
            if (crossing != null)
            {
                var far = crossing.FarKerb(Position).position;
                var dir = far - crossing.NearKerb(Position).position;
                dir.y = 0f;
                target = far + dir.normalized * 0.8f;
            }
            else if (!TryFindFarEdge(Position, Forward, out target))
            {
                return;
            }

            m_CrossStartPosition = Position;
            m_AutoTarget = target;
            m_AutoWalking = true;
        }

        static bool TryFindFarEdge(Vector3 from, Vector3 direction, out Vector3 target)
        {
            var wasOnRoad = false;
            for (var d = 0.5f; d < 30f; d += 0.5f)
            {
                var p = from + direction * d;
                var onRoad = RoadZone.IsOnRoadway(p);
                if (wasOnRoad && !onRoad)
                {
                    target = p + direction * 0.5f;
                    return true;
                }

                wasOnRoad |= onRoad;
            }

            target = from;
            return false;
        }

        void StepAutoWalk(float dt)
        {
            var toTarget = m_AutoTarget - Position;
            toTarget.y = 0f;
            var speed = m_Comfort != null ? m_Comfort.walkSpeed : 1.3f;
            if (toTarget.magnitude <= speed * dt + 0.05f)
            {
                m_Rig.MoveHorizontal(toTarget);
                m_AutoWalking = false;
                return;
            }

            m_Rig.MoveHorizontal(toTarget.normalized * (speed * dt));
        }

        void HandleDesktopFallback(float dt)
        {
            if (XRSettings.isDeviceActive)
                return;
            var input = RoadReadyInput.Instance;
            var move = input.DesktopMove.ReadValue<Vector2>();
            if (move.sqrMagnitude > 0.01f && !m_AutoWalking && (m_Comfort == null || m_Comfort.freeWalking))
            {
                var forward = Forward;
                var right = new Vector3(forward.z, 0f, -forward.x);
                var speed = m_Comfort != null ? m_Comfort.walkSpeed : 1.3f;
                m_Rig.MoveHorizontal((forward * move.y + right * move.x) * (speed * dt));
            }

            if (input.DesktopLookHold.IsPressed())
            {
                var look = input.DesktopLook.ReadValue<Vector2>();
                m_Rig.RotateYaw(look.x * m_DesktopLookSensitivity);
            }
        }

        void UpdateRoadwayState()
        {
            var onRoad = RoadZone.IsOnRoadway(Position);
            WaitingAtCrossing = !onRoad ? RoadZone.FindCrossingNear(Position, k_KerbWaitRadius) : null;

            if (onRoad && !m_OnRoadway)
            {
                RoadwayEnterTime = SimClock.Now;
                if (!m_AutoWalking)
                    m_CrossStartPosition = m_LastPosition;
                var crossing = RoadZone.FindCrossingAt(Position, 0.5f);
                var direction = crossing != null && crossing.KerbA != null && crossing.KerbB != null
                    ? crossing.FarKerb(Position).position - crossing.NearKerb(Position).position
                    : (m_Velocity.sqrMagnitude > 0.05f ? m_Velocity : Forward);
                direction.y = 0f;
                var length = crossing != null ? crossing.CrossingLength : EstimateCrossingLength(Position, direction.normalized);
                m_CurrentCrossing = new CrossingEvent
                {
                    time = SimClock.Now,
                    position = Position,
                    crossing = crossing,
                    direction = direction.normalized,
                    crossingLength = length,
                    viaCrossNowButton = Time.time - m_LastCrossNowTime <= k_CrossNowLinkSeconds,
                };
                IsCrossing = true;
                CrossingStarted?.Invoke(m_CurrentCrossing);
            }
            else if (!onRoad && m_OnRoadway && IsCrossing)
            {
                IsCrossing = false;
                var progressed = Vector3.Dot(Position - m_CurrentCrossing.position, m_CurrentCrossing.direction);
                if (progressed > m_CurrentCrossing.crossingLength * 0.6f)
                {
                    HasCompletedCrossing = true;
                    CrossingCompleted?.Invoke(m_CurrentCrossing);
                }
                else
                {
                    CrossingAbandoned?.Invoke(m_CurrentCrossing);
                }
            }

            m_OnRoadway = onRoad;
        }

        static float EstimateCrossingLength(Vector3 entry, Vector3 direction)
        {
            for (var d = 0.5f; d < 30f; d += 0.5f)
                if (!RoadZone.IsOnRoadway(entry + direction * d))
                    return d;
            return 7f;
        }

        void UpdatePrompt()
        {
            if (!InputEnabled)
            {
                Prompt = null;
                return;
            }

            if (m_AutoWalking)
                Prompt = "Crossing...  B = step back";
            else if (!m_OnRoadway && WaitingAtCrossing != null)
                Prompt = m_Comfort != null && m_Comfort.assistedCrossing ? "Look both ways.  A = cross now" : "Look both ways, then walk across";
            else
                Prompt = null;
        }
    }
}
