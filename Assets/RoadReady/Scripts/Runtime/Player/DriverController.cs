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
        [Tooltip("Extra deceleration while the handbrake is applied (it holds the car; driving against it is very slow).")]
        [SerializeField] float m_HandbrakeDeceleration = 6f;
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
        [Tooltip("Local axis the steering wheel spins around (its column). The wheel's rest rotation is kept.")]
        [SerializeField] Vector3 m_SteeringAxis = Vector3.forward;
        [SerializeField] Transform[] m_FrontWheels = Array.Empty<Transform>();
        [SerializeField] Transform[] m_AllWheels = Array.Empty<Transform>();
        [SerializeField] float m_WheelRadius = 0.32f;
        [SerializeField] Renderer[] m_BrakeLights = Array.Empty<Renderer>();
        [SerializeField] Renderer[] m_LeftIndicators = Array.Empty<Renderer>();
        [SerializeField] Renderer[] m_RightIndicators = Array.Empty<Renderer>();
        [Tooltip("Lamps that are one material slot of a larger mesh (imported car models): only that slot glows.")]
        [SerializeField] LampSlot[] m_BrakeLampSlots = Array.Empty<LampSlot>();
        [SerializeField] LampSlot[] m_LeftIndicatorSlots = Array.Empty<LampSlot>();
        [SerializeField] LampSlot[] m_RightIndicatorSlots = Array.Empty<LampSlot>();
        [SerializeField] SteeringWheelGrab m_Wheel;
        [Tooltip("Top speed in reverse (km/h).")]
        [SerializeField] float m_MaxReverseKmh = 12f;
        [SerializeField] AudioSource m_EngineAudio;
        [SerializeField] AudioSource m_HornAudio;
        [SerializeField] AudioSource m_IndicatorAudio;
        [SerializeField] AudioSource m_CrashAudio;

        MaterialPropertyBlock m_Block;
        Quaternion m_SteeringRest = Quaternion.identity;
        bool m_HasSteeringRest;
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
        /// <summary>Speed magnitude in m/s (also while reversing; use <see cref="SignedSpeed"/> for direction).</summary>
        public float Speed => Mathf.Abs(m_Speed);
        public float SpeedKmh => Mathf.Abs(m_Speed) * Units.MsToKmh;
        /// <summary>Negative while reversing.</summary>
        public float SignedSpeed => m_Speed;

        /// <summary>Automatic gearbox position, set by the gear shifter (or the G key).</summary>
        public Gear Gear { get; private set; } = Gear.Drive;
        public event Action<Gear> GearChanged;

        /// <summary>Handbrake applied (set by the handbrake lever or the H key).</summary>
        public bool Handbrake { get; private set; }
        public event Action<bool> HandbrakeChanged;

        public void SetHandbrake(bool applied)
        {
            if (applied == Handbrake)
                return;
            Handbrake = applied;
            HandbrakeChanged?.Invoke(applied);
        }

        /// <summary>Hand-held steering wheel; when held it overrides the thumbstick.</summary>
        public SteeringWheelGrab Wheel { get => m_Wheel; set => m_Wheel = value; }

        public void SetGear(Gear gear)
        {
            if (gear == Gear)
                return;
            Gear = gear;
            GearChanged?.Invoke(gear);
        }

        /// <summary>Simulated engine speed for the rev counter (the model has no gearbox): idle plus a sawtooth through 5 gears.</summary>
        public float Rpm
        {
            get
            {
                const float idle = 850f, gearTopKmh = 18f;
                var kmh = SpeedKmh;
                var inGear = kmh < 1f ? 0f : Mathf.Repeat(kmh, gearTopKmh) / gearTopKmh;
                var gear = Mathf.Min(5f, Mathf.Floor(kmh / gearTopKmh));
                return idle + (inGear * (3200f - 400f * gear) + gear * 300f) + Throttle * 600f;
            }
        }
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
            SetGear(Gear.Drive);
            SetHandbrake(false);
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

            // Longitudinal: the gear sets which way the throttle drives; brakes, coasting and Park always oppose motion.
            var drive = Gear switch { Gear.Drive => 1f, Gear.Reverse => -1f, _ => 0f } * Throttle * m_Acceleration;
            var resist = Brake * m_BrakeDeceleration + (Throttle < 0.05f || drive == 0f ? m_CoastDeceleration : 0f) + (Gear == Gear.Park ? m_BrakeDeceleration : 0f) + (Handbrake ? m_HandbrakeDeceleration : 0f);
            var speed = m_Speed + drive * dt;
            var slow = resist * dt;
            speed = Mathf.Abs(speed) <= slow ? 0f : speed - Mathf.Sign(speed) * slow;
            m_Speed = Mathf.Clamp(speed, -m_MaxReverseKmh * Units.KmhToMs, m_MaxSpeedKmh * Units.KmhToMs);

            // Lateral: steering authority drops with speed for stability.
            var speedFactor = Mathf.Lerp(1f, m_HighSpeedSteerFactor, Mathf.Abs(m_Speed) / (m_MaxSpeedKmh * Units.KmhToMs));
            var targetSteer = Steer * m_MaxSteerDegrees * speedFactor;
            m_SteerAngle = Mathf.MoveTowards(m_SteerAngle, targetSteer, m_MaxSteerDegrees * m_SteerResponse * dt);
            var yawRate = m_Speed / m_Wheelbase * Mathf.Tan(m_SteerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            m_Heading += yawRate * dt;
            YawRateDegrees = yawRate;

            var rotation = Quaternion.Euler(0f, m_Heading, 0f);
            var forward = rotation * Vector3.forward;
            var step = forward * (m_Speed * dt);

            if (Mathf.Abs(m_Speed) > 0.05f && CheckObstacle(m_Speed >= 0f ? forward : -forward, step.magnitude, out var hit))
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
            var stick = Mathf.Clamp(DeadZone(input.Steer.ReadValue<float>()), -1f, 1f);
            if (m_Wheel != null && m_Wheel.IsHeld)
            {
                Steer = m_Wheel.SteerInput;
            }
            else
            {
                Steer = stick;
                // Keep the wheel in step so grabbing it does not make the steering jump.
                if (m_Wheel != null) m_Wheel.FollowSteering(stick);
            }

            if (input.Handbrake.WasPressedThisFrame())
                SetHandbrake(!Handbrake);
            if (input.ShiftGear.WasPressedThisFrame())
                SetGear(Gear switch { Gear.Drive => Gear.Neutral, Gear.Neutral => Gear.Reverse, Gear.Reverse => Gear.Park, _ => Gear.Drive });

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
            {
                if (!m_HasSteeringRest)
                {
                    m_SteeringRest = m_SteeringWheel.localRotation;
                    m_HasSteeringRest = true;
                }

                // While held, the rim follows the hands exactly; otherwise it shows the road-wheel angle.
                var wheelDegrees = m_Wheel != null && m_Wheel.IsHeld ? m_Wheel.WheelAngle : m_SteerAngle * m_SteeringWheelRatio;
                m_SteeringWheel.localRotation = m_SteeringRest * Quaternion.AngleAxis(-wheelDegrees, m_SteeringAxis);
            }
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
            SetEmission(m_BrakeLampSlots, Brake > 0.1f, Color.red * 4f);
            SetEmission(m_LeftIndicatorSlots, Indicator == TurnSide.Left && blinkOn, new Color(1f, 0.55f, 0f) * 4f);
            SetEmission(m_RightIndicatorSlots, Indicator == TurnSide.Right && blinkOn, new Color(1f, 0.55f, 0f) * 4f);

            if (m_EngineAudio != null)
                m_EngineAudio.pitch = 0.7f + Mathf.Abs(m_Speed) / 12f + Throttle * 0.15f;
        }

        void SetEmission(LampSlot[] slots, bool on, Color onEmission)
        {
            if (slots.Length == 0)
                return;
            m_Block ??= new MaterialPropertyBlock();
            foreach (var slot in slots)
            {
                if (slot.renderer == null) continue;
                slot.renderer.GetPropertyBlock(m_Block, slot.materialIndex);
                m_Block.SetColor("_EmissionColor", on ? onEmission : slot.offEmission);
                slot.renderer.SetPropertyBlock(m_Block, slot.materialIndex);
            }
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

    /// <summary>One material slot of a renderer that acts as a lamp, and the emission it shows when off.</summary>
    [Serializable]
    public struct LampSlot
    {
        public Renderer renderer;
        public int materialIndex;
        public Color offEmission;
    }
}
