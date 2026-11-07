using System;
using System.Collections.Generic;

namespace RoadReady.Reports
{
    /// <summary>
    /// One scored attempt in the end-of-session report. Plain data (no Unity types) so it can be saved as JSON
    /// for the offline queue and tested without the game running.
    /// </summary>
    [Serializable]
    public class ReportAttempt
    {
        public string scenarioId;
        public string scenarioName;
        /// <summary>"driver" or "pedestrian". Empty for scenarios with only one role (e.g. parking), so the name stands alone.</summary>
        public string perspective;
        /// <summary>Whole number 0-100.</summary>
        public int score;
        public string finishedUtc;
        public float durationSeconds;

        /// <summary>What the learner sees in the table, e.g. "Market zebra crossing (driver)".</summary>
        public string Label => string.IsNullOrEmpty(perspective) ? scenarioName : $"{scenarioName} ({perspective})";

        /// <summary>Attempts of the same scenario in the same role are compared with each other.</summary>
        public string GroupKey => scenarioId + "|" + perspective;
    }

    /// <summary>
    /// Everything the email needs. It holds the learner's NAME and marks only: no participant code or other
    /// personal data. SessionId makes the report unique so it can never be sent twice.
    /// </summary>
    [Serializable]
    public class SessionReport
    {
        public string sessionId;
        /// <summary>The school's Firebase id; the offline queue only sends a report while the same school is signed in.</summary>
        public string schoolId;
        public string schoolName;
        public string learnerName;
        public string startUtc;
        public string endUtc;
        public List<ReportAttempt> attempts = new List<ReportAttempt>();

        public bool HasScores => attempts != null && attempts.Count > 0;

        public double DurationSeconds
        {
            get
            {
                var style = System.Globalization.DateTimeStyles.RoundtripKind;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                if (DateTime.TryParse(startUtc, culture, style, out var start) && DateTime.TryParse(endUtc, culture, style, out var end))
                    return Math.Max(0.0, (end - start).TotalSeconds);
                return 0.0;
            }
        }

        /// <summary>
        /// Total marks = the average of the BEST score per scenario and role, rounded to a whole number out of 100
        /// (halves round up). Retrying and improving is rewarded, and the result stays on the same 0-100 scale as the
        /// feedback screen. Zero when there are no scores.
        /// </summary>
        public int TotalMarks
        {
            get
            {
                if (!HasScores)
                    return 0;
                var best = new Dictionary<string, int>();
                foreach (var a in attempts)
                {
                    var key = a.GroupKey;
                    if (!best.TryGetValue(key, out var current) || a.score > current)
                        best[key] = a.score;
                }

                var sum = 0;
                foreach (var score in best.Values)
                    sum += score;
                return (int)Math.Round((double)sum / best.Count, MidpointRounding.AwayFromZero);
            }
        }
    }
}
