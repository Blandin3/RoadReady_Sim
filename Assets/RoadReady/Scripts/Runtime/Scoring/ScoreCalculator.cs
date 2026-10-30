using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using UnityEngine;

namespace RoadReady.Scoring
{
    /// <summary>
    /// Composite safety score 0-100 (proposal "Safety score" definition and 3.2.4):
    ///   detection component  = hazards responded to appropriately / hazards presented
    ///   reaction component   = mean normalised reaction time over responded hazards
    ///   rule component       = 100 - violation penalties (minor / major / critical)
    /// Weights are normalised over the components that apply (e.g. no hazards fired -> rules only).
    /// A collision caps the total.
    /// </summary>
    public static class ScoreCalculator
    {
        /// <summary>Decision added when a goal-based attempt runs out of time; caps the score like a collision does.</summary>
        public const string IncompleteRuleId = "task_incomplete";

        public static SafetyScore Compute(IReadOnlyList<PerformanceRecord> hazards, IReadOnlyList<DecisionRecord> decisions, ScoringConfig config, ScenarioDefinition scenario = null)
        {
            var score = new SafetyScore { hazardsTotal = hazards.Count };

            // Hazard detection and reaction time.
            var rtSum = 0f;
            var rtScoreSum = 0f;
            var rtCount = 0;
            foreach (var h in hazards)
            {
                if (h.decision != DecisionClass.Unsafe)
                    score.hazardsDetected++;
                if (h.reactionTime >= 0f && h.responded)
                {
                    rtSum += h.reactionTime;
                    rtScoreSum += NormaliseReaction(h.reactionTime, config);
                    rtCount++;
                }

                switch (h.saOutcome)
                {
                    case SituationAwarenessOutcome.PerceptionFailure: score.perceptionFailures++; break;
                    case SituationAwarenessOutcome.ComprehensionFailure: score.comprehensionFailures++; break;
                    case SituationAwarenessOutcome.ProjectionFailure: score.projectionFailures++; break;
                }

                if (h.collided)
                    score.collision = true;
            }

            score.detectionRate = hazards.Count > 0 ? (float)score.hazardsDetected / hazards.Count : 0f;
            score.detectionComponent = score.detectionRate * 100f;
            score.meanReactionTime = rtCount > 0 ? rtSum / rtCount : -1f;
            score.reactionComponent = rtCount > 0 ? rtScoreSum / rtCount * 100f : 0f;

            // Rule compliance.
            var penalty = 0f;
            foreach (var d in decisions)
            {
                switch (d.decision)
                {
                    case DecisionClass.Safe: score.safeDecisions++; break;
                    case DecisionClass.Borderline: score.borderlineDecisions++; break;
                    default: score.unsafeDecisions++; break;
                }

                if (!d.IsViolation)
                    continue;
                score.violationCount++;
                switch (d.severity)
                {
                    case ViolationSeverity.Minor:
                        score.minorViolations++;
                        penalty += config.minorViolationPenalty;
                        break;
                    case ViolationSeverity.Major:
                        score.majorViolations++;
                        penalty += config.majorViolationPenalty;
                        break;
                    case ViolationSeverity.Critical:
                        score.criticalViolations++;
                        penalty += config.criticalViolationPenalty;
                        score.collision |= d.ruleId == "collision";
                        break;
                }
            }

            score.ruleComponent = Mathf.Max(0f, 100f - penalty);

            // Weighted total over applicable components.
            float weighted = 0f, weights = 0f;
            if (hazards.Count > 0)
            {
                weighted += config.hazardDetectionWeight * score.detectionComponent + config.reactionTimeWeight * score.reactionComponent;
                weights += config.hazardDetectionWeight + config.reactionTimeWeight;
            }

            weighted += config.ruleComplianceWeight * score.ruleComponent;
            weights += config.ruleComplianceWeight;

            score.total = weights > 0f ? weighted / weights : 0f;
            if (score.collision)
                score.total = Mathf.Min(score.total, config.collisionScoreCap);
            foreach (var d in decisions)
                if (d.ruleId == IncompleteRuleId)
                    score.total = Mathf.Min(score.total, config.incompleteScoreCap);
            score.total = Mathf.Round(Mathf.Clamp(score.total, 0f, 100f));
            score.grade = config.GetGrade(score.total);
            if (scenario != null && scenario.scoringMode == ScoringMode.PointsDeduction)
                ApplyPointsDeduction(score, hazards, decisions, config, scenario);
            return score;
        }

        /// <summary>
        /// Driving-test scoring (reverse bay parking): start at 100 and take off each fault's points. A late or missing
        /// brake reaction to the hazard costs points; any collision (e.g. the pedestrian) scores 0. Not finishing
        /// (time limit) is capped like other goal scenarios. Pass mark from the scenario (RNP test yard: 80).
        /// </summary>
        static void ApplyPointsDeduction(SafetyScore score, IReadOnlyList<PerformanceRecord> hazards, IReadOnlyList<DecisionRecord> decisions, ScoringConfig config, ScenarioDefinition scenario)
        {
            var total = 100f;
            foreach (var d in decisions)
                if (d.IsViolation)
                    total -= d.points > 0f ? d.points : d.severity == ViolationSeverity.Critical ? 100f : config.minorViolationPenalty;
            foreach (var h in hazards)
                if (!h.collided && (!h.responded || h.reactionTime < 0f || h.reactionTime > config.parkingLateReactionSeconds))
                    total -= config.parkingLateReactionPoints;

            var incomplete = false;
            foreach (var d in decisions)
                incomplete |= d.ruleId == IncompleteRuleId;
            if (score.collision)
                total = 0f;
            else if (incomplete)
                total = Mathf.Min(total, config.incompleteScoreCap);

            score.ruleComponent = Mathf.Clamp(total, 0f, 100f);
            score.total = Mathf.Round(score.ruleComponent);
            var passed = !score.collision && !incomplete && score.total >= scenario.passMark;
            score.grade = passed ? $"Passed (RNP pass mark {scenario.passMark})" : "Not passed yet";
        }

        public static float NormaliseReaction(float reactionTime, ScoringConfig config) =>
            Mathf.Clamp01((config.worstReactionSeconds - reactionTime) / Mathf.Max(0.01f, config.worstReactionSeconds - config.bestReactionSeconds));
    }
}
