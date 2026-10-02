using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Config
{
    /// <summary>
    /// Designer-authored description of a scripted hazard. The scene-side <c>HazardTrigger</c> decides
    /// when it fires and which actors perform it; this asset decides how the response is judged.
    /// </summary>
    [CreateAssetMenu(menuName = "RoadReady/Hazard Definition", fileName = "Hazard_")]
    public class HazardDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier written to telemetry. Do not change after data collection starts.")]
        public string hazardId = "hazard_id";
        public string displayName = "Hazard";
        public HazardType type = HazardType.Custom;
        [TextArea] public string description;
        [Range(1, 3)] public int severity = 2;

        [Header("Evaluation")]
        [Tooltip("Which perspective this hazard is scored for.")]
        public Perspective perspective = Perspective.Driver;
        public RequiredResponse requiredResponse = RequiredResponse.Brake;
        [Tooltip("Seconds after onset in which a response counts.")]
        [Min(0.5f)] public float responseWindowSeconds = 4f;
        [Tooltip("Reaction time at or below this is Safe.")]
        [Min(0.1f)] public float safeReactionSeconds = 1.5f;
        [Tooltip("Reaction time at or below this is Borderline; slower is Unsafe.")]
        [Min(0.1f)] public float borderlineReactionSeconds = 2.5f;
        [Tooltip("Half-angle of the head-gaze cone used to decide the learner looked at the hazard.")]
        [Range(5f, 45f)] public float gazeConeDegrees = 18f;

        [Header("Feedback")]
        [TextArea] public string feedbackSafe = "Well done - you spotted it early and responded safely.";
        [TextArea] public string feedbackBorderline = "You responded, but late. Scan further ahead so you have more time.";
        [TextArea] public string feedbackUnsafe = "This could have caused a crash. Look for this situation earlier next time.";
        [Tooltip("Shared-responsibility insight shown in the report: what the other road user was seeing.")]
        [TextArea] public string otherPerspectiveInsight;
    }
}
