using System;

namespace RoadReady.Reports
{
    /// <summary>
    /// Keeps the running list of scored attempts for the learner who is training right now.
    /// Begin() when a learner is picked, RecordAttempt() for each finished scored attempt, End() when they press Done.
    /// It knows nothing about Unity or Firebase, so it can be tested on its own. The clock and the id maker are
    /// passed in so tests can control them.
    /// </summary>
    public class SessionTracker
    {
        readonly Func<DateTime> m_UtcNow;
        readonly Func<string> m_NewId;

        SessionReport m_Report;
        string m_MemberKey;

        public SessionTracker(Func<DateTime> utcNow = null, Func<string> newId = null)
        {
            m_UtcNow = utcNow ?? (() => DateTime.UtcNow);
            m_NewId = newId ?? (() => Guid.NewGuid().ToString("N"));
        }

        public bool IsActive => m_Report != null;

        /// <summary>True when the running session belongs to this learner (so a new learner starts a fresh one).</summary>
        public bool IsFor(string memberKey) => IsActive && m_MemberKey == memberKey;

        /// <summary>
        /// Starts a session for a learner. memberKey is only used to tell learners apart here: it is never put in the report.
        /// Starting a new session drops any session that was never ended (for example the headset was handed over without
        /// pressing Done); such a session is not reported.
        /// </summary>
        public void Begin(string memberKey, string schoolId, string schoolName, string learnerName)
        {
            m_MemberKey = memberKey;
            m_Report = new SessionReport
            {
                sessionId = m_NewId(),
                schoolId = schoolId,
                schoolName = schoolName,
                learnerName = learnerName,
                startUtc = m_UtcNow().ToString("o"),
            };
        }

        /// <summary>Adds one finished, scored attempt. Does nothing if no session is running.</summary>
        public void RecordAttempt(string scenarioId, string scenarioName, string perspective, float score, float durationSeconds)
        {
            if (m_Report == null)
                return;
            m_Report.attempts.Add(new ReportAttempt
            {
                scenarioId = scenarioId,
                scenarioName = scenarioName,
                perspective = perspective ?? "",
                score = (int)Math.Round(Math.Max(0f, Math.Min(100f, score)), MidpointRounding.AwayFromZero),
                finishedUtc = m_UtcNow().ToString("o"),
                durationSeconds = durationSeconds,
            });
        }

        /// <summary>
        /// Ends the session and hands back the finished report, or null when there is nothing to send (no session, or no
        /// scored attempts). The session is closed either way, so pressing Done twice can never produce a second report.
        /// </summary>
        public SessionReport End()
        {
            var report = m_Report;
            m_Report = null;
            m_MemberKey = null;
            if (report == null || !report.HasScores)
                return null;
            report.endUtc = m_UtcNow().ToString("o");
            return report;
        }
    }
}
