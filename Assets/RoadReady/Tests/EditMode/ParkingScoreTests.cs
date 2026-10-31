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
    /// <summary>Reverse bay parking, scored like the RNP test yard: start at 100, points off per fault, pass at 80.</summary>
    public class ParkingScoreTests
    {
        ScoringConfig m_Config;
        ScenarioDefinition m_Scenario;

        [SetUp]
        public void SetUp()
        {
            m_Config = ScriptableObject.CreateInstance<ScoringConfig>();
            m_Scenario = ScriptableObject.CreateInstance<ScenarioDefinition>();
            m_Scenario.scoringMode = ScoringMode.PointsDeduction;
            m_Scenario.passMark = 80;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Config);
            Object.DestroyImmediate(m_Scenario);
        }

        static List<PerformanceRecord> PedestrianBrakedFor(float reactionTime) =>
            new List<PerformanceRecord> { Hazard(DecisionClass.Safe, responded: true, reactionTime: reactionTime) };

        SafetyScore Score(List<PerformanceRecord> hazards, List<DecisionRecord> decisions) =>
            ScoreCalculator.Compute(hazards, decisions, m_Config, m_Scenario);

        [Test]
        public void FaultFreePark_Scores100_AndPasses()
        {
            var score = Score(PedestrianBrakedFor(1.0f), NoDecisions);

            Assert.AreEqual(100f, score.total);
            Assert.AreEqual("Passed (RNP pass mark 80)", score.grade);
        }

        [Test]
        public void ExactlyThePassMark_Passes()
        {
            var decisions = new List<DecisionRecord>
            {
                Rule("parking_observation", DecisionClass.Unsafe, ViolationSeverity.Major, m_Config.parkingNoObservationPoints), // -15
                Rule("parking_line", DecisionClass.Borderline, ViolationSeverity.Minor, m_Config.parkingLineOrConePoints),       // -5
            };

            var score = Score(PedestrianBrakedFor(1.0f), decisions);

            Assert.AreEqual(80f, score.total);
            StringAssert.StartsWith("Passed", score.grade);
        }

        [Test]
        public void BelowThePassMark_DoesNotPass()
        {
            var decisions = new List<DecisionRecord>
            {
                Rule("parking_observation", DecisionClass.Unsafe, ViolationSeverity.Major, m_Config.parkingNoObservationPoints), // -15
                Rule("parking_kerb", DecisionClass.Unsafe, ViolationSeverity.Minor, m_Config.parkingKerbPoints),                 // -10
            };

            var score = Score(PedestrianBrakedFor(1.0f), decisions);

            Assert.AreEqual(75f, score.total);
            Assert.AreEqual("Not passed yet", score.grade);
        }

        [Test]
        public void LateBrakeForThePedestrian_CostsReactionPoints()
        {
            var score = Score(PedestrianBrakedFor(m_Config.parkingLateReactionSeconds + 0.5f), NoDecisions);

            Assert.AreEqual(100f - m_Config.parkingLateReactionPoints, score.total);
        }

        [Test]
        public void NoBrakeForThePedestrian_CostsReactionPoints()
        {
            var hazards = new List<PerformanceRecord> { Hazard(DecisionClass.Unsafe, responded: false, reactionTime: -1f) };

            var score = Score(hazards, NoDecisions);

            Assert.AreEqual(100f - m_Config.parkingLateReactionPoints, score.total);
        }

        [Test]
        public void HittingThePedestrian_ScoresZero_AndFails()
        {
            var hazards = new List<PerformanceRecord> { Hazard(DecisionClass.Unsafe, responded: false, reactionTime: -1f, collided: true) };

            var score = Score(hazards, NoDecisions);

            Assert.AreEqual(0f, score.total);
            Assert.AreEqual("Not passed yet", score.grade);
        }

        [Test]
        public void NotFinishingInTime_IsCappedAndFails_EvenWithNoFaults()
        {
            var decisions = new List<DecisionRecord> { Rule(ScoreCalculator.IncompleteRuleId, DecisionClass.Safe, ViolationSeverity.None) };

            var score = Score(PedestrianBrakedFor(1.0f), decisions);

            Assert.AreEqual(m_Config.incompleteScoreCap, score.total);
            Assert.AreEqual("Not passed yet", score.grade);
        }

        [Test]
        public void CriticalFaultWithoutPoints_TakesAllHundred()
        {
            var decisions = new List<DecisionRecord> { Rule("parking_critical", DecisionClass.Unsafe, ViolationSeverity.Critical) };

            var score = Score(PedestrianBrakedFor(1.0f), decisions);

            Assert.AreEqual(0f, score.total);
        }

        [Test]
        public void ScoreNeverGoesBelowZero()
        {
            var decisions = new List<DecisionRecord>();
            for (var i = 0; i < 12; i++)
                decisions.Add(Rule("parking_cone", DecisionClass.Borderline, ViolationSeverity.Minor, 10f));

            var score = Score(PedestrianBrakedFor(1.0f), decisions);

            Assert.AreEqual(0f, score.total);
        }
    }
}
