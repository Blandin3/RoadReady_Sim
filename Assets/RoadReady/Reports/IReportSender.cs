using System.Threading.Tasks;
using UnityEngine;

namespace RoadReady.Reports
{
    /// <summary>
    /// Anything that can deliver a finished report. Returns true only when the report was really handed over,
    /// and false when it could not be sent (no internet, not configured...). It must never throw.
    /// </summary>
    public interface IReportSender
    {
        Task<bool> SendAsync(SessionReport report);
    }

    /// <summary>
    /// Stage b stand-in used until the real Firestore sender is added: it only writes the email to the Console and
    /// reports "not sent", so the app never claims a report was delivered when it was not.
    /// </summary>
    public class LogOnlySender : IReportSender
    {
        public Task<bool> SendAsync(SessionReport report)
        {
            var email = ReportBuilder.Build(report);
            Debug.Log($"[RoadReady] Report NOT sent (no sender set up yet). Subject: {email.subject}\n{email.text}");
            return Task.FromResult(false);
        }
    }
}
