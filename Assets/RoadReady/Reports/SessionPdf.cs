using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RoadReady.Data;
using UnityEngine;

namespace RoadReady.Reports
{
    /// <summary>
    /// The PDF attached to the driving school's email: every scenario the learner explored with all the marks they
    /// earned there, the average, and every mistake they made. Pure (no Unity scene, no network) so it can be tested.
    /// </summary>
    public static class SessionPdf
    {
        const int k_MaxMistakesPerAttempt = 40;

        /// <summary>One line of the results table: a scenario in one role, with every attempt's mark.</summary>
        public class ScenarioLine
        {
            public string label;
            public List<int> scores = new List<int>();
            public int mistakes;
            public int Best => scores.Count == 0 ? 0 : scores.Max();
        }

        /// <summary>Scenarios in the order the learner first played them.</summary>
        public static List<ScenarioLine> Scenarios(SessionReport report)
        {
            var lines = new List<ScenarioLine>();
            var byKey = new Dictionary<string, ScenarioLine>();
            foreach (var a in report.attempts)
            {
                if (!byKey.TryGetValue(a.GroupKey, out var line))
                {
                    line = new ScenarioLine { label = a.Label };
                    byKey[a.GroupKey] = line;
                    lines.Add(line);
                }

                line.scores.Add(a.score);
                line.mistakes += a.mistakes?.Count ?? 0;
            }

            return lines;
        }

        /// <summary>Average of the learner's best mark in each scenario explored (the same figure as the email's total marks).</summary>
        public static int AverageOfBest(SessionReport report) => report.TotalMarks;

        /// <summary>Average of every attempt's mark, rounded (halves up).</summary>
        public static int AverageOfAll(SessionReport report) =>
            report.HasScores ? (int)Math.Round(report.attempts.Average(a => (double)a.score), MidpointRounding.AwayFromZero) : 0;

        public static string FileName(SessionReport report)
        {
            var chars = (report.learnerName ?? "").Where(c => char.IsLetterOrDigit(c) && c < 128).Take(24).ToArray();
            var name = chars.Length > 0 ? new string(chars) : "learner";
            return $"RoadReady_report_{name}.pdf";
        }

        public static byte[] Build(SessionReport report)
        {
            var scenarios = Scenarios(report);
            var allMistakes = report.attempts.SelectMany(a => a.mistakes ?? new List<ReportMistake>()).ToList();
            var average = AverageOfBest(report);
            var pdf = new PdfWriter();

            pdf.Text("RoadReady", 11f, true, PdfWriter.Blue);
            pdf.Text("Driving session report", 24f, true);
            pdf.Text($"Learner: {report.learnerName}   |   Driving school: {report.schoolName}", 10.5f, false, PdfWriter.Grey);
            pdf.Text($"{FormatDate(report.endUtc)}   |   Session length {Duration(report.DurationSeconds)}   |   {report.attempts.Count} attempt(s), {scenarios.Count} scenario(s)", 10.5f, false, PdfWriter.Grey);
            pdf.Rule();

            // ---------------------------------------------------------------- average
            pdf.Heading("Overall result");
            pdf.Bar($"Average mark: {average} / 100  ({Grade(average)})", "", average / 100f, GradeColour(average));
            pdf.Text($"Average of the best mark in each of the {scenarios.Count} scenario(s) explored. Average of all {report.attempts.Count} attempt(s): {AverageOfAll(report)} / 100.", 9.5f, false, PdfWriter.Grey);
            pdf.Text($"Mistakes made in this session: {allMistakes.Count}", 10.5f, true, allMistakes.Count == 0 ? PdfWriter.Green : PdfWriter.Red);

            // ---------------------------------------------------------------- every scenario with its marks
            pdf.Heading("Marks in every scenario");
            foreach (var s in scenarios)
            {
                pdf.KeepTogether(60f);
                pdf.Text(s.label, 11.5f, true);
                pdf.Text($"Marks earned: {string.Join(", ", s.scores.Select(x => x.ToString(CultureInfo.InvariantCulture)))}   |   Best: {s.Best} / 100   |   Attempts: {s.scores.Count}   |   Mistakes: {s.mistakes}", 10f, false, PdfWriter.Grey, 10f);
                pdf.Bar("Best", $"{s.Best} / 100", s.Best / 100f, GradeColour(s.Best));
            }

            // ---------------------------------------------------------------- mistakes
            pdf.Heading("Mistakes made while practising");
            if (allMistakes.Count == 0)
            {
                pdf.Text("No mistakes were recorded. Well done.", 10.5f, false, PdfWriter.Green);
            }
            else
            {
                pdf.Text("Most common:", 10.5f, true);
                foreach (var group in allMistakes.GroupBy(m => m.title).OrderByDescending(g => g.Count()).Take(8))
                    pdf.Text($"- {group.Key}  (x{group.Count()})", 10.5f, false, null, 10f);

                pdf.Space(4f);
                pdf.Text("Detailed mistakes by Timestamps", 10.5f, true);

                // Each scenario (in the order it was first played) has its own block with its own attempts' mistakes.
                var number = new Dictionary<string, int>();
                var attemptNumbers = new List<int>();
                foreach (var a in report.attempts)
                {
                    number[a.GroupKey] = number.TryGetValue(a.GroupKey, out var n) ? n + 1 : 1;
                    attemptNumbers.Add(number[a.GroupKey]);
                }

                var groupOrder = new List<string>();
                foreach (var a in report.attempts)
                    if (!groupOrder.Contains(a.GroupKey))
                        groupOrder.Add(a.GroupKey);

                foreach (var key in groupOrder)
                {
                    var inScenario = new List<int>();
                    for (var i = 0; i < report.attempts.Count; i++)
                        if (report.attempts[i].GroupKey == key && report.attempts[i].mistakes != null && report.attempts[i].mistakes.Count > 0)
                            inScenario.Add(i);
                    if (inScenario.Count == 0)
                        continue;

                    pdf.KeepTogether(60f);
                    pdf.Space(6f);
                    pdf.Text(report.attempts[inScenario[0]].Label, 11.5f, true);
                    foreach (var i in inScenario)
                    {
                        var a = report.attempts[i];
                        pdf.KeepTogether(40f);
                        pdf.Space(2f);
                        pdf.Text($"Attempt {attemptNumbers[i]} (mark {a.score} / 100)", 10.5f, true, null, 6f);
                        foreach (var m in a.mistakes.Take(k_MaxMistakesPerAttempt))
                        {
                            var when = $"{m.timeSeconds / 60}:{(m.timeSeconds % 60).ToString("00", CultureInfo.InvariantCulture)}";
                            var severity = string.IsNullOrEmpty(m.severity) ? "" : $" [{m.severity}]";
                            var speed = m.speedKmh > 0 ? $", {m.speedKmh} km/h" : "";
                            pdf.Text($"- {when}  {m.title}{severity}{speed}", 10f, false, SeverityColour(m.severity), 14f);
                        }

                        if (a.mistakes.Count > k_MaxMistakesPerAttempt)
                            pdf.Text($"... and {a.mistakes.Count - k_MaxMistakesPerAttempt} more", 9.5f, false, PdfWriter.Grey, 14f);
                    }
                }
            }

            pdf.Space(10f);
            pdf.Rule();
            pdf.Text("Simulated training result sent by RoadReady.", 9f, false, PdfWriter.Grey);
            return pdf.Build("RoadReady - " + report.learnerName);
        }

        // The same bands as the game and the email: 85 / 70 / 50.
        public static string Grade(int score) => score >= 85 ? "Road Ready" : score >= 70 ? "Almost there" : score >= 50 ? "Needs practice" : "Unsafe - try again";

        static Color GradeColour(int score) => score >= 85 ? PdfWriter.Green : score >= 70 ? PdfWriter.Amber : PdfWriter.Red;

        static Color SeverityColour(string severity) => severity == "Critical" || severity == "Major" ? PdfWriter.Red : severity == "Minor" ? PdfWriter.Amber : PdfWriter.Grey;

        static string Duration(double seconds)
        {
            var s = (int)Math.Round(seconds);
            return s < 60 ? s + " s" : s / 60 + " min " + s % 60 + " s";
        }

        static string FormatDate(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? d.ToUniversalTime().AddHours(2).ToString("d MMMM yyyy HH:mm", CultureInfo.InvariantCulture) + " CAT (Rwanda time)" // Rwanda is UTC+2 all year
                : "date unknown";
    }
}
