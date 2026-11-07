using System;
using NUnit.Framework;

namespace RoadReady.Reports.Tests
{
    /// <summary>Total marks, the session tracker (skip when empty, no double report) and the email text (including hostile names).</summary>
    public class ReportTests
    {
        static readonly DateTime k_Start = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

        // A tracker with a fixed clock (starts at 09:00, moves on 10 minutes every time it is asked) and predictable ids.
        static SessionTracker NewTracker()
        {
            var calls = 0;
            var ids = 0;
            return new SessionTracker(() => k_Start.AddMinutes(10 * calls++), () => "session-" + ++ids);
        }

        static SessionReport Report(params (string id, string name, string role, float score)[] attempts)
        {
            var tracker = NewTracker();
            tracker.Begin("member-1", "school-1", "Kigali Driving School", "Amina Uwase");
            foreach (var a in attempts)
                tracker.RecordAttempt(a.id, a.name, a.role, a.score, 60f);
            return tracker.End();
        }

        // ------------------------------------------------------------ total marks

        [Test]
        public void TotalMarks_IsTheAverageOfTheBestScorePerScenarioAndRole()
        {
            var report = Report(
                ("zebra", "Market zebra crossing", "driver", 60f),
                ("zebra", "Market zebra crossing", "driver", 80f),      // best driver zebra = 80 (not 60, not the mean 70)
                ("zebra", "Market zebra crossing", "pedestrian", 50f),  // a different role counts separately
                ("parking", "Reverse bay parking", "", 90f));

            Assert.AreEqual(73, report.TotalMarks, "(80 + 50 + 90) / 3 = 73.33 -> 73");
        }

        [Test]
        public void TotalMarks_RoundsHalvesUp_AndImprovingNeverLowersIt()
        {
            Assert.AreEqual(71, Report(("a", "A", "", 70f), ("b", "B", "", 71f)).TotalMarks, "70.5 -> 71");
            Assert.AreEqual(90, Report(("a", "A", "", 90f), ("a", "A", "", 40f)).TotalMarks, "a worse retry keeps the best");
        }

        [Test]
        public void Scores_AreWholeNumbersBetween0And100()
        {
            var report = Report(("a", "A", "", 104.6f), ("b", "B", "", -5f), ("c", "C", "", 49.5f));

            CollectionAssert.AreEqual(new[] { 100, 0, 50 }, new[] { report.attempts[0].score, report.attempts[1].score, report.attempts[2].score });
        }

        // ------------------------------------------------------------ tracker

        [Test]
        public void NoScoredAttempts_MeansNothingToSend()
        {
            var tracker = NewTracker();
            tracker.Begin("member-1", "school-1", "School", "Amina");

            Assert.IsNull(tracker.End());
            Assert.IsFalse(tracker.IsActive, "the session is closed even when nothing is sent");
        }

        [Test]
        public void EndingWithoutAnySession_GivesNothing()
        {
            Assert.IsNull(NewTracker().End());
        }

        [Test]
        public void EndingTwice_NeverGivesTheSameReportTwice()
        {
            var tracker = NewTracker();
            tracker.Begin("member-1", "school-1", "School", "Amina");
            tracker.RecordAttempt("a", "A", "", 80f, 30f);

            var first = tracker.End();
            var second = tracker.End(); // a double tap on Done

            Assert.IsNotNull(first);
            Assert.IsNull(second);
        }

        [Test]
        public void EachSessionGetsItsOwnId_AndAttemptsGoToTheRightSession()
        {
            var tracker = NewTracker();
            tracker.Begin("member-1", "school-1", "School", "Amina");
            tracker.RecordAttempt("a", "A", "", 80f, 30f);
            var first = tracker.End();
            tracker.Begin("member-2", "school-1", "School", "Eric");
            tracker.RecordAttempt("a", "A", "", 55f, 30f);
            var second = tracker.End();

            Assert.AreNotEqual(first.sessionId, second.sessionId);
            Assert.AreEqual(80, first.TotalMarks);
            Assert.AreEqual(55, second.TotalMarks);
            Assert.AreEqual("Eric", second.learnerName);
        }

        [Test]
        public void AttemptsRecordedWithNoSession_AreIgnored()
        {
            var tracker = NewTracker();

            tracker.RecordAttempt("a", "A", "", 80f, 30f);
            tracker.Begin("member-1", "school-1", "School", "Amina");

            Assert.IsNull(tracker.End(), "the early attempt did not leak into the new session");
        }

        [Test]
        public void IsFor_TellsLearnersApart_AndSessionTimesAreSet()
        {
            var tracker = NewTracker();
            tracker.Begin("member-1", "school-1", "School", "Amina");
            tracker.RecordAttempt("a", "A", "", 80f, 30f);

            Assert.IsTrue(tracker.IsFor("member-1"));
            Assert.IsFalse(tracker.IsFor("member-2"));
            var report = tracker.End();
            Assert.Greater(report.DurationSeconds, 0.0);
            Assert.IsFalse(tracker.IsFor("member-1"));
        }

        [Test]
        public void TheReportCarriesNoParticipantCode()
        {
            var report = Report(("a", "A", "", 80f));
            var json = UnityEngine.JsonUtility.ToJson(report);

            StringAssert.DoesNotContain("member-1", json);
        }

        // ------------------------------------------------------------ email text

        [Test]
        public void Subject_HasTheNameAndTheMarks()
        {
            var report = Report(("a", "A", "", 82f));

            Assert.AreEqual("RoadReady session report: Amina Uwase - 82", ReportBuilder.Subject(report));
        }

        [Test]
        public void Email_ListsEverythingTheSchoolNeeds()
        {
            var report = Report(
                ("zebra", "Market zebra crossing", "driver", 60f),
                ("zebra", "Market zebra crossing", "driver", 80f),
                ("parking", "Reverse bay parking", "", 90f));

            var email = ReportBuilder.Build(report);

            foreach (var body in new[] { email.text, email.html })
            {
                StringAssert.Contains("Kigali Driving School", body);
                StringAssert.Contains("Amina Uwase", body);
                StringAssert.Contains("Total marks: 85", body.Replace("<b>", "").Replace("</b>", ""));
                StringAssert.Contains("Overall score: average of best attempt per scenario", body);
                StringAssert.Contains("Market zebra crossing (driver)", body);
                StringAssert.Contains("Reverse bay parking", body);
                StringAssert.DoesNotContain("Reverse bay parking (", body, "a one-role scenario shows only its name");
                StringAssert.Contains("Simulated training result sent by RoadReady.", body);
                StringAssert.Contains("2026-11-03", body);
            }

            // Every scored attempt is listed, not just the best one.
            StringAssert.Contains("60 / 100", email.text);
            StringAssert.Contains("80 / 100", email.text);
            StringAssert.Contains("<table", email.html);
        }

        [TestCase("<script>alert(1)</script>")]
        [TestCase("\"><img src=x onerror=alert(1)>")]
        [TestCase("'; DROP TABLE members; --")]
        [TestCase("A & B <b>bold</b>")]
        public void HostileNames_AreShownAsTextInTheHtml(string hostile)
        {
            var tracker = NewTracker();
            tracker.Begin("m", "s", "Evil <i>School</i>", hostile);
            tracker.RecordAttempt("a", "<u>Scenario</u>", "driver", 70f, 10f);

            var html = ReportBuilder.Html(tracker.End());

            StringAssert.DoesNotContain("<script", html);
            StringAssert.DoesNotContain("<img", html);
            StringAssert.DoesNotContain("<b>bold", html);
            StringAssert.DoesNotContain("<i>School", html);
            StringAssert.DoesNotContain("<u>Scenario", html);
        }

        [Test]
        public void HostileName_IsEncodedExactly()
        {
            var html = ReportBuilder.Html(Report("<script>alert(1)</script>"));

            StringAssert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }

        [Test]
        public void LineBreaksInANameCannotAddMailHeaders()
        {
            var tracker = NewTracker();
            tracker.Begin("m", "s", "School", "Amina\r\nBcc: attacker@example.com");
            tracker.RecordAttempt("a", "A", "", 70f, 10f);
            var report = tracker.End();

            var subject = ReportBuilder.Subject(report);

            StringAssert.DoesNotContain("\r", subject);
            StringAssert.DoesNotContain("\n", subject);
            foreach (var line in ReportBuilder.PlainText(report).Split('\n'))
                StringAssert.DoesNotStartWith("Bcc:", line.Trim());
        }

        [Test]
        public void VeryLongAndEmptyNames_AreHandled()
        {
            Assert.LessOrEqual(ReportBuilder.Clean(new string('x', 5000)).Length, 80);
            Assert.AreEqual("(no name)", ReportBuilder.Clean("   "));
            Assert.AreEqual("(no name)", ReportBuilder.Clean(null));
        }

        // ------------------------------------------------------------ the mail document (must match the Firestore rules)

        [Test]
        public void MailDocument_HasExactlyTheKeysTheRulesAllow_AndOnlyTheSchoolAsRecipient()
        {
            var doc = FirestoreMailSender.BuildDocument(Report(("a", "A", "", 80f)), "school@example.com");

            CollectionAssert.AreEquivalent(new[] { "to", "message" }, doc.Keys);
            var to = (System.Collections.Generic.List<object>)doc["to"];
            CollectionAssert.AreEqual(new object[] { "school@example.com" }, to);
            var message = (System.Collections.Generic.Dictionary<string, object>)doc["message"];
            CollectionAssert.AreEquivalent(new[] { "subject", "text", "html" }, message.Keys);
            StringAssert.StartsWith("RoadReady session report: Amina Uwase", (string)message["subject"]);
        }

        [Test]
        public void MailDocument_StaysWithinTheRulesSizeLimits()
        {
            var tracker = NewTracker();
            tracker.Begin("m", "s", new string('S', 5000), new string('N', 5000));
            for (var i = 0; i < 60; i++)
                tracker.RecordAttempt("s" + i, new string('X', 5000), "driver", 50f, 10f);

            var message = (System.Collections.Generic.Dictionary<string, object>)FirestoreMailSender.BuildDocument(tracker.End(), "a@b.c")["message"];

            Assert.LessOrEqual(((string)message["subject"]).Length, 200);
            Assert.LessOrEqual(((string)message["text"]).Length, 20000);
            Assert.LessOrEqual(((string)message["html"]).Length, 50000);
        }

        // ------------------------------------------------------------ the Apps Script message

        [Test]
        public void RelayPayload_HasTheFieldsTheScriptNeeds_AndNoPersonalCode()
        {
            var report = Report(("zebra", "Market zebra crossing", "driver", 80f), ("parking", "Reverse bay parking", "", 90f));

            var json = AppsScriptSender.BuildPayloadJson(report, "school@example.com", "the-secret");

            StringAssert.Contains("\"secret\":\"the-secret\"", json);
            StringAssert.Contains("\"to\":\"school@example.com\"", json);
            StringAssert.Contains("\"sessionId\":\"" + report.sessionId + "\"", json);
            StringAssert.Contains("\"learnerName\":\"Amina Uwase\"", json);
            StringAssert.Contains("\"totalMarks\":85", json);
            StringAssert.Contains("Market zebra crossing (driver)", json);
            StringAssert.DoesNotContain("member-1", json);
        }

        [Test]
        public void RelayPayload_HostileNamesLoseTheirLineBreaks()
        {
            var tracker = NewTracker();
            tracker.Begin("m", "s", "School", "Amina\r\nBcc: attacker@example.com");
            tracker.RecordAttempt("a", "A", "", 70f, 10f);

            var json = AppsScriptSender.BuildPayloadJson(tracker.End(), "school@example.com", "x");

            StringAssert.DoesNotContain("\\r", json);
            StringAssert.DoesNotContain("\\n", json);
        }

        // Helper: a report whose learner has the given name, with one attempt.
        static SessionReport Report(string learnerName)
        {
            var tracker = NewTracker();
            tracker.Begin("m", "s", "School", learnerName);
            tracker.RecordAttempt("a", "A", "", 70f, 10f);
            return tracker.End();
        }
    }
}
