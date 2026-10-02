using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadReady.Config
{
    /// <summary>
    /// Administrator-configurable scenario parameters (FR11). One instance per difficulty level on each
    /// <see cref="ScenarioDefinition"/>; runtime overrides are persisted as JSON by <see cref="ScenarioOverrideStore"/>.
    /// </summary>
    [Serializable]
    public class ScenarioParameters
    {
        [Tooltip("Seed for traffic spawning and compliance rolls. Same seed = identical traffic every attempt.")]
        public int randomSeed = 1001;

        [Header("Traffic")]
        [Tooltip("Background vehicles spawned per minute on each spawner.")]
        [Range(0f, 40f)] public float trafficDensityPerMinute = 10f;
        [Range(0f, 1f)] public float motoTaxiShare = 0.35f;
        [Range(0f, 1f)] public float busShare = 0.1f;
        [Range(0f, 1f)] public float bicycleShare = 0.05f;
        [Range(10f, 80f)] public float npcCruiseSpeedKmh = 35f;
        [Tooltip("Probability that a background driver yields to a pedestrian at an unsignalised zebra.")]
        [Range(0f, 1f)] public float driverYieldCompliance = 0.6f;
        [Tooltip("Probability that a background driver obeys a red signal.")]
        [Range(0f, 1f)] public float signalCompliance = 0.95f;
        [Tooltip("Background pedestrians spawned per minute on each pedestrian spawner.")]
        [Range(0f, 30f)] public float pedestrianDensityPerMinute = 6f;
        [Tooltip("Probability that a background pedestrian jaywalks instead of using the crossing.")]
        [Range(0f, 1f)] public float pedestrianJaywalkRate = 0.1f;

        [Header("Road rules")]
        [Range(20f, 80f)] public float speedLimitKmh = 40f;
        [Tooltip("Multiplies all signal phase durations.")]
        [Range(0.5f, 2f)] public float signalCycleScale = 1f;

        [Header("Attempt")]
        [Range(30f, 600f)] public float timeLimitSeconds = 180f;
        public bool endAttemptOnCollision = true;
        [Tooltip("Scales the timing of scripted hazards (lower = less warning time).")]
        [Range(0.5f, 1.5f)] public float hazardLeadTimeScale = 1f;
        public List<string> disabledHazardIds = new List<string>();

        [Header("Environment (used once final assets are in)")]
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        public Weather weather = Weather.Clear;

        public ScenarioParameters Clone()
        {
            var clone = (ScenarioParameters)MemberwiseClone();
            clone.disabledHazardIds = new List<string>(disabledHazardIds);
            return clone;
        }

        public bool IsHazardEnabled(string hazardId) => !disabledHazardIds.Contains(hazardId);
    }

    public enum TimeOfDay
    {
        Day,
        Dusk,
        Night
    }

    public enum Weather
    {
        Clear,
        Rain
    }
}
