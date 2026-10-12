using System.Collections;
using RoadReady.Core;
using RoadReady.Scenarios;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// Sound of the learner's car (Vehicle Essentials clips): getting in and starting up, an engine that blends idle,
    /// 1000 and 2000 rpm loops with the simulated rpm, road noise with speed, horn, indicator tick-tock in time with
    /// the blinking arrows, gear-lever and handbrake clicks, a collision thud and the engine stopping at the end.
    /// </summary>
    [RequireComponent(typeof(DriverController))]
    public class PlayerCarAudio : MonoBehaviour
    {
        [Header("Start / stop")]
        [SerializeField] AudioClip m_DoorClose;
        [SerializeField] AudioClip m_SeatBelt;
        [SerializeField] AudioClip m_Keys;
        [SerializeField] AudioClip m_EngineStart;
        [SerializeField] AudioClip m_EngineStop;

        [Header("Loops")]
        [SerializeField] AudioClip m_Idle;
        [SerializeField] AudioClip m_Rpm1000;
        [SerializeField] AudioClip m_Rpm2000;
        [SerializeField] AudioClip m_RoadNoise;

        [Header("Controls")]
        [SerializeField] AudioClip m_Horn;
        [SerializeField] AudioClip m_IndicatorTick;
        [SerializeField] AudioClip m_IndicatorTock;
        [SerializeField] AudioClip m_GearShift;
        [SerializeField] AudioClip m_Handbrake;
        [SerializeField] AudioClip m_Impact;

        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] float m_EngineVolume = 0.55f;
        [Range(0f, 1f)] [SerializeField] float m_RoadVolume = 0.5f;
        [Range(0f, 1f)] [SerializeField] float m_InteriorVolume = 0.8f;

        DriverController m_Driver;
        AudioSource m_IdleSource, m_LowSource, m_HighSource, m_RoadSource, m_HornSource, m_OneShots;
        ScenarioManager m_Scenarios;
        bool m_EngineRunning, m_WasInputEnabled, m_LastBlink;
        Coroutine m_Startup;

        public void Configure(AudioClip doorClose, AudioClip seatBelt, AudioClip keys, AudioClip engineStart, AudioClip engineStop,
            AudioClip idle, AudioClip rpm1000, AudioClip rpm2000, AudioClip roadNoise,
            AudioClip horn, AudioClip tick, AudioClip tock, AudioClip gearShift, AudioClip handbrake, AudioClip impact)
        {
            m_DoorClose = doorClose; m_SeatBelt = seatBelt; m_Keys = keys; m_EngineStart = engineStart; m_EngineStop = engineStop;
            m_Idle = idle; m_Rpm1000 = rpm1000; m_Rpm2000 = rpm2000; m_RoadNoise = roadNoise;
            m_Horn = horn; m_IndicatorTick = tick; m_IndicatorTock = tock; m_GearShift = gearShift; m_Handbrake = handbrake; m_Impact = impact;
        }

        void Awake()
        {
            m_Driver = GetComponent<DriverController>();
            // Engine sounds come from under the bonnet, the rest from the cabin.
            var engineBay = new Vector3(0f, 0.6f, 1.6f);
            var cabin = new Vector3(-0.3f, 1.0f, 0.3f);
            m_IdleSource = Source("Engine idle", m_Idle, engineBay, true);
            m_LowSource = Source("Engine 1000 rpm", m_Rpm1000, engineBay, true);
            m_HighSource = Source("Engine 2000 rpm", m_Rpm2000, engineBay, true);
            m_RoadSource = Source("Road noise", m_RoadNoise, new Vector3(0f, 0.3f, 0f), true);
            m_HornSource = Source("Horn", m_Horn, new Vector3(0f, 0.6f, 2.1f), false);
            m_OneShots = Source("Cabin", null, cabin, false);
            // One-shots are scaled by the source volume, so this one stays at full volume.
            m_OneShots.volume = 1f;
        }

        void OnEnable()
        {
            m_Driver.HornPressed += OnHorn;
            m_Driver.GearChanged += OnGear;
            m_Driver.HandbrakeChanged += OnHandbrake;
            m_Driver.ObstacleHit += OnObstacle;
            m_Scenarios = FindAnyObjectByType<ScenarioManager>();
            if (m_Scenarios != null)
            {
                m_Scenarios.CollisionOccurred += OnCollision;
                m_Scenarios.StateChanged += OnAttemptState;
            }

            m_WasInputEnabled = false;
            SetEngine(false, immediate: true);
        }

        void OnDisable()
        {
            m_Driver.HornPressed -= OnHorn;
            m_Driver.GearChanged -= OnGear;
            m_Driver.HandbrakeChanged -= OnHandbrake;
            m_Driver.ObstacleHit -= OnObstacle;
            if (m_Scenarios != null)
            {
                m_Scenarios.CollisionOccurred -= OnCollision;
                m_Scenarios.StateChanged -= OnAttemptState;
            }

            SetEngine(false, immediate: true);
        }

        void OnAttemptState(AttemptState state)
        {
            // The countdown is the moment the learner "gets in": door, seat belt, key, engine.
            if (state == AttemptState.Countdown && isActiveAndEnabled)
            {
                if (m_Startup != null) StopCoroutine(m_Startup);
                m_Startup = StartCoroutine(StartUp());
            }
            else if (state == AttemptState.Finished && m_EngineRunning)
            {
                SetEngine(false, immediate: false);
                Play(m_EngineStop, 0.8f);
            }
        }

        IEnumerator StartUp()
        {
            SetEngine(false, immediate: true);
            Play(m_DoorClose, 0.7f);
            yield return new WaitForSecondsRealtime(0.6f);
            Play(m_SeatBelt, 0.8f);
            yield return new WaitForSecondsRealtime(0.5f);
            Play(m_Keys, 0.6f);
            yield return new WaitForSecondsRealtime(0.35f);
            Play(m_EngineStart, 0.9f);
            yield return new WaitForSecondsRealtime(m_EngineStart != null ? Mathf.Min(1.2f, m_EngineStart.length * 0.6f) : 0f);
            SetEngine(true, immediate: false);
            m_Startup = null;
        }

        void Update()
        {
            // Safety net: if input was enabled without a countdown (e.g. restart), make sure the engine runs.
            if (m_Driver.InputEnabled && !m_WasInputEnabled && !m_EngineRunning && m_Startup == null)
                SetEngine(true, immediate: false);
            m_WasInputEnabled = m_Driver.InputEnabled;

            UpdateEngine();
            UpdateIndicator();
        }

        void UpdateEngine()
        {
            var running = m_EngineRunning ? 1f : 0f;
            // Simulated rpm from the DriverController (idle ~850, gears pull it up to ~3000).
            var rpm = Mathf.Clamp(m_Driver.Rpm, 700f, 3500f);
            var low = Mathf.InverseLerp(900f, 1600f, rpm);
            var high = Mathf.InverseLerp(1600f, 2600f, rpm);
            var throttleLift = 0.75f + 0.25f * m_Driver.Throttle;

            Fade(m_IdleSource, running * (1f - low) * m_EngineVolume, 1f);
            Fade(m_LowSource, running * low * (1f - high) * m_EngineVolume * throttleLift, Mathf.Lerp(0.85f, 1.15f, Mathf.InverseLerp(900f, 1600f, rpm)));
            Fade(m_HighSource, running * high * m_EngineVolume * throttleLift, Mathf.Lerp(0.9f, 1.35f, Mathf.InverseLerp(1600f, 3500f, rpm)));

            var speed = Mathf.InverseLerp(2f, 60f, m_Driver.SpeedKmh);
            Fade(m_RoadSource, speed * m_RoadVolume, Mathf.Lerp(0.85f, 1.2f, speed));
        }

        void UpdateIndicator()
        {
            // Same timing as the dashboard arrows: 0.35 s on, 0.35 s off.
            var blink = m_Driver.Indicator != TurnSide.None && Mathf.Repeat(Time.time, 0.7f) < 0.35f;
            if (m_Driver.Indicator != TurnSide.None && blink != m_LastBlink)
                Play(blink ? m_IndicatorTick : m_IndicatorTock, 0.45f);
            m_LastBlink = blink;
        }

        void OnHorn()
        {
            if (m_Horn == null) return;
            m_HornSource.volume = m_InteriorVolume;
            m_HornSource.Play();
        }

        void OnGear(Gear gear)
        {
            Play(m_GearShift, 0.8f);
        }

        void OnHandbrake(bool applied) => Play(m_Handbrake, applied ? 0.9f : 0.6f);

        void OnObstacle(Collider hit) => Play(m_Impact, 1f);

        void OnCollision(string what) => Play(m_Impact, 1f);

        void SetEngine(bool running, bool immediate)
        {
            m_EngineRunning = running;
            foreach (var source in new[] { m_IdleSource, m_LowSource, m_HighSource, m_RoadSource })
            {
                if (source == null || source.clip == null) continue;
                if (running && !source.isPlaying)
                {
                    source.volume = 0f;
                    source.time = Random.Range(0f, source.clip.length * 0.9f); // de-phase the loops
                    source.Play();
                }
                else if (!running && immediate)
                {
                    source.Stop();
                    source.volume = 0f;
                }
            }
        }

        static void Fade(AudioSource source, float volume, float pitch)
        {
            if (source == null || source.clip == null) return;
            if (volume > 0.001f && !source.isPlaying)
                source.Play();
            source.volume = Mathf.MoveTowards(source.volume, volume, Time.deltaTime * 2.5f);
            source.pitch = Mathf.MoveTowards(source.pitch, pitch, Time.deltaTime * 1.5f);
            if (volume <= 0f && source.volume <= 0.001f && source.isPlaying)
                source.Stop();
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip != null && m_OneShots != null)
                m_OneShots.PlayOneShot(clip, volume * m_InteriorVolume);
        }

        AudioSource Source(string name, AudioClip clip, Vector3 localPosition, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0f;
            // Mostly 3D so sounds come from the right place in the headset, but never fading out inside the car.
            source.spatialBlend = 0.75f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 2f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
