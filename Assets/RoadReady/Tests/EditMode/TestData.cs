using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Data;

namespace RoadReady.Tests
{
    /// <summary>Builders for the records the scoring and data tests feed in.</summary>
    static class TestData
    {
        public static PerformanceRecord Hazard(DecisionClass decision, bool responded, float reactionTime, bool collided = false) =>
            new PerformanceRecord
            {
                hazardId = "test_hazard",
                decision = decision,
                responded = responded,
                reactionTime = reactionTime,
                collided = collided,
            };

        public static DecisionRecord Rule(string ruleId, DecisionClass decision, ViolationSeverity severity, float points = 0f) =>
            new DecisionRecord
            {
                source = DecisionSource.Rule,
                ruleId = ruleId,
                decision = decision,
                severity = decision == DecisionClass.Safe ? ViolationSeverity.None : severity,
                points = points,
            };

        public static readonly List<PerformanceRecord> NoHazards = new List<PerformanceRecord>();
        public static readonly List<DecisionRecord> NoDecisions = new List<DecisionRecord>();
    }
}
