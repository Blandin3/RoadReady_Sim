using System.Collections.Generic;
using System.Linq;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;

namespace RoadReady.Scoring
{
    public class FeedbackLine
    {
        public string title;
        public string detail;
        public DecisionClass decision;
        public bool isHazard;
        public string situationAwareness;
    }

    /// <summary>Everything the feedback report screen shows (FR8/FR9). Rebuilt from the attempt record.</summary>
    public class FeedbackReport
    {
        public string scenarioName;
        public Perspective perspective;
        public AttemptPhase phase;
        public int level;
        public string headline;
        public string grade;
        public SafetyScore score;
        public AttemptEndReason endReason;
        public List<FeedbackLine> hazardLines = new List<FeedbackLine>();
        public List<FeedbackLine> ruleLines = new List<FeedbackLine>();
        public List<string> tips = new List<string>();
        public string awarenessInsight;
        public string otherPerspectiveInsight;
        public int attemptNumber;
        public float previousScore = -1f;
        public float bestScore = -1f;
        public List<float> history = new List<float>();
        public bool unlockedNextLevel;
        public bool isTutorial;
    }

    public static class FeedbackGenerator
    {
        public static FeedbackReport Build(AttemptRecord attempt, ScenarioDefinition scenario, IReadOnlyList<AttemptRecord> previousAttempts, bool unlockedNextLevel)
        {
            var s = attempt.score;
            var report = new FeedbackReport
            {
                scenarioName = scenario != null ? scenario.displayName : attempt.scenarioName,
                perspective = attempt.perspective,
                phase = attempt.phase,
                level = attempt.difficultyLevel,
                score = s,
                grade = s.grade,
                endReason = attempt.endReason,
                attemptNumber = attempt.attemptNumber,
                unlockedNextLevel = unlockedNextLevel,
                isTutorial = attempt.phase == AttemptPhase.Tutorial,
            };

            report.headline = attempt.endReason switch
            {
                AttemptEndReason.Collision => "Collision - this would have hurt someone",
                AttemptEndReason.TimeLimit => "Time ran out",
                AttemptEndReason.TutorialComplete => "Tutorial complete",
                AttemptEndReason.Aborted => "Attempt stopped",
                _ => s.total >= 85 ? "Excellent, safe decisions" : s.total >= 70 ? "Good - a few things to tighten up" : "Let's practise this again",
            };

            foreach (var h in attempt.hazardRecords)
            {
                report.hazardLines.Add(new FeedbackLine
                {
                    title = h.hazardName,
                    detail = h.feedback,
                    decision = h.decision,
                    isHazard = true,
                    situationAwareness = h.saOutcome switch
                    {
                        SituationAwarenessOutcome.PerceptionFailure => "Missed it (perception)",
                        SituationAwarenessOutcome.ComprehensionFailure => "Saw it, did not act (comprehension)",
                        SituationAwarenessOutcome.ProjectionFailure => "Acted too late (anticipation)",
                        _ => "Good awareness",
                    },
                });
            }

            foreach (var d in attempt.decisions.Where(d => d.source == DecisionSource.Rule))
                report.ruleLines.Add(new FeedbackLine { title = d.title, detail = d.detail, decision = d.decision });

            // Up to three distinct tips, worst decisions first.
            var tips = attempt.decisions
                .Where(d => d.decision != DecisionClass.Safe && !string.IsNullOrEmpty(d.tip))
                .OrderByDescending(d => d.decision).ThenByDescending(d => d.severity)
                .Select(d => d.tip).Distinct().Take(3);
            report.tips.AddRange(tips);

            report.awarenessInsight = BuildAwarenessInsight(s);
            report.otherPerspectiveInsight = attempt.hazardRecords
                .Where(h => !string.IsNullOrEmpty(h.otherPerspectiveInsight))
                .OrderByDescending(h => h.decision)
                .Select(h => h.otherPerspectiveInsight)
                .FirstOrDefault();

            if (previousAttempts != null)
            {
                var prior = previousAttempts.Where(a => a.attemptId != attempt.attemptId).ToList();
                if (prior.Count > 0)
                {
                    report.previousScore = prior[prior.Count - 1].score.total;
                    report.bestScore = prior.Max(a => a.score.total);
                }

                report.history.AddRange(prior.Skip(System.Math.Max(0, prior.Count - 7)).Select(a => a.score.total));
                report.history.Add(s.total);
            }

            return report;
        }

        static string BuildAwarenessInsight(SafetyScore s)
        {
            if (s.hazardsTotal == 0)
                return null;
            if (s.perceptionFailures >= s.comprehensionFailures && s.perceptionFailures >= s.projectionFailures && s.perceptionFailures > 0)
                return "Most mistakes happened because the danger was not noticed. Keep your eyes moving: far ahead, both sides, then near.";
            if (s.comprehensionFailures >= s.projectionFailures && s.comprehensionFailures > 0)
                return "You looked at the danger but did not act on it. Ask yourself: 'What could this person or vehicle do next?'";
            if (s.projectionFailures > 0)
                return "You reacted, but late. Try to predict what will happen a few seconds ahead and act before you have to.";
            return "Great situation awareness - you spotted every hazard and acted in time.";
        }
    }
}
