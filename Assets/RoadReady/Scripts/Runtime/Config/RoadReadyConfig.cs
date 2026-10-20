using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Config
{
    public enum RemoteBackend
    {
        None,
        /// <summary>Firebase Realtime Database REST API: PUT {baseUrl}/{collection}/{id}.json?auth={token}</summary>
        FirebaseRealtimeDatabase,
        /// <summary>Generic JSON POST to {baseUrl}/{collection} (e.g. a small PostgreSQL / Supabase REST API).</summary>
        JsonPost
    }

    /// <summary>Root configuration asset referenced by the bootstrap scene.</summary>
    [CreateAssetMenu(menuName = "RoadReady/RoadReady Config", fileName = "RoadReadyConfig")]
    public class RoadReadyConfig : ScriptableObject
    {
        [Header("Scenarios")]
        public ScenarioDefinition driverTutorial;
        public ScenarioDefinition pedestrianTutorial;
        public List<ScenarioDefinition> scenarios = new List<ScenarioDefinition>();

        [Header("Scoring")]
        public ScoringConfig scoring;

        [Header("Comfort defaults")]
        public ComfortSettings defaultComfort = new ComfortSettings();

        [Header("Branding")]
        [Tooltip("RoadReady logo shown on the splash and welcome screens. Leave empty to show the 'RR' badge instead.")]
        public Texture2D logo;
        [Tooltip("How long the logo splash stays on screen before the welcome page (seconds).")]
        [Range(1f, 10f)] public float splashSeconds = 3f;
        [Tooltip("Unlit material with the lobby background picture; it fills the whole view behind the menus. Leave empty for the plain lobby.")]
        public Material lobbyBackdrop;
        [Tooltip("Plain colour behind the menus while no scenario is loaded (splash, welcome, setup, menus). Scenarios keep their own sky.")]
        public Color lobbyBackgroundColor = new Color(0.055f, 0.165f, 0.32f);

        [Header("Study")]
        public string studyName = "RoadReady Kimironko Pilot";
        [Tooltip("Researcher PIN for the admin panel. Change before the pilot.")]
        public string adminPin = "2026";
        [Tooltip("Show the SSQ after every session (NFR comfort target).")]
        public bool askSsqAfterEverySession = true;
        [Tooltip("SSQ total score at which the researcher is warned to stop the session.")]
        public float ssqWarningTotalScore = 40f;
        [Tooltip("Seconds of the unscored tutorial before it can be skipped.")]
        public float tutorialSkipDelaySeconds = 3f;

        [Header("Data")]
        public RemoteBackend remoteBackend = RemoteBackend.None;
        [Tooltip("e.g. https://your-project-default-rtdb.firebaseio.com")]
        public string remoteBaseUrl = "";
        [Tooltip("Database secret / auth token. Leave empty for open test databases only.")]
        public string remoteAuthToken = "";
        [Tooltip("Telemetry sampling rate (Hz) for the per-attempt trajectory log.")]
        [Range(1f, 30f)] public float telemetrySampleRate = 10f;

        [Header("Voice instructions (FR12). Keys are listed in InstructionLibrary.")]
        public List<VoiceLine> voiceLines = new List<VoiceLine>();

        public ScenarioDefinition GetTutorial(Perspective perspective) =>
            perspective == Perspective.Driver ? driverTutorial : pedestrianTutorial;

        public ScenarioDefinition FindScenario(string scenarioId)
        {
            if (driverTutorial != null && driverTutorial.scenarioId == scenarioId) return driverTutorial;
            if (pedestrianTutorial != null && pedestrianTutorial.scenarioId == scenarioId) return pedestrianTutorial;
            return scenarios.Find(s => s != null && s.scenarioId == scenarioId);
        }

        public AudioClip FindVoice(string key)
        {
            var line = voiceLines.Find(v => v.key == key);
            return line.clip;
        }
    }

    [System.Serializable]
    public struct VoiceLine
    {
        public string key;
        public AudioClip clip;
    }
}
