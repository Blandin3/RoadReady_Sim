using UnityEngine;

namespace RoadReady.Config
{
    /// <summary>
    /// All thresholds and weights of the deterministic rule-based evaluation engine (proposal 3.2.3).
    /// Keeping them in one asset makes the scoring model auditable and reportable in the thesis.
    /// </summary>
    [CreateAssetMenu(menuName = "RoadReady/Scoring Config", fileName = "ScoringConfig")]
    public class ScoringConfig : ScriptableObject
    {
        [Header("Composite safety score weights (normalised at runtime)")]
        [Range(0f, 1f)] public float hazardDetectionWeight = 0.45f;
        [Range(0f, 1f)] public float reactionTimeWeight = 0.25f;
        [Range(0f, 1f)] public float ruleComplianceWeight = 0.30f;

        [Header("Reaction time normalisation (seconds)")]
        [Tooltip("Reaction time scoring 100%.")]
        public float bestReactionSeconds = 0.75f;
        [Tooltip("Reaction time scoring 0%.")]
        public float worstReactionSeconds = 3.0f;

        [Header("Rule compliance penalties (points off 100)")]
        public float minorViolationPenalty = 10f;
        public float majorViolationPenalty = 25f;
        public float criticalViolationPenalty = 60f;
        [Tooltip("A collision caps the composite score at this value.")]
        public float collisionScoreCap = 40f;

        [Header("Grade bands")]
        public int roadReadyGrade = 85;
        public int almostThereGrade = 70;
        public int needsPracticeGrade = 50;

        [Header("Proximity (time-to-collision, seconds)")]
        [Tooltip("TTC below this is a near miss (Unsafe).")]
        public float criticalTimeToCollision = 1.0f;
        [Tooltip("TTC below this is a close call (Borderline).")]
        public float warningTimeToCollision = 2.0f;
        [Tooltip("Hazard minimum TTC at or above this keeps a response Safe.")]
        public float safeHazardTimeToCollision = 2.0f;

        [Header("Driver rules")]
        [Tooltip("km/h over the limit tolerated before an excursion starts.")]
        public float speedToleranceKmh = 3f;
        [Tooltip("Seconds over the limit before it counts as a violation.")]
        public float speedingGraceSeconds = 1.5f;
        [Tooltip("Overspeed ratio above which speeding is Unsafe rather than Borderline.")]
        public float unsafeSpeedingRatio = 1.2f;
        [Tooltip("Minimum gap (s) to a priority vehicle when entering a junction.")]
        public float giveWayGapSeconds = 3.0f;
        [Tooltip("Entering a give-way junction faster than this is Borderline.")]
        public float giveWayMaxEntrySpeedKmh = 20f;
        [Tooltip("Time headway below this while moving is 'following too closely'.")]
        public float minFollowingHeadwaySeconds = 1.0f;
        public float followingGraceSeconds = 2.0f;
        [Tooltip("Head yaw relative to the car needed to count as a blind-spot / mirror check.")]
        public float blindSpotCheckYawDegrees = 55f;
        [Tooltip("Heading change inside a junction zone that counts as a turn.")]
        public float turnDetectionDegrees = 45f;
        [Tooltip("How long the car may be outside drivable zones before it is a violation.")]
        public float offRoadGraceSeconds = 0.5f;

        [Header("Pedestrian rules")]
        [Tooltip("Assumed walking speed for required-gap computation (m/s).")]
        public float pedestrianWalkSpeed = 1.3f;
        [Tooltip("Extra seconds beyond the crossing time required for a Safe gap.")]
        public float safeGapMarginSeconds = 1.5f;
        [Tooltip("Head yaw either side of the crossing direction that counts as 'looked'.")]
        public float lookBothWaysYawDegrees = 45f;
        [Tooltip("Look-checks must happen within this many seconds before stepping out.")]
        public float lookBothWaysWindowSeconds = 5f;
        [Tooltip("Standing in the carriageway longer than crossing time + this is Borderline.")]
        public float roadwayDwellGraceSeconds = 4f;

        [Header("Gaze")]
        [Tooltip("Continuous seconds inside the gaze cone that count as a fixation.")]
        public float gazeFixationSeconds = 0.2f;

        public string GetGrade(float score)
        {
            if (score >= roadReadyGrade) return "Road Ready";
            if (score >= almostThereGrade) return "Almost there";
            if (score >= needsPracticeGrade) return "Needs practice";
            return "Unsafe - try again";
        }
    }
}
