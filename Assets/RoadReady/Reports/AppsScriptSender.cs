using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace RoadReady.Reports
{
    /// <summary>
    /// Delivery option B (free, no Firebase billing). Posts the report as JSON to a Google Apps Script web app that
    /// sends the email from a Gmail account. See docs/email-relay/Code.gs and the README.
    ///
    /// The app sends plain FIELDS (names, marks), not ready-made email HTML. The script builds the email itself and
    /// escapes every value, so even someone who copies the shared secret out of the app cannot make it send their own HTML.
    /// The recipient is always the signed-in school's own login email, and the script only accepts addresses on its allowlist.
    /// </summary>
    public class AppsScriptSender : IReportSender
    {
        readonly string m_Url;
        readonly string m_Secret;
        readonly int m_TimeoutSeconds;

        public AppsScriptSender(ReportSettings settings)
        {
            m_Url = (settings.emailRelayUrl ?? "").Trim();
            m_Secret = settings.emailRelaySecret ?? "";
            m_TimeoutSeconds = Mathf.Max(3, Mathf.RoundToInt(settings.requestTimeoutSeconds));
        }

        // ------------------------------------------------------------------ the message sent to the script

        [Serializable]
        class Payload
        {
            public string secret;
            public string to;
            public string sessionId;
            public string schoolName;
            public string learnerName;
            public string endUtc;
            public int durationSeconds;
            public int totalMarks;
            public List<PayloadAttempt> attempts = new List<PayloadAttempt>();
        }

        [Serializable]
        class PayloadAttempt
        {
            public string label;
            public int score;
        }

        [Serializable]
        class Reply
        {
            public bool ok;
            public string error;
        }

        /// <summary>
        /// The JSON text sent to the script. Typed values are cleaned the same way as in the email builder (no line
        /// breaks, limited length). Pure, so it can be tested.
        /// </summary>
        public static string BuildPayloadJson(SessionReport report, string toEmail, string secret)
        {
            var payload = new Payload
            {
                secret = secret,
                to = toEmail,
                sessionId = report.sessionId,
                schoolName = ReportBuilder.Clean(report.schoolName),
                learnerName = ReportBuilder.Clean(report.learnerName),
                endUtc = report.endUtc,
                durationSeconds = (int)Math.Round(report.DurationSeconds),
                totalMarks = report.TotalMarks,
            };
            foreach (var a in report.attempts)
                payload.attempts.Add(new PayloadAttempt { label = ReportBuilder.Clean(a.Label), score = a.score });
            return JsonUtility.ToJson(payload);
        }

        // ------------------------------------------------------------------ sending

        public async Task<bool> SendAsync(SessionReport report)
        {
            try
            {
                if (!m_Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(m_Secret))
                {
                    Debug.LogWarning("[RoadReady] Report not sent: the email relay is not set up (see the README, 'End-of-session email report').");
                    return false;
                }

                var user = FirebaseAuth.DefaultInstance.CurrentUser;
                if (user == null || string.IsNullOrEmpty(user.Email))
                {
                    Debug.LogWarning("[RoadReady] Report not sent: no driving school is signed in.");
                    return false;
                }

                if (!string.IsNullOrEmpty(report.schoolId) && report.schoolId != user.UserId)
                {
                    Debug.LogWarning("[RoadReady] Report not sent: it belongs to a different school than the one signed in.");
                    return false;
                }

                var json = BuildPayloadJson(report, user.Email, m_Secret);
                using (var request = new UnityWebRequest(m_Url, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = m_TimeoutSeconds;

                    var finished = new TaskCompletionSource<bool>();
                    request.SendWebRequest().completed += _ => finished.TrySetResult(true);
                    await finished.Task;

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("[RoadReady] Report not sent: " + request.error);
                        return false;
                    }

                    var reply = JsonUtility.FromJson<Reply>(request.downloadHandler.text);
                    if (reply == null || !reply.ok)
                    {
                        Debug.LogWarning("[RoadReady] The email relay refused the report: " + (reply?.error ?? "unreadable reply"));
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RoadReady] Report not sent: " + e.Message);
                return false;
            }
        }
    }
}
