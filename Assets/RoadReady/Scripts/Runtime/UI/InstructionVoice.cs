using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.UI
{
    /// <summary>Plays the voice clip for an instruction key (FR12) when one is assigned and voice is enabled.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class InstructionVoice : MonoBehaviour
    {
        AudioSource m_Source;
        RoadReadyConfig m_Config;
        ComfortSettings m_Comfort;
        string m_LastKey;

        public void Initialize(RoadReadyConfig config, ComfortSettings comfort)
        {
            m_Config = config;
            m_Comfort = comfort;
            m_Source = GetComponent<AudioSource>();
            m_Source.spatialBlend = 0f;
            m_Source.playOnAwake = false;
            RoadReadyEvents.InstructionRequested += Play;
        }

        public void ApplyComfort(ComfortSettings comfort) => m_Comfort = comfort;

        void OnDestroy() => RoadReadyEvents.InstructionRequested -= Play;

        public void Replay()
        {
            if (m_LastKey != null)
                Play(m_LastKey);
        }

        void Play(string key)
        {
            m_LastKey = key;
            if (m_Config == null || m_Comfort == null || !m_Comfort.voiceInstructions)
                return;
            var clip = m_Config.FindVoice(key);
            if (clip == null)
                return;
            m_Source.Stop();
            m_Source.clip = clip;
            m_Source.Play();
        }
    }
}
