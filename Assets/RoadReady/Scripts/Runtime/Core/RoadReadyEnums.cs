using System;

namespace RoadReady.Core
{
    /// <summary>The road-user role the learner occupies during an attempt.</summary>
    public enum Perspective
    {
        Driver,
        Pedestrian
    }

    [Flags]
    public enum PerspectiveMask
    {
        None = 0,
        Driver = 1 << 0,
        Pedestrian = 1 << 1,
        Both = Driver | Pedestrian
    }

    /// <summary>The curated scenario set from proposal section 1.5, plus the unscored tutorial (FR13).</summary>
    public enum ScenarioType
    {
        Tutorial,
        UnsignalizedZebraCrossing,
        SignalizedIntersection,
        UnsignalizedJunction
    }

    public enum RoadUserKind
    {
        Car,
        MotoTaxi,
        Bus,
        Truck,
        Bicycle,
        Pedestrian
    }

    public enum HazardType
    {
        PedestrianStepsOut,
        PedestrianOnCrossing,
        VehicleFailsToYield,
        MotoTaxiWeaving,
        MotoTaxiSuddenStop,
        RedLightRunner,
        VehicleFromBlindSpot,
        JaywalkingPedestrian,
        CyclistSwerve,
        TurningVehicle,
        ObscuredVehicle,
        Custom
    }

    /// <summary>What counts as an "appropriate corrective action" for a hazard.</summary>
    public enum RequiredResponse
    {
        /// <summary>Driver: press the brake / decelerate.</summary>
        Brake,
        /// <summary>Driver: come to a stop before a line / point and stay there until the hazard clears.</summary>
        StopBeforePoint,
        /// <summary>Driver: steer or brake to avoid.</summary>
        AvoidOrBrake,
        /// <summary>Pedestrian: do not enter the carriageway while the hazard is active.</summary>
        WaitAtKerb,
        /// <summary>Pedestrian: already crossing - stop or step back out of the vehicle's path.</summary>
        StopOrRetreat,
        /// <summary>Either role: visually check the hazard (head gaze) within the window.</summary>
        LookTowards
    }

    /// <summary>Classification the rule-based evaluation engine assigns to every decision (proposal 3.2.3).</summary>
    public enum DecisionClass
    {
        Safe,
        Borderline,
        Unsafe
    }

    public enum ViolationSeverity
    {
        None = 0,
        Minor = 1,
        Major = 2,
        Critical = 3
    }

    /// <summary>Endsley (1995) three-level situation awareness failure classification.</summary>
    public enum SituationAwarenessOutcome
    {
        NoFailure,
        /// <summary>Level 1: the hazard was never looked at / acknowledged.</summary>
        PerceptionFailure,
        /// <summary>Level 2: the hazard was looked at but no appropriate response followed.</summary>
        ComprehensionFailure,
        /// <summary>Level 3: the hazard was perceived and responded to, but too late / with too little margin.</summary>
        ProjectionFailure
    }

    public enum DecisionSource
    {
        Hazard,
        Rule
    }

    /// <summary>Where an attempt sits in the single-group pre/post design (proposal 3.2.2).</summary>
    public enum AttemptPhase
    {
        Tutorial,
        PreTest,
        Training,
        PostTest
    }

    public enum StudyCondition
    {
        RoadReadyTraining,
        BriefingOnly
    }

    public enum AgeBand
    {
        Age16To17,
        Age18To24,
        Age25To34,
        Age35Plus
    }

    public enum DrivingExperience
    {
        None,
        Learner,
        LicensedUnder2Years,
        Licensed2YearsPlus
    }

    public enum AttemptEndReason
    {
        GoalReached,
        TimeLimit,
        Collision,
        Aborted,
        TutorialComplete
    }

    public enum AppState
    {
        Boot,
        ParticipantSetup,
        Consent,
        Excluded,
        Instructions,
        MainMenu,
        Briefing,
        Loading,
        InScenario,
        Paused,
        Feedback,
        Questionnaire,
        Debrief,
        Admin
    }

    public enum TurnSide
    {
        None,
        Left,
        Right
    }

    public enum QuestionnaireType
    {
        SSQ,
        SUS
    }

    public static class PerspectiveExtensions
    {
        public static PerspectiveMask ToMask(this Perspective perspective)
        {
            return perspective == Perspective.Driver ? PerspectiveMask.Driver : PerspectiveMask.Pedestrian;
        }

        public static bool Includes(this PerspectiveMask mask, Perspective perspective)
        {
            return (mask & perspective.ToMask()) != 0;
        }

        public static Perspective Other(this Perspective perspective)
        {
            return perspective == Perspective.Driver ? Perspective.Pedestrian : Perspective.Driver;
        }

        public static bool IsVehicle(this RoadUserKind kind)
        {
            return kind != RoadUserKind.Pedestrian;
        }
    }
}
