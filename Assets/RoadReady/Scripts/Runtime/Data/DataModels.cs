using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;

namespace RoadReady.Data
{
    // Mapping to the proposal ERD (Figure 5):
    //   Participant        -> ParticipantRecord   (anonymised: no name / email is stored, see NFR data privacy)
    //   Training Session   -> AttemptRecord       (one scenario attempt)
    //   Performance Record -> PerformanceRecord   (one per scripted hazard)
    //   Safety Score       -> SafetyScore         (embedded in the attempt)
    //   Scenario / Hazard  -> ScenarioDefinition / HazardDefinition assets (ids are stored on records)
    // SessionRecord groups the attempts of one sitting in the headset; the SSQ/SUS are attached to it.
    // Time fields use -1 for "did not happen" because JsonUtility has no nullable support.

    [Serializable]
    public class ParticipantRecord
    {
        public string participantId;
        public string createdUtc;
        public Perspective group;
        public StudyCondition condition;
        public AgeBand ageBand;
        public DrivingExperience drivingExperience;
        public bool consentGiven;
        public string consentUtc;
        public bool guardianConsentVerified;
        public bool driverTutorialDone;
        public bool pedestrianTutorialDone;
        public int sessionCount;
        public List<LevelProgress> levels = new List<LevelProgress>();

        public bool IsTutorialDone(Perspective perspective) =>
            perspective == Perspective.Driver ? driverTutorialDone : pedestrianTutorialDone;

        public void SetTutorialDone(Perspective perspective)
        {
            if (perspective == Perspective.Driver) driverTutorialDone = true;
            else pedestrianTutorialDone = true;
        }

        public int GetUnlockedLevel(string scenarioId, Perspective perspective)
        {
            var progress = levels.Find(l => l.scenarioId == scenarioId && l.perspective == perspective);
            return progress != null ? progress.unlockedLevel : 1;
        }

        public void SetUnlockedLevel(string scenarioId, Perspective perspective, int level)
        {
            var progress = levels.Find(l => l.scenarioId == scenarioId && l.perspective == perspective);
            if (progress == null)
            {
                progress = new LevelProgress { scenarioId = scenarioId, perspective = perspective };
                levels.Add(progress);
            }

            progress.unlockedLevel = Math.Max(progress.unlockedLevel, level);
        }
    }

    [Serializable]
    public class LevelProgress
    {
        public string scenarioId;
        public Perspective perspective;
        public int unlockedLevel = 1;
    }

    [Serializable]
    public class SessionRecord
    {
        public string sessionId;
        public string participantId;
        public int sessionNumber;
        public bool isPostTestSession;
        public string startUtc;
        public string endUtc;
        public string appVersion;
        public string deviceModel;
        public List<string> attemptIds = new List<string>();
        public QuestionnaireResult ssq;
        public QuestionnaireResult sus;
        public bool hasSsq;
        public bool hasSus;
    }

    [Serializable]
    public class AttemptRecord
    {
        public string attemptId;
        public string sessionId;
        public string participantId;
        public string scenarioId;
        public string scenarioName;
        public ScenarioType scenarioType;
        public Perspective perspective;
        public int difficultyLevel = 1;
        public AttemptPhase phase;
        public StudyCondition condition;
        /// <summary>1-based count of scored attempts for this participant + scenario + perspective.</summary>
        public int attemptNumber;
        public string startUtc;
        public string endUtc;
        public float durationSeconds;
        public AttemptEndReason endReason;
        public bool completed;
        public int randomSeed;
        public ScenarioParameters parameters;
        public List<PerformanceRecord> hazardRecords = new List<PerformanceRecord>();
        public List<DecisionRecord> decisions = new List<DecisionRecord>();
        public SafetyScore score = new SafetyScore();
        public string appVersion;
    }

    [Serializable]
    public class PerformanceRecord
    {
        public string recordId;
        public string hazardId;
        public string hazardName;
        public HazardType hazardType;
        public RequiredResponse requiredResponse;
        public int severity;
        public float onsetTime = -1f;
        public float fixationTime = -1f;
        public float spottedPressTime = -1f;
        public float responseTime = -1f;
        /// <summary>responseTime - onsetTime, or -1 when no appropriate response was made.</summary>
        public float reactionTime = -1f;
        public float minTimeToCollision = 99f;
        public bool perceived;
        public bool responded;
        public bool collided;
        public DecisionClass decision;
        public SituationAwarenessOutcome saOutcome;
        public string feedback;
        public string otherPerspectiveInsight;
    }

    [Serializable]
    public class DecisionRecord
    {
        public string decisionId;
        public DecisionSource source;
        /// <summary>Rule id (e.g. "speed_limit") or hazard id.</summary>
        public string ruleId;
        public string title;
        public string detail;
        public DecisionClass decision;
        public ViolationSeverity severity;
        public float time;
        public float posX;
        public float posZ;
        public float speedKmh;
        public string tip;

        public bool IsViolation => source == DecisionSource.Rule && decision != DecisionClass.Safe;
    }

    [Serializable]
    public class SafetyScore
    {
        public float total;
        public string grade;
        public int hazardsTotal;
        public int hazardsDetected;
        /// <summary>0-1: hazards responded to appropriately / hazards presented.</summary>
        public float detectionRate;
        public float detectionComponent;
        /// <summary>Mean seconds over hazards with a response; -1 if none.</summary>
        public float meanReactionTime = -1f;
        public float reactionComponent;
        public int violationCount;
        public int minorViolations;
        public int majorViolations;
        public int criticalViolations;
        public float ruleComponent;
        public int safeDecisions;
        public int borderlineDecisions;
        public int unsafeDecisions;
        public bool collision;
        public int perceptionFailures;
        public int comprehensionFailures;
        public int projectionFailures;
    }

    [Serializable]
    public class QuestionnaireResult
    {
        public QuestionnaireType type;
        public string sessionId;
        public string completedUtc;
        public int[] answers;
        /// <summary>SSQ subscales (weighted). Zero for SUS.</summary>
        public float nausea;
        public float oculomotor;
        public float disorientation;
        /// <summary>SSQ total severity or SUS score (0-100).</summary>
        public float total;
    }

    /// <summary>One line of the per-attempt NDJSON trajectory log.</summary>
    [Serializable]
    public struct TelemetrySample
    {
        public string kind;
        public float t;
        public float x;
        public float z;
        public float yaw;
        public float headYaw;
        public float headPitch;
        public float speedKmh;
        public float throttle;
        public float brake;
        public float steer;
        public string zone;
    }

    [Serializable]
    public struct TelemetryEvent
    {
        public string kind;
        public float t;
        public string id;
        public string detail;
    }
}
