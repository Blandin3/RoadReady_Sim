using NUnit.Framework;
using RoadReady.Core;
using RoadReady.Data;

namespace RoadReady.Tests
{
    /// <summary>SSQ (Kennedy et al., 1993) and SUS (Brooke, 1996) scoring against the published formulas.</summary>
    public class QuestionnaireTests
    {
        static int[] Filled(int length, int value)
        {
            var answers = new int[length];
            for (var i = 0; i < length; i++)
                answers[i] = value;
            return answers;
        }

        [Test]
        public void Definitions_HaveTheStandardItemCounts()
        {
            Assert.AreEqual(16, Questionnaires.SSQ.items.Length);
            Assert.AreEqual(4, Questionnaires.SSQ.scaleLabels.Length);
            Assert.AreEqual(0, Questionnaires.SSQ.scaleMin);
            Assert.AreEqual(10, Questionnaires.SUS.items.Length);
            Assert.AreEqual(5, Questionnaires.SUS.scaleLabels.Length);
            Assert.AreEqual(1, Questionnaires.SUS.scaleMin);
            Assert.AreSame(Questionnaires.SSQ, Questionnaires.Get(QuestionnaireType.SSQ));
            Assert.AreSame(Questionnaires.SUS, Questionnaires.Get(QuestionnaireType.SUS));
        }

        [Test]
        public void Ssq_NoSymptoms_ScoresZero()
        {
            var r = Questionnaires.Score(QuestionnaireType.SSQ, Filled(16, 0), "S1");

            Assert.AreEqual(0f, r.nausea);
            Assert.AreEqual(0f, r.oculomotor);
            Assert.AreEqual(0f, r.disorientation);
            Assert.AreEqual(0f, r.total);
        }

        [Test]
        public void Ssq_AllSlight_MatchesTheWeightedSubscales()
        {
            // Each subscale has 7 items: N = 7 * 9.54, O = 7 * 7.58, D = 7 * 13.92, total = 21 * 3.74.
            var r = Questionnaires.Score(QuestionnaireType.SSQ, Filled(16, 1), "S1");

            Assert.AreEqual(66.78f, r.nausea, 0.01f);
            Assert.AreEqual(53.06f, r.oculomotor, 0.01f);
            Assert.AreEqual(97.44f, r.disorientation, 0.01f);
            Assert.AreEqual(78.54f, r.total, 0.01f);
        }

        [Test]
        public void Ssq_AllSevere_GivesTheMaximumTotal()
        {
            var r = Questionnaires.Score(QuestionnaireType.SSQ, Filled(16, 3), "S1");

            Assert.AreEqual(235.62f, r.total, 0.01f);
        }

        [Test]
        public void Ssq_ItemsCountTowardsTheirSubscalesOnly()
        {
            // Nausea (item 8) belongs to the Nausea and Disorientation subscales, not Oculomotor.
            var answers = Filled(16, 0);
            answers[7] = 3;

            var r = Questionnaires.Score(QuestionnaireType.SSQ, answers, "S1");

            Assert.AreEqual(3 * 9.54f, r.nausea, 0.01f);
            Assert.AreEqual(0f, r.oculomotor);
            Assert.AreEqual(3 * 13.92f, r.disorientation, 0.01f);
            Assert.AreEqual(6 * 3.74f, r.total, 0.01f);
        }

        [Test]
        public void Sus_AllNeutral_Scores50()
        {
            Assert.AreEqual(50f, Questionnaires.Score(QuestionnaireType.SUS, Filled(10, 3), "S1").total);
        }

        [Test]
        public void Sus_BestAnswers_Score100_AndWorstScore0()
        {
            // Odd-numbered statements are positive, even-numbered are negative.
            var best = new[] { 5, 1, 5, 1, 5, 1, 5, 1, 5, 1 };
            var worst = new[] { 1, 5, 1, 5, 1, 5, 1, 5, 1, 5 };

            Assert.AreEqual(100f, Questionnaires.Score(QuestionnaireType.SUS, best, "S1").total);
            Assert.AreEqual(0f, Questionnaires.Score(QuestionnaireType.SUS, worst, "S1").total);
        }

        [Test]
        public void Sus_AgreeWithPositivesAndDisagreeWithNegatives_Scores75()
        {
            var answers = new[] { 4, 2, 4, 2, 4, 2, 4, 2, 4, 2 };

            Assert.AreEqual(75f, Questionnaires.Score(QuestionnaireType.SUS, answers, "S1").total);
        }

        [Test]
        public void Score_KeepsACopyOfTheAnswers_AndTheSession()
        {
            var answers = Filled(10, 3);

            var r = Questionnaires.Score(QuestionnaireType.SUS, answers, "RR-TEST-S01");
            answers[0] = 5;

            Assert.AreEqual(3, r.answers[0], "later edits to the input must not change the stored result");
            Assert.AreEqual("RR-TEST-S01", r.sessionId);
            Assert.AreEqual(QuestionnaireType.SUS, r.type);
            Assert.IsFalse(string.IsNullOrEmpty(r.completedUtc));
        }
    }
}
