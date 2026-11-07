using UnityEngine;

namespace RoadReady.Reports
{
    public enum ReportDelivery
    {
        /// <summary>Free: a Google Apps Script web app sends the email from a Gmail account (docs/email-relay/Code.gs).</summary>
        AppsScript,
        /// <summary>Needs the Firebase Blaze plan: writes to the "mail" collection for the "Trigger Email from Firestore" extension.</summary>
        FirestoreMail,
    }

    /// <summary>
    /// Settings for the end-of-session report. To create them: Assets > Create > RoadReady > Report Settings, name the
    /// file exactly "ReportSettings" and keep it in Assets/RoadReady/Reports/Resources. That file holds your relay secret,
    /// so it is kept out of git on purpose (see .gitignore). Without the file the defaults below are used and nothing is sent.
    /// </summary>
    [CreateAssetMenu(menuName = "RoadReady/Report Settings", fileName = "ReportSettings")]
    public class ReportSettings : ScriptableObject
    {
        [Header("How the email is delivered")]
        public ReportDelivery delivery = ReportDelivery.AppsScript;

        [Header("Apps Script relay (free option)")]
        [Tooltip("The web app URL from 'Deploy > New deployment' in Apps Script. Must start with https://")]
        public string emailRelayUrl = "";
        [Tooltip("The same secret you put in Code.gs. It ends up inside the build, so it only protects against casual misuse: the script also checks an allowlist of school emails.")]
        public string emailRelaySecret = "";
        [Tooltip("Hard limit for one request (the learner does not wait this long; see below).")]
        public float requestTimeoutSeconds = 10f;

        [Header("Timing (the learner never waits longer than the two together, 5 s by default)")]
        [Tooltip("How long to wait for the email to be accepted before giving up and saving it for later.")]
        public float sendTimeoutSeconds = 3.5f;
        [Tooltip("How long the final message (sent / will be sent later) stays on screen before leaving.")]
        public float resultHoldSeconds = 1.5f;

        [Header("Messages")]
        public bool showNoScoresMessage = true;
        public string sendingMessage = "Sending report...";
        public string sentMessage = "Report sent to your driving school";
        public string offlineMessage = "No internet. The report will be sent later.";
        public string noScoresMessage = "No scores to report";

        /// <summary>The settings asset if there is one in a Resources folder, otherwise a copy with the default values.</summary>
        public static ReportSettings Load()
        {
            var asset = Resources.Load<ReportSettings>("ReportSettings");
            return asset != null ? asset : CreateInstance<ReportSettings>();
        }
    }
}
