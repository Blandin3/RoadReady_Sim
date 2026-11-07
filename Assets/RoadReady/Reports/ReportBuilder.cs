using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace RoadReady.Reports
{
    /// <summary>The three parts of the email that the mail service needs.</summary>
    public struct EmailContent
    {
        public string subject;
        public string text;
        public string html;
    }

    /// <summary>
    /// Turns a finished SessionReport into the email text. Pure: no Unity, no network, so it is easy to test.
    /// Safety: every value that a person typed (the learner's name, the school name, scenario names) is cleaned first.
    /// In the HTML it is also HTML-encoded, so a name like "&lt;script&gt;" shows as text and can never run.
    /// </summary>
    public static class ReportBuilder
    {
        public const string TotalExplanation = "Overall score: average of best attempt per scenario";
        public const string Footer = "Simulated training result sent by RoadReady.";

        const int k_MaxNameLength = 80;
        const string k_Unnamed = "(no name)";

        public static EmailContent Build(SessionReport report) => new EmailContent
        {
            subject = Subject(report),
            text = PlainText(report),
            html = Html(report),
        };

        /// <summary>"RoadReady session report: {Name} - {TotalMarks}". Line breaks are removed so a name cannot add mail headers.</summary>
        public static string Subject(SessionReport report) =>
            $"RoadReady session report: {Clean(report.learnerName)} - {report.TotalMarks}";

        public static string PlainText(SessionReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RoadReady session report");
            sb.AppendLine();
            sb.AppendLine("School: " + Clean(r.schoolName));
            sb.AppendLine("Learner: " + Clean(r.learnerName));
            sb.AppendLine("Date: " + DateText(r.endUtc));
            sb.AppendLine("Session length: " + DurationText(r.DurationSeconds));
            sb.AppendLine("Total marks: " + r.TotalMarks + " / 100");
            sb.AppendLine(TotalExplanation);
            sb.AppendLine();
            sb.AppendLine("Attempts:");
            foreach (var a in r.attempts)
                sb.AppendLine($"- {Clean(a.Label)}: {a.score} / 100");
            sb.AppendLine();
            sb.AppendLine(Footer);
            return sb.ToString();
        }

        public static string Html(SessionReport r)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;max-width:560px\">");
            sb.Append("<h2 style=\"margin:0 0 12px\">RoadReady session report</h2>");
            sb.Append("<p style=\"margin:4px 0\"><b>School:</b> ").Append(Enc(r.schoolName)).Append("</p>");
            sb.Append("<p style=\"margin:4px 0\"><b>Learner:</b> ").Append(Enc(r.learnerName)).Append("</p>");
            sb.Append("<p style=\"margin:4px 0\"><b>Date:</b> ").Append(Enc(DateText(r.endUtc))).Append("</p>");
            sb.Append("<p style=\"margin:4px 0\"><b>Session length:</b> ").Append(Enc(DurationText(r.DurationSeconds))).Append("</p>");
            sb.Append("<p style=\"margin:12px 0 2px;font-size:20px\"><b>Total marks: ").Append(r.TotalMarks).Append(" / 100</b></p>");
            sb.Append("<p style=\"margin:0 0 12px;color:#555;font-size:13px\">").Append(Enc(TotalExplanation)).Append("</p>");
            sb.Append("<table style=\"border-collapse:collapse;width:100%\">");
            sb.Append("<tr><th style=\"text-align:left;border-bottom:2px solid #ccc;padding:6px\">Scenario</th>");
            sb.Append("<th style=\"text-align:right;border-bottom:2px solid #ccc;padding:6px\">Score</th></tr>");
            foreach (var a in r.attempts)
            {
                sb.Append("<tr><td style=\"border-bottom:1px solid #eee;padding:6px\">").Append(Enc(a.Label)).Append("</td>");
                sb.Append("<td style=\"border-bottom:1px solid #eee;padding:6px;text-align:right\">").Append(a.score).Append(" / 100</td></tr>");
            }

            sb.Append("</table>");
            sb.Append("<p style=\"margin:16px 0 0;color:#777;font-size:12px\">").Append(Enc(Footer)).Append("</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>HTML-encodes a cleaned value (&lt; &gt; &amp; and both kinds of quote).</summary>
        static string Enc(string value) => WebUtility.HtmlEncode(Clean(value));

        /// <summary>
        /// Makes a typed value safe to print: control characters (including line breaks and tabs) become spaces,
        /// the ends are trimmed and very long values are cut. An empty value becomes "(no name)".
        /// </summary>
        public static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return k_Unnamed;
            var sb = new StringBuilder(Math.Min(value.Length, k_MaxNameLength));
            foreach (var c in value)
            {
                if (sb.Length >= k_MaxNameLength)
                    break;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }

            var cleaned = sb.ToString().Trim();
            return cleaned.Length == 0 ? k_Unnamed : cleaned;
        }

        static string DateText(string utc)
        {
            if (DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                return when.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            return "unknown";
        }

        static string DurationText(double seconds)
        {
            var total = (int)Math.Round(seconds);
            return total < 60 ? $"{total} s" : $"{total / 60} min {total % 60} s";
        }
    }
}
