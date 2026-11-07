using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

namespace RoadReady.Reports
{
    /// <summary>
    /// Delivery option A. Sends the report by writing ONE document to the top-level "mail" collection in Firestore.
    /// The Firebase extension "Trigger Email from Firestore" sees the new document and sends the email, using SMTP
    /// settings that live in the Firebase console. This app therefore holds no email password and no API key.
    ///
    /// The recipient is ALWAYS the signed-in school's own login email. There is no way to type another address,
    /// and the Firestore rules check the same thing on the server.
    ///
    /// The document id is the report's session id, and the rules only allow creating a document, never changing one.
    /// So even if the same report is sent twice (a retry after a slow connection), only the first can ever exist and
    /// only one email goes out.
    /// </summary>
    public class FirestoreMailSender : IReportSender
    {
        public const string Collection = "mail";

        /// <summary>
        /// The document written to Firestore: exactly { to: [email], message: { subject, text, html } }.
        /// These are the only keys the security rules accept. Pure, so it can be tested.
        /// </summary>
        public static Dictionary<string, object> BuildDocument(SessionReport report, string toEmail)
        {
            var email = ReportBuilder.Build(report);
            return new Dictionary<string, object>
            {
                { "to", new List<object> { toEmail } },
                {
                    "message", new Dictionary<string, object>
                    {
                        { "subject", email.subject },
                        { "text", email.text },
                        { "html", email.html },
                    }
                },
            };
        }

        public async Task<bool> SendAsync(SessionReport report)
        {
            try
            {
                var user = FirebaseAuth.DefaultInstance.CurrentUser;
                if (user == null || string.IsNullOrEmpty(user.Email))
                {
                    Debug.LogWarning("[RoadReady] Report not sent: no driving school is signed in.");
                    return false;
                }

                // A report that belongs to another school is never sent from this login.
                if (!string.IsNullOrEmpty(report.schoolId) && report.schoolId != user.UserId)
                {
                    Debug.LogWarning("[RoadReady] Report not sent: it belongs to a different school than the one signed in.");
                    return false;
                }

                var document = BuildDocument(report, user.Email);

                // This finishes only when the server has accepted the document. With no internet it simply waits;
                // the coordinator stops waiting after a few seconds and Firestore keeps the write and sends it
                // when the connection returns.
                await FirebaseFirestore.DefaultInstance.Collection(Collection).Document(report.sessionId).SetAsync(document);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RoadReady] Report not sent: " + e.Message);
                return false;
            }
        }
    }
}
