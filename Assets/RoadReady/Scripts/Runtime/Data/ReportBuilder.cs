using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Data
{
    /// <summary>
    /// The overall results report a participant gets as a PDF: how they did across every scored attempt, scenario by
    /// scenario, what went well and what to work on. It is written after each attempt so it is ready to send as soon
    /// as the headset comes off (people rarely stay for the on-screen feedback).
    /// </summary>
    public static class ReportBuilder
    {
        /// <summary>Writes the report for one participant and returns its file name inside <see cref="ContactStore.Folder"/>, or null when there is nothing to report yet.</summary>
        public static string Write(DataService data, string participantId)
        {
            var contact = data.Contacts.Get(participantId);
            var participant = data.LoadParticipant(participantId);
            if (contact == null || participant == null)
                return null;

            var pdf = Build(participant, contact.name, data.GetAttempts(participantId));
            if (pdf == null)
                return null;

            Directory.CreateDirectory(data.Contacts.Folder);
            var file = $"{participantId}_{SafeName(contact.name)}.pdf";
            File.WriteAllBytes(Path.Combine(data.Contacts.Folder, file), pdf);
            data.Contacts.MarkReport(contact, file);
            return file;
        }

        static string SafeName(string name)
        {
            var chars = (name ?? "").Where(c => char.IsLetterOrDigit(c) && c < 128).Take(24).ToArray();
            return chars.Length > 0 ? new string(chars) : "report";
        }

        public static byte[] Build(ParticipantRecord participant, string name, IReadOnlyList<AttemptRecord> attempts)
        {
            var scored = attempts.Where(a => a.phase != AttemptPhase.Tutorial && a.endReason != AttemptEndReason.Aborted)
                .OrderBy(a => a.startUtc, StringComparer.Ordinal).ToList();
            if (scored.Count == 0)
                return null;

            var role = participant.group == Perspective.Driver ? "driver" : "pedestrian";
            var pdf = new PdfWriter();
            pdf.Text("RoadReady", 11f, true, PdfWriter.Blue);
            pdf.Text("Your road safety report", 24f, true);
            pdf.Text($"Prepared for {name}   |   {DateTime.Now:d MMMM yyyy}   |   Training as a {role}", 10.5f, false, PdfWriter.Grey);
            pdf.Rule();

            // ---------------------------------------------------------------- overall
            var average = scored.Average(a => a.score.total);
            var best = scored.Max(a => a.score.total);
            var hazardsTotal = scored.Sum(a => a.score.hazardsTotal);
            var hazardsHandled = scored.Sum(a => a.score.hazardsDetected);
            var rules = scored.Average(a => a.score.ruleComponent);
            var reactions = scored.Where(a => a.score.meanReactionTime >= 0f).Select(a => a.score.meanReactionTime).ToList();
            var violations = scored.Sum(a => a.score.violationCount);
            var collisions = scored.Count(a => a.score.collision);

            pdf.Heading("How you did overall");
            pdf.Text(Verdict(average, role));
            pdf.Space(4f);
            pdf.Text($"You completed {scored.Count} scored attempt{(scored.Count == 1 ? "" : "s")} across {scored.Select(a => a.scenarioId + a.perspective).Distinct().Count()} scenario{(scored.Select(a => a.scenarioId + a.perspective).Distinct().Count() == 1 ? "" : "s")}. " +
                     $"Your best safety score was {best:0} out of 100.");
            if (scored.Count >= 2)
            {
                var first = scored[0].score.total;
                var last = scored[scored.Count - 1].score.total;
                pdf.Text(last > first + 2f ? $"You are improving: your first score was {first:0} and your latest is {last:0}."
                    : last < first - 2f ? $"Your latest score ({last:0}) is lower than your first ({first:0}); a little more practice will bring it back up."
                    : $"Your scores are steady ({first:0} first, {last:0} latest).");
            }

            pdf.Space(6f);
            pdf.Bar("Average safety score", $"{average:0} / 100", average / 100f, ScoreColor(average));
            if (hazardsTotal > 0)
                pdf.Bar("Hazards handled safely", $"{hazardsHandled} of {hazardsTotal}", hazardsHandled / (float)hazardsTotal, ScoreColor(100f * hazardsHandled / hazardsTotal));
            pdf.Bar("Following the rules of the road", $"{rules:0} / 100", rules / 100f, ScoreColor(rules));
            if (reactions.Count > 0)
                pdf.Text($"Average reaction time to a hazard: {reactions.Average():0.0} seconds.", 10.5f, false, PdfWriter.Grey);
            pdf.Text($"Rule violations: {violations}.   Collisions: {collisions}.", 10.5f, false, PdfWriter.Grey);

            // ---------------------------------------------------------------- scenario by scenario
            var groups = scored.GroupBy(a => a.scenarioId + "|" + a.perspective).Select(g => g.ToList()).ToList();
            var tips = new List<string>();
            pdf.Heading("Scenario by scenario");
            foreach (var group in groups)
            {
                var latest = group[group.Count - 1];
                var s = latest.score;
                pdf.KeepTogether(120f);
                pdf.Rule();
                pdf.Text($"{latest.scenarioName}  ({(latest.perspective == Perspective.Driver ? "as a driver" : "as a pedestrian")})", 12.5f, true, PdfWriter.Blue);
                pdf.Text($"{group.Count} attempt{(group.Count == 1 ? "" : "s")}: {string.Join("  >  ", group.Select(a => a.score.total.ToString("0")))}      Latest grade: {s.grade}", 10.5f);
                pdf.Bar("Latest safety score", $"{s.total:0} / 100", s.total / 100f, ScoreColor(s.total));

                var good = latest.decisions.Where(d => d.decision == DecisionClass.Safe).Select(d => d.title)
                    .Concat(latest.hazardRecords.Where(h => h.decision == DecisionClass.Safe).Select(h => h.hazardName))
                    .Where(t => !string.IsNullOrEmpty(t)).Distinct().Take(4).ToList();
                if (good.Count > 0)
                {
                    pdf.Text("What went well", 10.5f, true, PdfWriter.Green);
                    foreach (var line in good)
                        pdf.Text("-  " + line, 10f, false, null, 10f);
                }

                var work = new List<string>();
                foreach (var d in latest.decisions.Where(d => d.decision != DecisionClass.Safe).Take(5))
                {
                    work.Add(string.IsNullOrEmpty(d.detail) ? d.title : $"{d.title}: {d.detail}");
                    if (!string.IsNullOrEmpty(d.tip))
                        tips.Add(d.tip);
                }

                foreach (var h in latest.hazardRecords.Where(h => h.decision != DecisionClass.Safe).Take(3))
                {
                    work.Add(string.IsNullOrEmpty(h.feedback) ? h.hazardName : $"{h.hazardName}: {h.feedback}");
                    if (!string.IsNullOrEmpty(h.otherPerspectiveInsight))
                        tips.Add(h.otherPerspectiveInsight);
                }

                if (work.Count > 0)
                {
                    pdf.Space(3f);
                    pdf.Text("To work on", 10.5f, true, PdfWriter.Amber);
                    foreach (var line in work.Distinct())
                        pdf.Text("-  " + line, 10f, false, null, 10f);
                }
                else if (good.Count == 0)
                {
                    pdf.Text("A clean run: no decisions to flag.", 10f, false, PdfWriter.Green);
                }
            }

            // ---------------------------------------------------------------- next steps
            var next = tips.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(6).ToList();
            if (next.Count > 0)
            {
                pdf.Heading("Keep practising");
                foreach (var tip in next)
                    pdf.Text("-  " + tip, 10.5f, false, null, 10f);
            }

            pdf.Space(10f);
            pdf.Rule();
            pdf.Text("This report is only for you. It was made by the RoadReady training simulator from your own attempts, and your name and email are used for nothing else.", 9f, false, PdfWriter.Grey);
            return pdf.Build("RoadReady results for " + name);
        }

        static string Verdict(float average, string role) =>
            average >= 85f ? $"Excellent. As a {role} you made safe decisions almost all of the time and reacted to hazards in good time."
            : average >= 70f ? $"Good work. As a {role} you handled most situations safely; a few decisions below can still be sharper."
            : average >= 50f ? $"You are getting there. As a {role} you spotted some hazards but missed others, and a few road rules slipped. The notes below show where to focus."
            : $"This is a start. As a {role} several hazards and rules were missed, which is exactly what practice is for. The notes below show where to focus first.";

        static Color ScoreColor(float score) => score >= 85f ? PdfWriter.Green : score >= 50f ? PdfWriter.Amber : PdfWriter.Red;
    }
}
