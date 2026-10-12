using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>
    /// 3D engine / tyre loop for traffic, rising in pitch and volume with speed. Hearing traffic approach is part of
    /// the pedestrian task, so every car, bus and moto-taxi carries one. Silent while the vehicle is hidden (dormant
    /// hazard actors must not give themselves away before they appear).
    /// </summary>
    [RequireComponent(typeof(TrafficVehicleAI))]
    public class VehicleEngineAudio : MonoBehaviour
    {
        [SerializeField] AudioClip m_Idle;
        [SerializeField] AudioClip m_Drive;
        [Tooltip("Pitch multiplier for this vehicle type (smaller engines sound higher).")]
        [SerializeField] float m_PitchScale = 1f;
        [Range(0f, 1f)] [SerializeField] float m_Volume = 0.7f;
        [SerializeField] float m_MaxDistance = 70f;

        TrafficVehicleAI m_Vehicle;
        AudioSource m_IdleSource, m_DriveSource;
        Renderer m_AnyRenderer;

        public void Configure(AudioClip idle, AudioClip drive, float pitchScale, float volume)
        {
            m_Idle = idle;
            m_Drive = drive;
            m_PitchScale = pitchScale;
            m_Volume = volume;
        }

        void Awake()
        {
            m_Vehicle = GetComponent<TrafficVehicleAI>();
            m_AnyRenderer = GetComponentInChildren<Renderer>();
            m_IdleSource = Source(m_Idle);
            m_DriveSource = Source(m_Drive);
        }

        void OnEnable()
        {
            // De-phase identical loops so a queue of cars does not sound like one.
            foreach (var s in new[] { m_IdleSource, m_DriveSource })
                if (s != null && s.clip != null)
                {
                    s.time = Random.Range(0f, s.clip.length * 0.9f);
                    s.Play();
                }
        }

        void Update()
        {
            var audible = m_AnyRenderer == null || m_AnyRenderer.enabled;
            var kmh = m_Vehicle.Speed * 3.6f;
            var moving = Mathf.InverseLerp(3f, 25f, kmh);
            var target = audible ? m_Volume : 0f;
            Set(m_IdleSource, target * (1f - moving), m_PitchScale);
            Set(m_DriveSource, target * Mathf.Lerp(0.15f, 1f, moving) * (kmh > 1f ? 1f : 0f), m_PitchScale * Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(5f, 50f, kmh)));
        }

        static void Set(AudioSource s, float volume, float pitch)
        {
            if (s == null) return;
            s.volume = Mathf.MoveTowards(s.volume, volume, Time.deltaTime * 2f);
            s.pitch = pitch;
        }

        AudioSource Source(AudioClip clip)
        {
            if (clip == null) return null;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = m_MaxDistance;
            source.dopplerLevel = 0.6f;
            return source;
        }
    }
}
