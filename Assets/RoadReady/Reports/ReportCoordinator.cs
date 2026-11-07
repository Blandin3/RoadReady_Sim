using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using RoadReady.Accounts;
using RoadReady.Core;
using RoadReady.Data;
using UnityEngine;

namespace RoadReady.Reports
{
    /// <summary>
    /// Wires the end-of-session report together. It starts itself when the app starts, so the game does not need to
    /// know it exists: the game only rings the two doorbells in SessionHooks.
    ///
    ///  * It watches who is training (CurrentMember). When a learner is picked it starts a SessionTracker session.
    ///  * Each scored attempt the game announces is added to that session.
    ///  * When the learner presses Done, the game asks us to run before leaving. We end the session, build the report,
    ///    show a status message, try to send it, and then tell the game to carry on. The learner never waits more than
    ///    sendTimeoutSeconds + resultHoldSeconds (5 s by default).
    ///
    /// A headset taken off mid-session, or a learner who leaves without pressing Done, is not reported.
    /// </summary>
    public class ReportCoordinator : MonoBehaviour
    {
        /// <summary>
        /// Who delivers the email. Normally left empty: it is then chosen from ReportSettings (the free Apps Script
        /// relay by default, or the Firestore "mail" collection). Tests can set their own.
        /// </summary>
        public static IReportSender Sender { get; set; }

        static ReportCoordinator s_Instance;
        static Action<AttemptRecord> s_AttemptHandler;

        readonly SessionTracker m_Tracker = new SessionTracker();
        readonly Dictionary<string, string> m_SchoolNames = new Dictionary<string, string>();

        ReportSettings m_Settings;
        ReportStatusView m_Status;
        bool m_Closed;   // Done was pressed; wait for the learner to leave before starting another session
        bool m_Busy;     // a report is being sent right now (ignores a second tap on Done)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            // Clean up first, in case the editor keeps old subscriptions when entering Play mode without a reload.
            SessionHooks.BeforeLeaving = null;
            Sender = null; // chosen again from ReportSettings in Awake
            if (s_AttemptHandler != null)
                SessionHooks.ScoredAttemptFinished -= s_AttemptHandler;

            var go = new GameObject("RoadReadyReports");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<ReportCoordinator>();
        }

        void Awake()
        {
            m_Settings = ReportSettings.Load();
            if (Sender == null)
                Sender = m_Settings.delivery == ReportDelivery.FirestoreMail ? new FirestoreMailSender() : (IReportSender)new AppsScriptSender(m_Settings);
            m_Status = ReportStatusView.Create(transform);
            s_AttemptHandler = OnScoredAttempt;
            SessionHooks.ScoredAttemptFinished += s_AttemptHandler;
            SessionHooks.BeforeLeaving = OnBeforeLeaving;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                if (s_AttemptHandler != null)
                    SessionHooks.ScoredAttemptFinished -= s_AttemptHandler;
                SessionHooks.BeforeLeaving = null;
                s_Instance = null;
            }
        }

        // ------------------------------------------------------------------ session start

        void Update()
        {
            // The learner is "in training" while the school screens have handed them to the game.
            var training = CurrentMember.IsSet && SchoolHandoff.HasLearner;
            if (!training)
            {
                m_Closed = false; // back at Home: the next learner can start a fresh session
                return;
            }

            if (m_Closed || m_Busy)
                return;
            if (!m_Tracker.IsFor(CurrentMember.MemberId))
                BeginSession();
        }

        void BeginSession()
        {
            var schoolId = CurrentMember.SchoolId;
            m_Tracker.Begin(CurrentMember.MemberId, schoolId, SchoolNameOr(schoolId, "your driving school"), CurrentMember.FullName);
            if (!m_SchoolNames.ContainsKey(schoolId ?? ""))
                _ = FetchSchoolName(schoolId);
        }

        /// <summary>Looks up the school's name once (Firestore keeps it cached offline too) so the email can show it.</summary>
        async Task FetchSchoolName(string schoolId)
        {
            try
            {
                var school = await AuthService.GetSchoolAsync();
                if (!string.IsNullOrWhiteSpace(school.Name))
                    m_SchoolNames[schoolId ?? ""] = school.Name;
            }
            catch (Exception e)
            {
                Debug.Log("[RoadReady] Could not read the school name for the report: " + e.Message);
            }
        }

        string SchoolNameOr(string schoolId, string fallback) =>
            m_SchoolNames.TryGetValue(schoolId ?? "", out var name) ? name : fallback;

        // ------------------------------------------------------------------ attempts

        void OnScoredAttempt(AttemptRecord attempt)
        {
            if (!CurrentMember.IsSet || !SchoolHandoff.HasLearner)
                return; // played straight from the bootstrap scene, not through a school login
            if (!m_Tracker.IsFor(CurrentMember.MemberId))
                BeginSession();

            // Parking and the open road have only one role (driver), so they show their name alone.
            var oneRole = attempt.scenarioType == ScenarioType.ParkingBay || attempt.scenarioType == ScenarioType.OpenRoad;
            var role = oneRole ? "" : attempt.perspective == Perspective.Driver ? "driver" : "pedestrian";
            m_Tracker.RecordAttempt(attempt.scenarioId, attempt.scenarioName, role, attempt.score.total, attempt.durationSeconds);
        }

        // ------------------------------------------------------------------ end of session

        void OnBeforeLeaving(Action carryOn)
        {
            if (m_Busy)
                return; // a second tap on Done: the first one is already handling it and will carry on
            m_Busy = true;
            StartCoroutine(EndSessionRoutine(carryOn));
        }

        IEnumerator EndSessionRoutine(Action carryOn)
        {
            var report = m_Tracker.End();
            m_Closed = true;

            if (report == null)
            {
                // Nothing was scored, so nothing is sent.
                if (m_Settings.showNoScoresMessage)
                {
                    m_Status.Show(m_Settings.noScoresMessage);
                    yield return WaitRealtime(m_Settings.resultHoldSeconds);
                    m_Status.Hide();
                }

                Finish(carryOn);
                yield break;
            }

            report.schoolName = SchoolNameOr(report.schoolId, report.schoolName);
            m_Status.Show(m_Settings.sendingMessage);

            var send = SendSafely(report);
            var waited = 0f;
            while (!send.IsCompleted && waited < m_Settings.sendTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var sent = send.IsCompleted && send.Result;
            m_Status.Show(sent ? m_Settings.sentMessage : m_Settings.offlineMessage);
            yield return WaitRealtime(m_Settings.resultHoldSeconds);
            m_Status.Hide();
            Finish(carryOn);
        }

        /// <summary>Sends the report and turns any error into "not sent", so the learner is never stuck.</summary>
        static async Task<bool> SendSafely(SessionReport report)
        {
            try
            {
                return await Sender.SendAsync(report);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RoadReady] Sending the report failed: " + e.Message);
                return false;
            }
        }

        static IEnumerator WaitRealtime(float seconds)
        {
            var until = Time.unscaledTime + Mathf.Max(0f, seconds);
            while (Time.unscaledTime < until)
                yield return null;
        }

        void Finish(Action carryOn)
        {
            m_Busy = false;
            carryOn();
        }
    }
}
