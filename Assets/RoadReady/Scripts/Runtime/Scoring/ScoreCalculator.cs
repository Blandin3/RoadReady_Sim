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
        public static SafetyScore Compute(IReadOnlyList<PerformanceRecord> hazards, IReadOnlyList<DecisionRecord> decisions, ScoringConfig config)
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
            score.total = Mathf.Round(Mathf.Clamp(score.total, 0f, 100f));
            score.grade = config.GetGrade(score.total);
            return score;
        }

        public static float NormaliseReaction(float reactionTime, ScoringConfig config) =>
            Mathf.Clamp01((config.worstReactionSeconds - reactionTime) / Mathf.Max(0.01f, config.worstReactionSeconds - config.bestReactionSeconds));
    }
}
