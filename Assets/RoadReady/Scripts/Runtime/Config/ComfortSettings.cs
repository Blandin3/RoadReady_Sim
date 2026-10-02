using System;
using UnityEngine;

namespace RoadReady.Config
{
    public enum TurnMode
    {
        Snap,
        Smooth
    }

    /// <summary>NFR safety and comfort options, persisted per device.</summary>
    [Serializable]
    public class ComfortSettings
    {
        const string k_PrefsKey = "RoadReady.Comfort";

        [Tooltip("Tunnelling vignette while the vehicle accelerates / turns or the pedestrian moves.")]
        public bool vignetteEnabled = true;
        [Range(0.3f, 1f)] public float vignetteStrength = 0.7f;
        public TurnMode turnMode = TurnMode.Snap;
        [Range(15f, 90f)] public float snapTurnDegrees = 45f;
        [Tooltip("Pressing 'Cross now' walks the learner across automatically (reduces locomotion sickness).")]
        public bool assistedCrossing = true;
        [Tooltip("Allow thumbstick walking in the pedestrian module.")]
        public bool freeWalking = true;
        [Range(0.5f, 2f)] public float walkSpeed = 1.3f;
        [Tooltip("Seated play: pedestrian eye height is fixed rather than tracked.")]
        public bool seatedMode;
        [Range(1.2f, 1.9f)] public float seatedEyeHeight = 1.6f;
        [Tooltip("UI text scale multiplier (NFR accessibility).")]
        [Range(1f, 1.5f)] public float textScale = 1f;
        [Range(0f, 1f)] public float masterVolume = 1f;
        public bool voiceInstructions = true;

        public static ComfortSettings Load(ComfortSettings defaults)
        {
            var json = PlayerPrefs.GetString(k_PrefsKey, string.Empty);
            var settings = defaults != null ? defaults.Clone() : new ComfortSettings();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, settings);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoadReady] Could not read comfort settings: {e.Message}");
                }
            }

            return settings;
        }

        public void Save()
        {
            PlayerPrefs.SetString(k_PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public ComfortSettings Clone() => (ComfortSettings)MemberwiseClone();
    }
}
