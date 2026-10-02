using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Config
{
    /// <summary>
    /// A trainable scenario (FR1/FR2). Adding a new scenario = new scene + new asset; the scoring engine
    /// needs no change (NFR scalability).
    /// </summary>
    [CreateAssetMenu(menuName = "RoadReady/Scenario Definition", fileName = "Scenario_")]
    public class ScenarioDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier written to telemetry. Do not change after data collection starts.")]
        public string scenarioId = "scenario_id";
        public string displayName = "Scenario";
        public ScenarioType type = ScenarioType.UnsignalizedZebraCrossing;
        [Tooltip("Real-world location the scenario is modelled on.")]
        public string location = "Kimironko, Kigali";
        [TextArea] public string description;
        public Sprite thumbnail;

        [Tooltip("Scene loaded additively for this scenario. Must be in Build Settings.")]
        public string sceneName;
        public PerspectiveMask supportedPerspectives = PerspectiveMask.Both;
        [Tooltip("Tutorials are never scored (FR13).")]
        public bool isTutorial;

        [Header("Briefing text shown before an attempt")]
        [TextArea] public string driverBriefing;
        [TextArea] public string pedestrianBriefing;

        [Header("Difficulty progression (level 1 is always used for pre/post tests)")]
        public List<ScenarioParameters> difficultyLevels = new List<ScenarioParameters> { new ScenarioParameters() };
        [Tooltip("Composite score needed to unlock the next difficulty level.")]
        [Range(0, 100)] public int unlockNextLevelScore = 80;

        [Header("Hazards scored in this scenario (scene triggers reference these)")]
        public List<HazardDefinition> hazards = new List<HazardDefinition>();

        public int LevelCount => Mathf.Max(1, difficultyLevels.Count);

        public string GetBriefing(Perspective perspective) =>
            perspective == Perspective.Driver ? driverBriefing : pedestrianBriefing;

        public ScenarioParameters GetDefaultParameters(int level)
        {
            if (difficultyLevels == null || difficultyLevels.Count == 0)
                return new ScenarioParameters();
            level = Mathf.Clamp(level, 1, difficultyLevels.Count);
            return difficultyLevels[level - 1];
        }
    }
}
