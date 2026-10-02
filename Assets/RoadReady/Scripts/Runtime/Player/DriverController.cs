using System;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// The learner's vehicle (FR3). Simplified, deterministic kinematic bicycle model driven from Update (not
    /// FixedUpdate) so the seated camera never judders - a major simulator-sickness trigger.
    /// Left-hand drive, right-hand traffic (Rwanda).
    /// </summary>
    public class DriverController : MonoBehaviour, IRoadAgent
    {
        [Header("Seat")]
        [Tooltip("Where the learner's eyes should be. Forward = straight ahead through the windscreen.")]
        [SerializeField] Transform m_EyePoint;

        [Header("Dimensions")]
        [SerializeField] float m_Length = 4.3f;
        [SerializeField] float m_Width = 1.8f;
        [SerializeField] float m_Wheelbase = 2.6f;

        [Header("Handling")]
        [SerializeField] float m_MaxSpeedKmh = 70f;
        [SerializeField] float m_Acceleration = 3.2f;
        [SerializeField] float m_BrakeDeceleration = 7.5f;
        [SerializeField] float m_CoastDeceleration = 0.6f;
        [SerializeField] float m_MaxSteerDegrees = 32f;
        [Tooltip("Steering authority at max speed relative to standstill.")]
        [SerializeField] float m_HighSpeedSteerFactor = 0.35f;
        [SerializeField] float m_SteerResponse = 4f;
        [Tooltip("Input dead zone for analogue triggers / stick.")]
        [SerializeField] float m_DeadZone = 0.08f;

        [Header("Collision")]
        [SerializeField] LayerMask m_ObstacleMask = ~0;
        [SerializeField] LayerMask m_GroundMask = ~0;
        [SerializeField] bool m_FollowGround = true;

        [Header("Visuals (optional)")]
        [SerializeField] Transform m_SteeringWheel;
        [SerializeField] float m_SteeringWheelRatio = 14f;
        [SerializeField] Transform[] m_FrontWheels = Array.Empty<Transform>();
        [SerializeField] Transform[] m_AllWheels = Array.Empty<Transform>();
        [SerializeField] float m_WheelRadius = 0.32f;
        [SerializeField] Renderer[] m_BrakeLights = Array.Empty<Renderer>();
        [SerializeField] Renderer[] m_LeftIndicators = Array.Empty<Renderer>();
        [SerializeField] Renderer[] m_RightIndicators = Array.Empty<Renderer>();
        [SerializeField] AudioSource m_EngineAudio;
        [SerializeField] AudioSource m_HornAudio;
        [SerializeField] AudioSource m_IndicatorAudio;
        [SerializeField] AudioSource m_CrashAudio;

        MaterialPropertyBlock m_Block;
        float m_Speed;
        float m_SteerAngle;
        float m_Heading;
        float m_IndicatorHeadingAtOn;
        float m_LastSpeed;
        bool m_Registered;
        Vector3 m_Velocity;

        public event Action<Collider> ObstacleHit;
        public event Action HornPressed;
        public event Action<TurnSide> IndicatorChanged;

        public RoadUserKind Kind => RoadUserKind.Car;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => m_Velocity;
        public Vector3 Forward => transform.forward;
        public float Speed => m_Speed;
        public float SpeedKmh => m_Speed * Units.MsToKmh;
        public float Length => m_Length;
        public float Width => m_Width;
        public bool IsPlayer => true;
        public bool IsYielding => false;
        public Transform Transform => transform;
        public Transform EyePoint => m_EyePoint;

        public bool InputEnabled { get; set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }
        public float Acceleration { get; private set; }
        public float YawRateDegrees { get; private set; }
        public TurnSide Indicator { get; private set; }
        public float LastIndicatorTime { get; private set; } = -100f;

        public void Configure(Transform eyePoint, Transform steeringWheel, Renderer[] brakeLights, Renderer[] leftIndicators, Renderer[] rightIndicators)
        {
            m_EyePoint = eyePoint;
            m_SteeringWheel = steeringWheel;
            m_BrakeLights = brakeLights ?? Array.Empty<Renderer>();
            m_LeftIndicators = leftIndicators ?? Array.Empty<Renderer>();
            m_RightIndicators = rightIndicators ?? Array.Empty<Renderer>();
        }

        void OnEnable()
        {
            m_Heading = transform.eulerAngles.y;
            SetRegistered(true);
        }

        void OnDisable() => SetRegistered(false);

        void SetRegistered(bool registered)
        {
            if (registered == m_Registered)
                return;
            m_Registered = registered;
            if (registered) RoadAgentRegistry.Register(this);
            else RoadAgentRegistry.Unregister(this);
        }

        public void ResetTo(Transform spawn)
        {
            transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
            m_Heading = spawn.eulerAngles.y;
            m_Speed = 0f;
            m_LastSpeed = 0f;
            m_SteerAngle = 0f;
            m_Velocity = Vector3.zero;
            SetIndicator(TurnSide.None);
        }

        public void Stop()
        {
            m_Speed = 0f;
            m_Velocity = Vector3.zero;
        }

        void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            ReadInput();

            // Longitudinal.
            var accel = Throttle * m_Acceleration - Brake * m_BrakeDeceleration - (Throttle < 0.05f ? m_CoastDeceleration : 0f);
            m_Speed = Mathf.Clamp(m_Speed + accel * dt, 0f, m_MaxSpeedKmh * Units.KmhToMs);

            // Lateral: steering authority drops with speed for stability.
            var speedFactor = Mathf.Lerp(1f, m_HighSpeedSteerFactor, m_Speed / (m_MaxSpeedKmh * Units.KmhToMs));
            var targetSteer = Steer * m_MaxSteerDegrees * speedFactor;
            m_SteerAngle = Mathf.MoveTowards(m_SteerAngle, targetSteer, m_MaxSteerDegrees * m_SteerResponse * dt);
            var yawRate = m_Speed / m_Wheelbase * Mathf.Tan(m_SteerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            m_Heading += yawRate * dt;
            YawRateDegrees = yawRate;

            var rotation = Quaternion.Euler(0f, m_Heading, 0f);
            var forward = rotation * Vector3.forward;
            var step = forward * (m_Speed * dt);

            if (m_Speed > 0.05f && CheckObstacle(forward, step.magnitude, out var hit))
            {
                step = Vector3.zero;
                m_Speed = 0f;
                if (m_CrashAudio != null) m_CrashAudio.Play();
                ObstacleHit?.Invoke(hit);
            }

            var position = transform.position + step;
            if (m_FollowGround)
                position.y = SampleGround(position, transform.position.y);
            var previous = transform.position;
            transform.SetPositionAndRotation(position, rotation);

            m_Velocity = (transform.position - previous) / dt;
            Acceleration = (m_Speed - m_LastSpeed) / dt;
            m_LastSpeed = m_Speed;

            AutoCancelIndicator();
            UpdateVisuals(dt);
        }

        void ReadInput()
        {
            if (!InputEnabled)
            {
                Throttle = 0f;
                Brake = 1f;
                Steer = 0f;
                return;
            }

            var input = RoadReadyInput.Instance;
            Throttle = DeadZone(input.Throttle.ReadValue<float>());
            Brake = DeadZone(input.Brake.ReadValue<float>());
            Steer = Mathf.Clamp(DeadZone(input.Steer.ReadValue<float>()), -1f, 1f);

            if (input.IndicatorLeft.WasPressedThisFrame())
                SetIndicator(Indicator == TurnSide.Left ? TurnSide.None : TurnSide.Left);
            if (input.IndicatorRight.WasPressedThisFrame())
                SetIndicator(Indicator == TurnSide.Right ? TurnSide.None : TurnSide.Right);
            if (input.Horn.WasPressedThisFrame())
            {
                if (m_HornAudio != null) m_HornAudio.Play();
                HornPressed?.Invoke();
            }
        }

        float DeadZone(float value) => Mathf.Abs(value) < m_DeadZone ? 0f : value;

        public void SetIndicator(TurnSide side)
        {
            if (Indicator == side)
                return;
            Indicator = side;
            m_IndicatorHeadingAtOn = m_Heading;
            if (side != TurnSide.None)
                LastIndicatorTime = Time.time;
            if (m_IndicatorAudio != null)
            {
                if (side != TurnSide.None) m_IndicatorAudio.Play();
                else m_IndicatorAudio.Stop();
            }

            IndicatorChanged?.Invoke(side);
        }

        /// <summary>Like a real stalk: cancels after a turn of 60+ degrees once the wheel straightens.</summary>
        void AutoCancelIndicator()
        {
            if (Indicator == TurnSide.None)
                return;
            if (Mathf.Abs(Mathf.DeltaAngle(m_IndicatorHeadingAtOn, m_Heading)) > 60f && Mathf.Abs(m_SteerAngle) < 4f)
                SetIndicator(TurnSide.None);
        }

        bool CheckObstacle(Vector3 forward, float distance, out Collider hitCollider)
        {
            hitCollider = null;
            var center = transform.position + Vector3.up * 0.8f;
            var halfExtents = new Vector3(m_Width * 0.45f, 0.4f, 0.1f);
            var hits = Physics.BoxCastAll(center, halfExtents, forward, Quaternion.LookRotation(forward), m_Length * 0.5f + distance, m_ObstacleMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                    continue;
                if (hit.collider.GetComponentInParent<IRoadAgent>() != null)
                    continue; // road users are handled geometrically by the collision rule
                if (hit.collider is CharacterController)
                    continue;
                hitCollider = hit.collider;
                return true;
            }

            return false;
        }

        float SampleGround(Vector3 position, float fallback)
        {
            var origin = position + Vector3.up * 2f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 5f, m_GroundMask, QueryTriggerInteraction.Ignore);
            var best = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<IRoadAgent>() != null)
                    continue;
                if (hit.point.y > best)
                    best = hit.point.y;
            }

            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        void UpdateVisuals(float dt)
        {
            if (m_SteeringWheel != null)
                m_SteeringWheel.localRotation = Quaternion.Euler(0f, 0f, -m_SteerAngle * m_SteeringWheelRatio);
            foreach (var wheel in m_FrontWheels)
                if (wheel != null) wheel.localRotation = Quaternion.Euler(wheel.localEulerAngles.x, m_SteerAngle, 0f);
            if (m_WheelRadius > 0f)
            {
                var degrees = m_Speed * dt / m_WheelRadius * Mathf.Rad2Deg;
                foreach (var wheel in m_AllWheels)
                    if (wheel != null) wheel.Rotate(degrees, 0f, 0f, Space.Self);
            }

            var blinkOn = Mathf.Repeat(Time.time, 0.7f) < 0.35f;
            SetEmission(m_BrakeLights, Brake > 0.1f, Color.red);
            SetEmission(m_LeftIndicators, Indicator == TurnSide.Left && blinkOn, new Color(1f, 0.55f, 0f));
            SetEmission(m_RightIndicators, Indicator == TurnSide.Right && blinkOn, new Color(1f, 0.55f, 0f));

            if (m_EngineAudio != null)
                m_EngineAudio.pitch = 0.7f + m_Speed / 12f + Throttle * 0.15f;
        }

        void SetEmission(Renderer[] renderers, bool on, Color color)
        {
            if (renderers.Length == 0)
                return;
            m_Block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(m_Block);
                m_Block.SetColor("_EmissionColor", on ? color * 3f : Color.black);
                m_Block.SetColor("_BaseColor", on ? color : color * 0.25f);
                r.SetPropertyBlock(m_Block);
            }
        }
    }
}
