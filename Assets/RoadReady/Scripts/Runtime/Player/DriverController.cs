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
        [SerializeField] float m_Acceleration = 4f;
        [SerializeField] float m_BrakeDeceleration = 7.5f;
        [SerializeField] float m_CoastDeceleration = 0.6f;

        [Header("Pedal feel (an automatic car)")]
        [Tooltip("Throttle pedal curve: above 1 the first half of the travel is gentle, so pulling away is smooth.")]
        [SerializeField] float m_ThrottleCurve = 1.5f;
        [Tooltip("Brake pedal curve: above 1 a light touch slows the car gently; full travel is a hard stop.")]
        [SerializeField] float m_BrakeCurve = 1.3f;
        [Tooltip("How fast the pedals travel (full travel per second) when pressed and when released.")]
        [SerializeField] float m_PedalRise = 5f;
        [SerializeField] float m_PedalFall = 8f;
        [Tooltip("Share of the pull still left at top speed (engines pull less the faster they spin).")]
        [SerializeField] float m_TopSpeedPull = 0.35f;
        [Tooltip("Air and rolling resistance: extra slowing with the square of the speed (m/s per m/s squared).")]
        [SerializeField] float m_Drag = 0.004f;
        [Tooltip("Pedal travel below this counts as released (wheel pedals rest with a little noise).")]
        [SerializeField] float m_PedalDeadZone = 0.04f;
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
        public Gear Gear { get; private set; } = Gear.Park;
        public event Action<Gear> GearChanged;

        /// <summary>The engine. Off at the start of every drive: the car does not move until the ON button is pressed.</summary>
        public bool EngineOn { get; private set; }
        public event Action<bool> EngineChanged;

        /// <summary>Starts the engine (the ON button, O key or wheel button). It can only be switched off when the car is stopped.</summary>
        public void SetEngine(bool on)
        {
            if (on == EngineOn || (!on && SpeedKmh > 1f))
                return;
            EngineOn = on;
            EngineChanged?.Invoke(on);
        }

        /// <summary>Handbrake applied (set by the handbrake lever or the B key).</summary>
        public bool Handbrake { get; private set; } = true;
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
                if (!EngineOn)
                    return 0f;
                const float idle = 850f, gearTopKmh = 18f;
                var kmh = SpeedKmh;
                var inGear = kmh < 1f ? 0f : Mathf.Repeat(kmh, gearTopKmh) / gearTopKmh;
                var gear = Mathf.Min(5f, Mathf.Floor(kmh / gearTopKmh));
                return idle + (inGear * (3200f - 400f * gear) + gear * 300f) + Throttle * 600f;
            }
        }
        public float Length => m_Length;
        public float Wheelbase => m_Wheelbase;
        public float Width => m_Width;
        public bool IsPlayer => true;
        public bool IsYielding => false;
        public Transform Transform => transform;
        public Transform EyePoint => m_EyePoint;

        bool m_InputEnabled;
        float m_InputEnabledAt = -100f;
        bool m_ThrottleArmed;
        static bool s_WarnedWheel;

        public bool InputEnabled
        {
            get => m_InputEnabled;
            set
            {
                if (value && !m_InputEnabled)
                {
                    m_InputEnabledAt = Time.unscaledTime;
                    m_ThrottleArmed = false; // the accelerator must be seen released once before it can drive the car
                }
                m_InputEnabled = value;
            }
        }
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
            // Every drive starts in neutral with the engine off and the handbrake up: the car only moves once the engine is on,
            // the handbrake is down and D (or R) is selected.
            SetGear(Gear.Neutral);
            SetEngine(false);
            SetHandbrake(true);
            Debug.Log($"[RoadReady] Car ready for a new drive: gear {Gear}, engine {(EngineOn ? "on" : "off")}, handbrake {(Handbrake ? "up" : "down")}.");
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

            // Longitudinal. Park, the handbrake or a stopped engine hold the car still, whatever else happens. D only ever moves it
            // forwards and R only backwards; N does not drive and brings the car to a stop.
            if (Gear == Gear.Park || Handbrake || !EngineOn)
            {
                m_Speed = 0f;
            }
            else
            {
                // Pedals work like an automatic's: a curved throttle, a pull that fades as the speed rises, creep with both
                // pedals off, engine braking and drag when you lift, and a brake that is gentle at first.
                var direction = Gear switch { Gear.Drive => 1f, Gear.Reverse => -1f, _ => 0f };
                var kmh = Mathf.Abs(m_Speed) * Units.MsToKmh;
                var pull = Mathf.Lerp(1f, m_TopSpeedPull, Mathf.Clamp01(kmh / m_MaxSpeedKmh));
                var drive = direction * Mathf.Pow(Throttle, m_ThrottleCurve) * m_Acceleration * pull;
                // No creep: in D or R the car only moves while the throttle is pressed (parking and tutorial alike).
                var engineBraking = Throttle < 0.05f ? m_CoastDeceleration : 0f;
                var resist = Mathf.Pow(Brake, m_BrakeCurve) * m_BrakeDeceleration + engineBraking + m_Drag * m_Speed * m_Speed + (Gear == Gear.Neutral ? m_BrakeDeceleration : 0f);
                var speed = m_Speed + drive * dt;
                var slow = resist * dt;
                speed = Mathf.Abs(speed) <= slow ? 0f : speed - Mathf.Sign(speed) * slow;
                if (Gear == Gear.Drive) speed = Mathf.Max(0f, speed);
                else if (Gear == Gear.Reverse) speed = Mathf.Min(0f, speed);
                m_Speed = Mathf.Clamp(speed, -m_MaxReverseKmh * Units.KmhToMs, m_MaxSpeedKmh * Units.KmhToMs);
            }

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
            var throttle = DeadZone(input.Throttle.ReadValue<float>());
            var brake = DeadZone(input.Brake.ReadValue<float>());
            var stick = Mathf.Clamp(DeadZone(input.Steer.ReadValue<float>()), -1f, 1f);

            // A USB driving rig (e.g. G29): pedals add to the other inputs, the wheel steers unless the keys are used.
            var rig = RacingWheel.Current;
            if (rig != null)
            {
                throttle = Mathf.Max(throttle, PedalDeadZone(rig.Throttle));
                brake = Mathf.Max(brake, PedalDeadZone(rig.Brake));
                if (stick == 0f)
                    stick = rig.Steer;
            }
            // Safety: when control is handed over the accelerator only counts after it has been seen released once, so a
            // pedal (or trigger) that already reads pressed can never make the car drive off by itself.
            if (!m_ThrottleArmed)
            {
                if (throttle < 0.05f)
                    m_ThrottleArmed = true;
                else
                    throttle = 0f;
            }

            // Real pedals have travel and a spring: the pedal position eases towards the input (quick to press, quicker to
            // release), which also takes the jolt out of keyboard and trigger inputs.
            Throttle = Mathf.MoveTowards(Throttle, throttle, (throttle > Throttle ? m_PedalRise : m_PedalFall) * Time.deltaTime);
            Brake = Mathf.MoveTowards(Brake, brake, (brake > Brake ? m_PedalRise : m_PedalFall) * Time.deltaTime);

            // A wheel that was never calibrated may have its pedals on the wrong axes: say how to calibrate it, once.
            if (rig != null && !s_WarnedWheel && !rig.IsCalibrated)
            {
                s_WarnedWheel = true;
                RoadReadyEvents.Toast("Wheel pedals not calibrated: press F3 on the keyboard and follow the steps on the monitor", 7f);
            }

            if (m_Wheel != null && m_Wheel.IsHeld)
            {
                Steer = m_Wheel.SteerInput;
            }
            else
            {
                Steer = stick;
                // Keep the wheel in step so grabbing it does not make the steering jump (and so the virtual wheel
                // turns with a real one).
                if (m_Wheel != null) m_Wheel.FollowSteering(stick);
            }

            // Engine, handbrake and gear only respond once control has been handed over for a moment, so a button that is
            // already down on the wheel (or a stray press) cannot start the engine or release the handbrake by itself.
            var armed = Time.unscaledTime - m_InputEnabledAt >= 0.75f;
            if (armed && (input.Engine.WasPressedThisFrame() || RacingWheel.Pressed(RacingWheel.EngineButton)))
                SetEngine(!EngineOn);
            if (armed && (input.Handbrake.WasPressedThisFrame() || RacingWheel.Pressed(RacingWheel.HandbrakeButton)))
                SetHandbrake(!Handbrake);
            if (armed && input.ShiftGear.WasPressedThisFrame())
                SetGear(Gear switch { Gear.Drive => Gear.Neutral, Gear.Neutral => Gear.Reverse, Gear.Reverse => Gear.Park, _ => Gear.Drive });
            // Wheel R2 moves the lever towards D (P, R, N, D), L2 towards P (the D-pad adjusts the mirrors).
            if (armed && RacingWheel.Pressed(RacingWheel.GearTowardsDrive) && Gear != Gear.Drive)
                SetGear(Gear + 1);
            if (armed && RacingWheel.Pressed(RacingWheel.GearTowardsPark) && Gear != Gear.Park)
                SetGear(Gear - 1);
            // The car is an automatic, so the game chooses the gears: like any game set to automatic, it ignores the G29's
            // H-shifter and paddles for gear changes (the paddles work the indicators below). P, R, N and D come only from the
            // car's own gear lever: grab it, press G, or use R2 / L2 on the wheel.

            if (input.IndicatorLeft.WasPressedThisFrame() || RacingWheel.Pressed(RacingWheel.LeftPaddle))
                SetIndicator(Indicator == TurnSide.Left ? TurnSide.None : TurnSide.Left);
            if (input.IndicatorRight.WasPressedThisFrame() || RacingWheel.Pressed(RacingWheel.RightPaddle))
                SetIndicator(Indicator == TurnSide.Right ? TurnSide.None : TurnSide.Right);
            if (input.Horn.WasPressedThisFrame() || RacingWheel.Pressed(RacingWheel.HornButton))
            {
                if (m_HornAudio != null) m_HornAudio.Play();
                HornPressed?.Invoke();
            }
        }

        float DeadZone(float value) => Mathf.Abs(value) < m_DeadZone ? 0f : value;

        float PedalDeadZone(float value) => value < m_PedalDeadZone ? 0f : Mathf.InverseLerp(m_PedalDeadZone, 1f, value);

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
