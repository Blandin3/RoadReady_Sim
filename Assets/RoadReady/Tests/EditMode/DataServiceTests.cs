using System;
using System.IO;
using NUnit.Framework;
using RoadReady.Core;
using RoadReady.Data;
using static RoadReady.Tests.TestData;

namespace RoadReady.Tests
{
    /// <summary>Local study data: records survive a save / reload (a fresh DataService on the same folder), and withdrawal removes them.</summary>
    public class DataServiceTests
    {
        string m_Root;

        [SetUp]
        public void SetUp() => m_Root = Path.Combine(Path.GetTempPath(), "RoadReadyTests_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root))
                Directory.Delete(m_Root, true);
        }

        ParticipantRecord NewParticipant(DataService data) =>
            data.CreateParticipant(Perspective.Driver, StudyCondition.RoadReadyTraining, default, DrivingExperience.Learner, guardianConsentVerified: true);

        static AttemptRecord Attempt(ParticipantRecord p, string id, string startUtc, AttemptPhase phase = AttemptPhase.Training, AttemptEndReason end = AttemptEndReason.GoalReached) =>
            new AttemptRecord
            {
                attemptId = id,
                participantId = p.participantId,
                scenarioId = "zebra_crossing",
                perspective = Perspective.Driver,
                phase = phase,
                endReason = end,
                startUtc = startUtc,
            };

        [Test]
        public void ParticipantIds_AreAnonymousCodes()
        {
            var p = NewParticipant(new DataService(m_Root));

            StringAssert.IsMatch("^RR-[A-HJ-NP-Z2-9]{6}$", p.participantId);
        }

        [Test]
        public void Participant_SurvivesReload()
        {
            var data = new DataService(m_Root);
            var p = NewParticipant(data);
            data.RecordConsent(p, true);
            p.SetTutorialDone(Perspective.Driver);
            p.SetUnlockedLevel("zebra_crossing", Perspective.Driver, 2);
            data.SaveParticipant(p);

            var loaded = new DataService(m_Root).LoadParticipant(p.participantId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(p.participantId, loaded.participantId);
            Assert.AreEqual(StudyCondition.RoadReadyTraining, loaded.condition);
            Assert.AreEqual(DrivingExperience.Learner, loaded.drivingExperience);
            Assert.IsTrue(loaded.consentGiven);
            Assert.IsTrue(loaded.guardianConsentVerified);
            Assert.IsTrue(loaded.IsTutorialDone(Perspective.Driver));
            Assert.IsFalse(loaded.IsTutorialDone(Perspective.Pedestrian));
            Assert.AreEqual(2, loaded.GetUnlockedLevel("zebra_crossing", Perspective.Driver));
            CollectionAssert.Contains(new DataService(m_Root).ListParticipantIds(), p.participantId);
        }

        [Test]
        public void UnlockedLevel_NeverGoesBackDown()
        {
            var p = new ParticipantRecord();
            p.SetUnlockedLevel("zebra_crossing", Perspective.Driver, 3);
            p.SetUnlockedLevel("zebra_crossing", Perspective.Driver, 1);

            Assert.AreEqual(3, p.GetUnlockedLevel("zebra_crossing", Perspective.Driver));
            Assert.AreEqual(1, p.GetUnlockedLevel("zebra_crossing", Perspective.Pedestrian), "levels are tracked per perspective");
        }

        [Test]
        public void Attempt_WithScoreHazardsAndDecisions_SurvivesReload()
        {
            var data = new DataService(m_Root);
            var p = NewParticipant(data);
            var attempt = Attempt(p, "A1", "2026-10-31T10:00:00Z");
            attempt.hazardRecords.Add(Hazard(DecisionClass.Safe, true, 1.2f));
            attempt.decisions.Add(Rule("speed_limit", DecisionClass.Borderline, ViolationSeverity.Minor));
            attempt.score.total = 82f;
            attempt.score.grade = "Almost there";
            data.SaveAttempt(attempt);

            var reloaded = new DataService(m_Root).GetAttempts(p.participantId);

            Assert.AreEqual(1, reloaded.Count);
            var a = reloaded[0];
            Assert.AreEqual("A1", a.attemptId);
            Assert.AreEqual(82f, a.score.total);
            Assert.AreEqual("Almost there", a.score.grade);
            Assert.AreEqual(1, a.hazardRecords.Count);
            Assert.AreEqual(1.2f, a.hazardRecords[0].reactionTime, 1e-4f);
            Assert.AreEqual(1, a.decisions.Count);
            Assert.AreEqual("speed_limit", a.decisions[0].ruleId);
            Assert.AreEqual(ViolationSeverity.Minor, a.decisions[0].severity);
        }

        [Test]
        public void SavingAnAttemptAgain_ReplacesIt_InsteadOfDuplicating()
        {
            var data = new DataService(m_Root);
            var p = NewParticipant(data);
            var attempt = Attempt(p, "A1", "2026-10-31T10:00:00Z");
            data.SaveAttempt(attempt);
            attempt.score.total = 90f;
            data.SaveAttempt(attempt);

            Assert.AreEqual(1, data.GetAttempts(p.participantId).Count);
            var reloaded = new DataService(m_Root).GetAttempts(p.participantId);
            Assert.AreEqual(1, reloaded.Count);
            Assert.AreEqual(90f, reloaded[0].score.total);
        }

        [Test]
        public void ScoredAttempts_LeaveOutTutorialsAndAbortedRuns_OldestFirst()
        {
            var data = new DataService(m_Root);
            var p = NewParticipant(data);
            data.SaveAttempt(Attempt(p, "late", "2026-10-31T12:00:00Z", AttemptPhase.PostTest));
            data.SaveAttempt(Attempt(p, "tutorial", "2026-10-31T09:00:00Z", AttemptPhase.Tutorial, AttemptEndReason.TutorialComplete));
            data.SaveAttempt(Attempt(p, "aborted", "2026-10-31T10:30:00Z", end: AttemptEndReason.Aborted));
            data.SaveAttempt(Attempt(p, "early", "2026-10-31T10:00:00Z", AttemptPhase.PreTest));

            var scored = new DataService(m_Root).GetScoredAttempts(p.participantId, "zebra_crossing", Perspective.Driver);

            Assert.AreEqual(2, scored.Count);
            Assert.AreEqual("early", scored[0].attemptId);
            Assert.AreEqual("late", scored[1].attemptId);
        }

        [Test]
        public void Saving_LeavesNoTempFilesBehind_AndRaisesRecordSaved()
        {
            var data = new DataService(m_Root);
            string collection = null;
            data.RecordSaved += (c, id, json) => collection = c;

            NewParticipant(data);

            Assert.AreEqual("participants", collection);
            Assert.IsEmpty(Directory.GetFiles(m_Root, "*.tmp", SearchOption.AllDirectories));
        }

        [Test]
        public void WithdrawingAParticipant_DeletesAllTheirData()
        {
            var data = new DataService(m_Root);
            var p = NewParticipant(data);
            data.SaveAttempt(Attempt(p, "A1", "2026-10-31T10:00:00Z"));

            data.DeleteParticipant(p.participantId);

            Assert.IsNull(data.CurrentParticipant);
            Assert.IsNull(new DataService(m_Root).LoadParticipant(p.participantId));
            Assert.IsEmpty(new DataService(m_Root).GetAttempts(p.participantId));
            CollectionAssert.DoesNotContain(new DataService(m_Root).ListParticipantIds(), p.participantId);
        }
    }
}
