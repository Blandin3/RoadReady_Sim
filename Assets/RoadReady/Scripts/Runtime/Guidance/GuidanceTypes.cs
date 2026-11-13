using System;
using System.Globalization;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Guidance
{
    /// <summary>How much the driving guide says. Set per study phase in the admin panel.</summary>
    public enum GuidanceMode
    {
        /// <summary>Warnings with a short explanation of what to do.</summary>
        Coaching,
        /// <summary>Short warnings only, no advice.</summary>
        WarningsOnly,
        /// <summary>No cues at all.</summary>
        Off
    }

    /// <summary>
    /// What the guide thinks the driver needs to hear right now. The order is the class number used by the
    /// machine-learning model later, so never reorder it.
    /// </summary>
    public enum GuidanceLabel
    {
        Safe = 0,
        SlowDown = 1,
        CrossingAhead = 2,
        StopAhead = 3,
        LaneDeparture = 4
    }

    /// <summary>
    /// Every number the guide looks at, measured at 10 Hz. A distance of "NoneDistance" (or NoneTtc / NoneEdge)
    /// means "nothing relevant" - a fixed number is used instead of infinity so a model can read it.
    /// </summary>
    public struct GuidanceFeatures
    {
        public const float NoneDistance = 100f;
        public const float NoneTtc = 30f;
        public const float NoneEdge = 3f;

        public float speedKmh;
        public float speedLimitKmh;
        /// <summary>Metres to the next stop line ahead that currently demands a stop (red/amber, zebra with pedestrians, give-way with traffic).</summary>
        public float stopLineDistance;
        /// <summary>Metres to the nearest crossing ahead; 0 while inside one.</summary>
        public float crossingDistance;
        /// <summary>Smallest time-to-collision (s) with something ahead in the car's lane.</summary>
        public float nearestTtc;
        /// <summary>Metres to the nearest pedestrian ahead.</summary>
        public float nearestPedestrianDistance;
        /// <summary>Sideways metres from the car's side to the nearest road edge (stepping off the drivable zones).</summary>
        public float edgeDistance;

        public float OverLimitKmh => speedKmh - speedLimitKmh;

        public const string CsvHeader = "speedKmh,speedLimitKmh,overLimitKmh,stopLineDistance,crossingDistance,nearestTtc,nearestPedestrianDistance,edgeDistance";

        /// <summary>The feature values as one CSV fragment (same column order as <see cref="CsvHeader"/>).</summary>
        public string ToCsv() => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###},{3:0.###},{4:0.###},{5:0.###},{6:0.###},{7:0.###}",
            speedKmh, speedLimitKmh, OverLimitKmh, stopLineDistance, crossingDistance, nearestTtc, nearestPedestrianDistance, edgeDistance);
    }

    /// <summary>
    /// Settings and thresholds of the driving guide. Everything the rules use is here so it can be audited and
    /// reported. Lives inside RoadReadyConfig (Inspector: "Guidance").
    /// </summary>
    [Serializable]
    public class GuidanceSettings
    {
        public const string TrainingPrefsKey = "RoadReady.Guidance.TrainingMode";
        public const string TestPrefsKey = "RoadReady.Guidance.TestMode";

        [Header("Mode (the admin panel can override these on a device)")]
        [Tooltip("Tutorial and training attempts.")]
        public GuidanceMode trainingMode = GuidanceMode.Coaching;
        [Tooltip("Baseline (pre-test) and post-test attempts.")]
        public GuidanceMode testMode = GuidanceMode.WarningsOnly;

        [Header("Rule thresholds (ground truth for the labels)")]
        [Tooltip("Speed above the limit by more than this (km/h) -> SlowDown.")]
        public float slowDownOverLimitKmh = 2f;
        [Tooltip("Time-to-collision with something ahead below this (s) -> SlowDown.")]
        public float warningTtcSeconds = 2f;
        [Tooltip("A pedestrian ahead closer than this (m) while faster than the speed below -> SlowDown.")]
        public float pedestrianSlowMetres = 12f;
        public float pedestrianSlowSpeedKmh = 20f;
        [Tooltip("A crossing closer than this (m) ahead -> CrossingAhead.")]
        public float crossingAheadMetres = 30f;
        [Tooltip("A stop line that demands a stop closer than this (m) ahead -> StopAhead.")]
        public float stopAheadMetres = 40f;
        [Tooltip("The car's side closer than this (m) to the road edge -> LaneDeparture (before it leaves the road).")]
        public float edgeWarningMetres = 0.6f;
        [Tooltip("Below this speed (km/h) the car counts as standing still and gets no cues.")]
        public float minMovingSpeedKmh = 5f;

        [Header("Timing")]
        [Range(5f, 10f)] public float evaluationsPerSecond = 10f;
        [Tooltip("A label must hold this long (s) before it is announced, so a flicker is ignored.")]
        public float confirmSeconds = 0.3f;
        [Tooltip("The same cue is not repeated within this many seconds.")]
        public float cueCooldownSeconds = 8f;
        [Tooltip("Minimum seconds between two different cues (one at a time). A more urgent cue may interrupt.")]
        public float minGapBetweenCuesSeconds = 3f;
        [Tooltip("No cues in the first seconds of an attempt.")]
        public float startDelaySeconds = 5f;

        [Header("Developer only")]
        [Tooltip("Write one CSV row per evaluation (features + rule label) for training data. Only works in the Editor and development builds.")]
        public bool logFeatureRows;

        /// <summary>The mode for an attempt phase: the admin override if one was saved on this device, else the asset value.</summary>
        public GuidanceMode ModeFor(AttemptPhase phase)
        {
            var isTest = phase == AttemptPhase.PreTest || phase == AttemptPhase.PostTest;
            var key = isTest ? TestPrefsKey : TrainingPrefsKey;
            if (PlayerPrefs.HasKey(key))
            {
                var saved = PlayerPrefs.GetInt(key);
                if (Enum.IsDefined(typeof(GuidanceMode), saved))
                    return (GuidanceMode)saved;
            }

            return isTest ? testMode : trainingMode;
        }

        /// <summary>Saves an admin choice on this device (does not change the asset).</summary>
        public static void SaveModeOverride(bool testPhases, GuidanceMode mode)
        {
            PlayerPrefs.SetInt(testPhases ? TestPrefsKey : TrainingPrefsKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static void ClearModeOverrides()
        {
            PlayerPrefs.DeleteKey(TrainingPrefsKey);
            PlayerPrefs.DeleteKey(TestPrefsKey);
        }
    }
}
