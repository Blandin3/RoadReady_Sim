using System.Collections.Generic;
using NUnit.Framework;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Scoring;
using UnityEngine;
using static RoadReady.Tests.TestData;

namespace RoadReady.Tests
{
    /// <summary>Composite safety score: component weights, violation penalties, caps, grade bands and reaction-time normalisation.</summary>
    public class ScoreCalculatorTests
    {
        ScoringConfig m_Config;

        [SetUp]
        public void SetUp() => m_Config = ScriptableObject.CreateInstance<ScoringConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Config);

        [Test]
        public void CleanRunWithNoHazards_ScoresOnRulesOnly_AndIsRoadReady()
        {
            var score = ScoreCalculator.Compute(NoHazards, NoDecisions, m_Config);

            Assert.AreEqual(100f, score.ruleComponent);
            Assert.AreEqual(100f, score.total);
            Assert.AreEqual("Road Ready", score.grade);
            Assert.AreEqual(-1f, score.meanReactionTime, "no responses -> mean reaction time is -1");
        }

        [Test]
        public void ViolationPenalties_AreTakenOffTheRuleComponent()
        {
            var decisions = new List<DecisionRecord>
            {
                Rule("speed_limit", DecisionClass.Borderline, ViolationSeverity.Minor), // -10
                Rule("stop_line", DecisionClass.Unsafe, ViolationSeverity.Major),       // -25
            };

            var score = ScoreCalculator.Compute(NoHazards, decisions, m_Config);

            Assert.AreEqual(65f, score.ruleComponent);
            Assert.AreEqual(65f, score.total);
            Assert.AreEqual(2, score.violationCount);
            Assert.AreEqual(1, score.minorViolations);
            Assert.AreEqual(1, score.majorViolations);
            Assert.AreEqual("Needs practice", score.grade);
        }

        [Test]
        public void SafeRuleDecisions_AreNotPenalised()
        {
            var decisions = new List<DecisionRecord> { Rule("turn_signal", DecisionClass.Safe, ViolationSeverity.Minor) };

            var score = ScoreCalculator.Compute(NoHazards, decisions, m_Config);

            Assert.AreEqual(100f, score.total);
            Assert.AreEqual(0, score.violationCount);
            Assert.AreEqual(1, score.safeDecisions);
        }

        [Test]
        public void RuleComponent_NeverGoesBelowZero()
        {
            var decisions = new List<DecisionRecord>
            {
                Rule("red_light", DecisionClass.Unsafe, ViolationSeverity.Critical),
                Rule("red_light", DecisionClass.Unsafe, ViolationSeverity.Critical),
                Rule("red_light", DecisionClass.Unsafe, ViolationSeverity.Critical),
            };

            var score = ScoreCalculator.Compute(NoHazards, decisions, m_Config);

            Assert.AreEqual(0f, score.ruleComponent);
            Assert.AreEqual(0f, score.total);
            Assert.AreEqual("Unsafe - try again", score.grade);
        }

        [Test]
        public void WeightedTotal_CombinesDetectionReactionAndRules()
        {
            // One hazard handled with a 1.875 s reaction (normalised 0.5), one missed:
            // detection 50, reaction 50, rules 100 -> 0.45*50 + 0.25*50 + 0.30*100 = 65.
            var hazards = new List<PerformanceRecord>
            {
                Hazard(DecisionClass.Safe, responded: true, reactionTime: 1.875f),
                Hazard(DecisionClass.Unsafe, responded: false, reactionTime: -1f),
            };

            var score = ScoreCalculator.Compute(hazards, NoDecisions, m_Config);

            Assert.AreEqual(2, score.hazardsTotal);
            Assert.AreEqual(1, score.hazardsDetected);
            Assert.AreEqual(0.5f, score.detectionRate, 1e-4f);
            Assert.AreEqual(50f, score.detectionComponent, 1e-3f);
            Assert.AreEqual(50f, score.reactionComponent, 1e-3f);
            Assert.AreEqual(1.875f, score.meanReactionTime, 1e-4f, "missed hazard is left out of the mean");
            Assert.AreEqual(65f, score.total);
        }

        [Test]
        public void Weights_AreNormalised_SoScalingThemDoesNotChangeTheScore()
        {
            var hazards = new List<PerformanceRecord>
            {
                Hazard(DecisionClass.Safe, true, 1.2f),
                Hazard(DecisionClass.Unsafe, false, -1f),
            };
            var decisions = new List<DecisionRecord> { Rule("speed_limit", DecisionClass.Borderline, ViolationSeverity.Minor) };
            var baseline = ScoreCalculator.Compute(hazards, decisions, m_Config).total;

            m_Config.hazardDetectionWeight *= 2f;
            m_Config.reactionTimeWeight *= 2f;
            m_Config.ruleComplianceWeight *= 2f;

            Assert.AreEqual(baseline, ScoreCalculator.Compute(hazards, decisions, m_Config).total);
        }

        [Test]
        public void HazardCollision_CapsTheScore()
        {
            var hazards = new List<PerformanceRecord> { Hazard(DecisionClass.Safe, true, 0.5f, collided: true) };

            var score = ScoreCalculator.Compute(hazards, NoDecisions, m_Config);

            Assert.IsTrue(score.collision);
            Assert.AreEqual(m_Config.collisionScoreCap, score.total);
        }

        [Test]
        public void CollisionRuleViolation_CapsTheScore()
        {
            var decisions = new List<DecisionRecord> { Rule("collision", DecisionClass.Unsafe, ViolationSeverity.Critical) };

            var score = ScoreCalculator.Compute(NoHazards, decisions, m_Config);

            Assert.IsTrue(score.collision);
            Assert.LessOrEqual(score.total, m_Config.collisionScoreCap);
        }

        [Test]
        public void RunningOutOfTime_CapsTheScore()
        {
            var decisions = new List<DecisionRecord> { Rule(ScoreCalculator.IncompleteRuleId, DecisionClass.Safe, ViolationSeverity.None) };

            var score = ScoreCalculator.Compute(NoHazards, decisions, m_Config);

            Assert.AreEqual(m_Config.incompleteScoreCap, score.total);
        }

        [TestCase(0.75f, 1f)]   // best reaction time
        [TestCase(0.30f, 1f)]   // faster than best is clamped
        [TestCase(1.875f, 0.5f)] // half way
        [TestCase(3.0f, 0f)]    // worst reaction time
        [TestCase(5.0f, 0f)]    // slower than worst is clamped
        public void NormaliseReaction_MapsBestToOneAndWorstToZero(float seconds, float expected)
        {
            Assert.AreEqual(expected, ScoreCalculator.NormaliseReaction(seconds, m_Config), 1e-4f);
        }

        [TestCase(100f, "Road Ready")]
        [TestCase(85f, "Road Ready")]
        [TestCase(84f, "Almost there")]
        [TestCase(70f, "Almost there")]
        [TestCase(69f, "Needs practice")]
        [TestCase(50f, "Needs practice")]
        [TestCase(49f, "Unsafe - try again")]
        public void GradeBands_UseTheConfiguredThresholds(float total, string expected)
        {
            Assert.AreEqual(expected, m_Config.GetGrade(total));
        }
    }
}
