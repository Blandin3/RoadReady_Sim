using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Player;
using RoadReady.Scenarios;
using RoadReady.Scoring;
using RoadReady.UI;
using UnityEngine;

namespace RoadReady
{
    /// <summary>
    /// Application orchestrator ("RoadReady System" in the class diagram). Implements the session flow of the
    /// proposal flowchart (Figure 4): setup -> consent -> instructions -> tutorial -> module selection ->
    /// attempt -> feedback -> retry / switch perspective -> questionnaires -> debrief.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RoadReadyApp : MonoBehaviour
    {
        [SerializeField] RoadReadyConfig m_Config;
        [SerializeField] ScenarioManager m_Scenarios;
        [SerializeField] RoadReadyUI m_UI;
        [SerializeField] HudController m_Hud;
        [SerializeField] ScreenFader m_Fader;
        [SerializeField] PlayerRig m_Rig;
        [SerializeField] PedestrianController m_Pedestrian;
        [SerializeField] MotionComfortVignette m_Vignette;
        [SerializeField] InstructionVoice m_Voice;
        [SerializeField] RemoteSync m_Remote;

        readonly Queue<QuestionnaireType> m_PendingQuestionnaires = new Queue<QuestionnaireType>();
        AttemptRequest m_LastRequest;
        AttemptRecord m_LastAttempt;
        FeedbackReport m_LastReport;
        bool m_ReturnToMenuAfterTutorial;
        ScenarioDefinition m_PickedScenario;

        public static RoadReadyApp Instance { get; private set; }

        public RoadReadyConfig Config => m_Config;
        public DataService Data { get; private set; }
        public ScenarioOverrideStore Overrides { get; private set; }
        public ComfortSettings Comfort { get; private set; }
        public ScenarioManager Scenarios => m_Scenarios;
        public RoadReadyUI UI => m_UI;
        public PlayerRig Rig => m_Rig;
        public RemoteSync Remote => m_Remote;
        public AppState State { get; private set; } = AppState.Boot;
        public Perspective SelectedPerspective { get; set; }
        public ParticipantRecord Participant => Data.CurrentParticipant;
        public SessionRecord Session => Data.CurrentSession;
        public FeedbackReport LastReport => m_LastReport;
        public AttemptRequest LastRequest => m_LastRequest;
        public QuestionnaireResult LastSsq { get; private set; }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 90;
            Data = new DataService();
            Overrides = new ScenarioOverrideStore(Data.RootFolder);
            Comfort = ComfortSettings.Load(m_Config.defaultComfort);
            AudioListener.volume = Comfort.masterVolume;
            EnsurePanelInputConfiguration();
        }

        /// <summary>
        /// XRI's starter rig brings a uGUI EventSystem, which would otherwise intercept UI Toolkit input.
        /// XRI recommends a PanelInputConfiguration with input redirection disabled.
        /// </summary>
        static void EnsurePanelInputConfiguration()
        {
            var config = FindAnyObjectByType<UnityEngine.UIElements.PanelInputConfiguration>();
            if (config == null)
                config = new GameObject("Panel Input Configuration").AddComponent<UnityEngine.UIElements.PanelInputConfiguration>();
            config.panelInputRedirection = UnityEngine.UIElements.PanelInputConfiguration.PanelInputRedirection.Never;
        }

        void Start()
        {
            m_Rig.Initialize(Comfort);
            m_Rig.AutoRecentered += OnRigAutoRecentered;
            if (m_Pedestrian != null)
                m_Pedestrian.Initialize(m_Rig, Comfort);
            if (m_Vignette != null)
                m_Vignette.Initialize(m_Rig.Vignette, Comfort);
            if (m_Voice != null)
                m_Voice.Initialize(m_Config, Comfort);
            if (m_Remote != null)
                m_Remote.Initialize(m_Config, Data);

            m_Scenarios.Initialize(m_Config, Data, Overrides, m_Fader);
            m_Scenarios.AttemptFinished += OnAttemptFinished;
            m_Scenarios.AttemptFailed += OnAttemptFailed;
            m_UI.Initialize(this);
            m_UI.ApplyTextScale(Comfort.textScale);
            m_Hud.Initialize(this, m_Scenarios);

            // Plain background behind the menus: hide the lobby's floor (its collider stays, so the rig still stands on it).
            if (m_Scenarios.Lobby != null)
                foreach (var r in m_Scenarios.Lobby.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;

            if (m_Config.lobbyBackdrop != null && m_Scenarios.Lobby != null)
                LobbyBackdrop.Create(m_Scenarios.Lobby, m_Rig.Head, m_Config.lobbyBackdrop);

            m_Rig.ReturnToLobby();
            StartCoroutine(m_Fader.FadeTo(0f, 0.8f));
            StartCoroutine(ShowSplashWhenRigReady());
        }

        /// <summary>
        /// The XR Origin applies its camera height a few frames after Start (and without a headset it never
        /// tracks at all), so placing the menu immediately would put it below the learner's eyes.
        /// </summary>
        System.Collections.IEnumerator ShowSplashWhenRigReady()
        {
            for (var i = 0; i < 3; i++)
                yield return null;
            if (SchoolHandoff.HasLearner)
            {
                // Arrived from the school Home screens: the learner is already chosen, so skip the splash and name page.
                LearnerName = SchoolHandoff.LearnerName;
                ShowWelcome();
                yield break;
            }

            ShowSplash();
        }

        /// <summary>The headset was put back on: bring a floating menu back in front of the learner.</summary>
        void OnRigAutoRecentered()
        {
            if (m_UI != null && m_UI.MenuVisible)
                m_UI.Reposition();
        }

        void Update()
        {
            UpdateBackground();

            if (RoadReadyInput.Instance.Pause.WasPressedThisFrame() || Player.RacingWheel.Pressed(Player.RacingWheel.PauseButton))
            {
                if (State == AppState.InScenario)
                    Pause();
                else if (State == AppState.Paused)
                    Resume();
            }

            // Right thumbstick: slide the seat (your position in the car) while no menu is in the way.
            if (m_Rig != null && m_Rig.IsSeated && !m_UI.MenuVisible)
                m_Rig.AdjustSeat(RoadReadyInput.Instance.SeatMove.ReadValue<Vector2>(), Time.unscaledDeltaTime);

            // Wheel cross pad: turn the wing mirrors for a good view.
            if (m_Scenarios != null && m_Scenarios.Driver != null && !m_UI.MenuVisible)
            {
                var mirrors = m_Scenarios.Driver.GetComponent<Player.CarMirrors>();
                if (mirrors != null)
                    mirrors.Adjust(Player.RacingWheel.CrossPad(), Time.unscaledDeltaTime);
            }

            // Re-centre from any state (driving, countdown, paused, menus): wheel triangle, R, or right thumbstick click.
            if (RoadReadyInput.Instance.Recenter.WasPressedThisFrame() || Player.RacingWheel.Pressed(Player.RacingWheel.RecenterButton))
                RecenterView();
        }

        /// <summary>
        /// Plain dark-blue background (config colour) while no scenario is loaded, so the menus stand out; the
        /// scenario's own sky as soon as one is. A uniform colour has no detail to move against, so it is comfortable.
        /// </summary>
        void UpdateBackground()
        {
            var camera = m_Rig != null ? m_Rig.Head : null;
            if (camera == null || m_Scenarios == null)
                return;
            var plain = !m_Scenarios.HasScenarioLoaded;
            var flags = plain ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (camera.clearFlags != flags)
                camera.clearFlags = flags;
            if (plain)
                camera.backgroundColor = m_Config.lobbyBackgroundColor;
        }

        void SetState(AppState state)
        {
            var previous = State;
            State = state;
            RoadReadyEvents.RaiseAppStateChanged(previous, state);
        }

        // ================================================================ splash & welcome

        public void ShowSplash()
        {
            SetState(AppState.Splash);
            m_UI.Show(new SplashScreen());
        }

        /// <summary>Landing page: a learner types a name; the driving school logs in to see who took part today.</summary>
        public void ShowLanding()
        {
            LearnerName = null; // a new learner is about to type their name
            SetState(AppState.Welcome);
            m_UI.Show(new LandingScreen());
        }

        public void ShowSchoolLearners()
        {
            SetState(AppState.Welcome);
            m_UI.Show(new SchoolLearnersScreen());
        }

        /// <summary>The name the learner typed on the landing page; later screens reuse it so it is only typed once.</summary>
        public string LearnerName { get; private set; }

        public void LearnerStarted(string name)
        {
            LearnerName = (name ?? "").Trim();
            Data.School.AddLearner(name);
            ShowWelcome();
        }

        public void ShowWelcome()
        {
            SetState(AppState.Welcome);
            m_UI.Show(new WelcomeScreen());
        }

        public void ShowHowItWorks()
        {
            ShowMessage(AppState.Welcome, "HOW IT WORKS", "One street, two road users",
                "Each scenario is a real Kigali street situation - a zebra crossing, traffic lights or a moto-taxi junction.\n\n" +
                "You play it as a DRIVER, then as a PEDESTRIAN. After every attempt you get a safety score and see what the other road user saw.\n\n" +
                "Your first attempt is your baseline. Practise as often as you like and watch your score improve.",
                "Back", ShowWelcome);
        }

        // ================================================================ participant & consent

        public void GoToSetup()
        {
            SetState(AppState.ParticipantSetup);
            m_UI.Show(new ParticipantSetupScreen());
        }

        /// <summary>The key that ties a person to their saved study details: the member code from the school Home, or the name they typed.</summary>
        public string LearnerKey => SchoolHandoff.HasLearner
            ? LearnerProfileStore.MakeKey(SchoolHandoff.LearnerName, SchoolHandoff.LearnerCode)
            : LearnerProfileStore.MakeKey(LearnerName, null);

        /// <summary>The study record saved earlier for the current learner, or null the first time.</summary>
        public ParticipantRecord FindLearnerProfile()
        {
            var id = Data.Learners.FindParticipantId(LearnerKey);
            return id == null ? null : Data.LoadParticipant(id);
        }

        /// <summary>
        /// The learner approved (or edited) their setup details. First time: a study record is created and tied to their name.
        /// Next times: the saved record is updated with any changes and used, so nothing is typed in again.
        /// </summary>
        public void ConfirmParticipant(Perspective group, StudyCondition condition, AgeBand age, DrivingExperience experience, bool guardianConsent, bool postTest)
        {
            var saved = FindLearnerProfile();
            if (saved == null)
            {
                RegisterParticipant(group, condition, age, experience, guardianConsent);
                return;
            }

            saved.group = group;
            saved.condition = condition;
            saved.ageBand = age;
            saved.drivingExperience = experience;
            saved.guardianConsentVerified = guardianConsent;
            Data.SaveParticipant(saved);
            SelectReturningParticipant(saved.participantId, postTest);
        }

        public void RegisterParticipant(Perspective group, StudyCondition condition, AgeBand age, DrivingExperience experience, bool guardianConsent)
        {
            Data.CreateParticipant(group, condition, age, experience, guardianConsent);
            if (LearnerKey != null)
                Data.Learners.Link(LearnerKey, Data.CurrentParticipant.participantId);
            SelectedPerspective = group;
            SetState(AppState.Consent);
            m_UI.Show(new ConsentScreen());
        }

        public void SelectReturningParticipant(string participantId, bool postTest)
        {
            var participant = Data.LoadParticipant(participantId);
            if (participant == null)
            {
                RoadReadyEvents.Toast("Could not load that participant.");
                return;
            }

            Data.SetCurrentParticipant(participant);
            SelectedPerspective = participant.group;
            if (!participant.consentGiven)
            {
                SetState(AppState.Consent);
                m_UI.Show(new ConsentScreen());
                return;
            }

            Data.BeginSession(participant, postTest);
            ShowInstructions();
        }

        public void SubmitConsent(bool agreed)
        {
            var participant = Participant;
            if (!agreed)
            {
                // No consent = no data kept (flowchart: "Participant excluded").
                if (participant != null)
                    Data.DeleteParticipant(participant.participantId);
                ShowMessage(AppState.Excluded, "THANK YOU", "No problem", "You have chosen not to take part. No data about you has been stored. Please remove the headset and let the researcher know.", "Back to start", GoToSetup);
                return;
            }

            Data.RecordConsent(participant, true);
            Data.BeginSession(participant, false);
            ShowInstructions(); // the learner's name is asked on the landing page; the report email stays optional on the feedback screen
        }

        // ================================================================ results report (name + email, optional)

        /// <summary>
        /// Asks (once, up front) for a name and an email so the participant gets a PDF of how they did: people tend to
        /// take the headset off before the on-screen feedback. Skipped when they have already left one.
        /// </summary>
        void OfferReportContact(System.Action next)
        {
            var participant = Participant;
            if (participant == null || Data.Contacts.Get(participant.participantId) != null)
            {
                next();
                return;
            }

            SetState(AppState.Consent);
            m_UI.Show(new ContactScreen(participant.participantId, next));
        }

        /// <summary>From the feedback screen: add or change the report email, then come back to the feedback.</summary>
        public void OpenReportContact()
        {
            var participant = Participant;
            if (participant == null)
                return;
            SetState(AppState.Feedback);
            m_UI.Show(new ContactScreen(participant.participantId, () =>
            {
                SetState(AppState.Feedback);
                m_UI.Show(new FeedbackScreen(m_LastReport));
            }));
        }

        public void SaveReportContact(string participantId, string name, string email)
        {
            Data.Contacts.Save(participantId, name, email);
            WriteReport(participantId);
        }

        /// <summary>Rewrites the participant's overall PDF (after every attempt, so it is ready when the headset comes off).</summary>
        void WriteReport(string participantId)
        {
            try
            {
                var file = ReportBuilder.Write(Data, participantId);
                if (file != null)
                    Debug.Log($"[RoadReady] Results report written: {System.IO.Path.Combine(Data.Contacts.Folder, file)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadReady] Could not write the results report: {e.Message}");
            }
        }

        void ShowInstructions()
        {
            SetState(AppState.Instructions);
            m_UI.Show(new InstructionsScreen());
            RoadReadyEvents.RequestInstruction(InstructionLibrary.Welcome);
        }

        public void ReplayVoice() => m_Voice?.Replay();

        public void ContinueFromInstructions()
        {
            // Flowchart: tutorial precedes the first module selection.
            var participant = Participant;
            if (participant != null && !participant.IsTutorialDone(participant.group))
                StartTutorial(participant.group);
            else
                ShowMainMenu();
        }

        public void ShowMessage(AppState state, string eyebrow, string title, string body, string button, System.Action onContinue)
        {
            SetState(state);
            m_UI.Show(new MessageScreen(eyebrow, title, body, button, onContinue));
        }

        // ================================================================ menu & attempts

        public void ShowMainMenu()
        {
            SetState(AppState.MainMenu);
            m_UI.Show(new MainMenuScreen());
        }

        public void StartTutorial(Perspective perspective)
        {
            var tutorial = m_Config.GetTutorial(perspective);
            if (tutorial == null)
            {
                ShowMainMenu();
                return;
            }

            m_ReturnToMenuAfterTutorial = true;
            Launch(new AttemptRequest { scenario = tutorial, perspective = perspective, level = 1, phase = AttemptPhase.Tutorial });
        }

        public void OpenBriefing(ScenarioDefinition scenario, Perspective perspective)
        {
            SetState(AppState.Briefing);
            m_UI.Show(new BriefingScreen(scenario, perspective));
        }

        public void OpenSafetyBriefing()
        {
            SetState(AppState.Briefing);
            m_UI.Show(new SafetyBriefingScreen());
        }

        public AttemptPhase DeterminePhase(ScenarioDefinition scenario, Perspective perspective)
        {
            if (scenario.isTutorial)
                return AttemptPhase.Tutorial;
            if (Session != null && Session.isPostTestSession)
                return AttemptPhase.PostTest;
            var participant = Participant;
            if (participant == null)
                return AttemptPhase.Training;
            return Data.GetScoredAttempts(participant.participantId, scenario.scenarioId, perspective).Count == 0 ? AttemptPhase.PreTest : AttemptPhase.Training;
        }

        /// <summary>Briefing-only comparison group: baseline and post-test only, no VR training in between.</summary>
        public bool CanAttempt(ScenarioDefinition scenario, Perspective perspective, out string reason)
        {
            reason = null;
            var participant = Participant;
            if (participant == null || participant.condition != StudyCondition.BriefingOnly)
                return true;
            var phase = DeterminePhase(scenario, perspective);
            if (phase == AttemptPhase.PreTest || phase == AttemptPhase.PostTest)
                return true;
            reason = "Briefing-only group: this scenario unlocks again in the post-test session.";
            return false;
        }

        public int GetUnlockedLevel(ScenarioDefinition scenario, Perspective perspective) =>
            Participant != null ? Mathf.Min(scenario.LevelCount, Participant.GetUnlockedLevel(scenario.scenarioId, perspective)) : 1;

        public List<AttemptRecord> GetHistory(ScenarioDefinition scenario, Perspective perspective) =>
            Participant != null ? Data.GetScoredAttempts(Participant.participantId, scenario.scenarioId, perspective) : new List<AttemptRecord>();

        public void StartScenario(ScenarioDefinition scenario, Perspective perspective, int level)
        {
            var phase = DeterminePhase(scenario, perspective);
            // Pre- and post-tests always use level 1 so measurements stay comparable.
            if (phase == AttemptPhase.PreTest || phase == AttemptPhase.PostTest)
                level = 1;
            m_ReturnToMenuAfterTutorial = false;
            Launch(new AttemptRequest { scenario = scenario, perspective = perspective, level = level, phase = phase });
        }

        void Launch(AttemptRequest request)
        {
            m_LastRequest = request;
            SelectedPerspective = request.perspective;
            SetState(AppState.InScenario);
            m_UI.HideMenu();
            m_Scenarios.StartAttempt(request);
        }

        public void Retry()
        {
            var scenario = m_LastRequest.scenario;
            if (scenario == null)
            {
                ShowMainMenu();
                return;
            }

            if (scenario.isTutorial)
                Launch(m_LastRequest);
            else if (CanAttempt(scenario, m_LastRequest.perspective, out var reason))
                StartScenario(scenario, m_LastRequest.perspective, m_LastRequest.level);
            else
                RoadReadyEvents.Toast(reason, 4f);
        }

        /// <summary>The dual-perspective step: the same scenario from the other road user's point of view.</summary>
        public void SwitchPerspective()
        {
            var scenario = m_LastRequest.scenario;
            var other = m_LastRequest.perspective.Other();
            if (scenario == null || !scenario.supportedPerspectives.Includes(other))
                return;
            if (scenario.isTutorial)
            {
                StartTutorial(other);
                return;
            }

            if (!CanAttempt(scenario, other, out var reason))
            {
                RoadReadyEvents.Toast(reason, 4f);
                return;
            }

            StartScenario(scenario, other, GetUnlockedLevel(scenario, other));
        }

        public void NextLevel()
        {
            var scenario = m_LastRequest.scenario;
            if (scenario == null)
                return;
            StartScenario(scenario, m_LastRequest.perspective, Mathf.Min(scenario.LevelCount, m_LastRequest.level + 1));
        }

        public void BackToMenu()
        {
            Time.timeScale = 1f;
            if (m_Scenarios.HasScenarioLoaded)
            {
                m_UI.HideMenu();
                SetState(AppState.Loading);
                m_Scenarios.UnloadScenario(ShowMainMenu);
            }
            else
            {
                ShowMainMenu();
            }
        }

        void OnAttemptFinished(AttemptRecord attempt, FeedbackReport report)
        {
            m_LastAttempt = attempt;
            m_LastReport = report;
            if (State == AppState.Loading || attempt.endReason == AttemptEndReason.Aborted && State != AppState.InScenario && State != AppState.Paused)
                return; // leaving the scenario - no report
            Time.timeScale = 1f;
            m_Scenarios.SetPaused(false);
            if (attempt.phase != AttemptPhase.Tutorial && attempt.endReason != AttemptEndReason.Aborted)
                SessionHooks.RaiseScoredAttempt(attempt);
            if (attempt.phase != AttemptPhase.Tutorial)
                WriteReport(attempt.participantId);

            if (m_PickedScenario != null)
            {
                var picked = m_PickedScenario;
                m_PickedScenario = null;
                if (attempt.phase == AttemptPhase.Tutorial)
                {
                    var perspective = m_LastRequest.perspective;
                    m_UI.HideMenu();
                    SetState(AppState.Loading);
                    m_Scenarios.UnloadScenario(() => OpenBriefing(picked, perspective));
                    return;
                }
            }

            if (attempt.phase == AttemptPhase.Tutorial && m_ReturnToMenuAfterTutorial && attempt.endReason == AttemptEndReason.TutorialComplete)
            {
                SetState(AppState.Feedback);
                m_UI.Show(new MessageScreen("TUTORIAL COMPLETE", "You are ready",
                    "Great - you know the controls. Next you will choose a scenario. Your first attempt at each scenario is your baseline, so just behave as you would in real life.",
                    "Choose a scenario", BackToMenu));
                return;
            }

            SetState(AppState.Feedback);
            m_UI.Show(new FeedbackScreen(report));
        }

        void OnAttemptFailed(string message)
        {
            ShowMessage(AppState.MainMenu, "SETUP PROBLEM", "Scenario could not start", message + "\nRun RoadReady > Build Everything in the editor.", "Back to menu", BackToMenu);
        }

        // ================================================================ pause

        public void Pause()
        {
            if (State != AppState.InScenario || m_Scenarios.State != AttemptState.Running)
                return;
            m_Scenarios.SetPaused(true);
            SetState(AppState.Paused);
            m_UI.Show(new PauseScreen());
        }

        /// <summary>
        /// The "Skip tutorial" button: pauses and offers the scenarios. The learner picks one (ending the tutorial and
        /// going to its briefing) or closes the panel to carry on.
        /// </summary>
        public void OpenScenarioPicker()
        {
            if (State != AppState.InScenario || m_Scenarios.State != AttemptState.Running)
                return;
            m_Scenarios.SetPaused(true);
            SetState(AppState.Paused);
            m_UI.Show(new ScenarioPickerScreen());
        }

        /// <summary>From the feedback report's close button: the list of scenarios, to choose a different one.</summary>
        public void OpenScenarioListAfterAttempt()
        {
            if (m_LastReport == null)
            {
                BackToMenu();
                return;
            }

            SetState(AppState.Feedback);
            m_UI.Show(new ScenarioPickerScreen(afterAttempt: true));
        }

        /// <summary>Back from that list to the last feedback report.</summary>
        public void ShowLastFeedback()
        {
            if (m_LastReport == null)
            {
                BackToMenu();
                return;
            }

            SetState(AppState.Feedback);
            m_UI.Show(new FeedbackScreen(m_LastReport));
        }

        public void PickScenario(ScenarioDefinition scenario)
        {
            m_PickedScenario = scenario;
            Resume();
            m_Scenarios.SkipTutorial(); // the tutorial counts as done; OnAttemptFinished carries on to the scenario
        }

        public void Resume()
        {
            if (State != AppState.Paused)
                return;
            m_UI.HideMenu();
            SetState(AppState.InScenario);
            m_Scenarios.SetPaused(false);
            m_Rig.SetPointers(false);
        }

        /// <summary>The exit button on the car door: ends the attempt after a confirmation.</summary>
        public void RequestExitCar()
        {
            if (State != AppState.InScenario || m_UI.MenuVisible)
                return;
            if (m_Scenarios.State == AttemptState.Running)
                Pause();
            m_UI.Confirm("Leave the car?", "Opening the door ends this attempt and takes you back to the menu.",
                "Leave the car", BackToMenu, () =>
                {
                    if (State == AppState.Paused)
                        Resume();
                    else
                        m_UI.HideMenu();
                }, "Stay in the car");
        }

        public void RestartAttempt()
        {
            m_Scenarios.SetPaused(false);
            SetState(AppState.Loading);
            m_UI.HideMenu();
            m_Scenarios.AbortAttempt();
            // The abort completes on the next frame; relaunch after the scene is reloaded by StartAttempt.
            StartCoroutine(RelaunchNextFrame());
        }

        System.Collections.IEnumerator RelaunchNextFrame()
        {
            yield return null;
            yield return null;
            Launch(m_LastRequest);
        }

        public void SkipTutorial()
        {
            Resume();
            m_Scenarios.SkipTutorial();
        }

        public void RecenterView()
        {
            m_Scenarios.Recenter();
            if (m_UI.MenuVisible)
                m_UI.Reposition();
        }

        // ================================================================ comfort & admin

        public void OpenComfort(System.Action onClose)
        {
            m_UI.Show(new ComfortScreen(onClose), reposition: false);
        }

        public void SaveComfort(ComfortSettings settings)
        {
            Comfort = settings;
            settings.Save();
            AudioListener.volume = settings.masterVolume;
            m_Rig.ApplyComfort(settings);
            m_Pedestrian?.ApplyComfort(settings);
            m_Vignette?.ApplyComfort(settings);
            m_Voice?.ApplyComfort(settings);
            m_UI.ApplyTextScale(settings.textScale);
        }

        public void OpenAdmin()
        {
            SetState(AppState.Admin);
            m_UI.Show(new AdminScreen());
        }

        // ================================================================ end of session

        public void FinishSession()
        {
            m_PendingQuestionnaires.Clear();
            if (m_Config.askSsqAfterEverySession)
                m_PendingQuestionnaires.Enqueue(QuestionnaireType.SSQ);
            // SUS once, after the final (post-test) session.
            if (Session != null && Session.isPostTestSession)
                m_PendingQuestionnaires.Enqueue(QuestionnaireType.SUS);

            RoadReadyEvents.RequestInstruction(InstructionLibrary.SessionEnd);
            if (m_Scenarios.HasScenarioLoaded)
            {
                m_UI.HideMenu();
                SetState(AppState.Loading);
                m_Scenarios.UnloadScenario(NextQuestionnaire);
            }
            else
            {
                NextQuestionnaire();
            }
        }

        void NextQuestionnaire()
        {
            if (m_PendingQuestionnaires.Count == 0)
            {
                ShowDebrief();
                return;
            }

            SetState(AppState.Questionnaire);
            m_UI.Show(new QuestionnaireScreen(m_PendingQuestionnaires.Dequeue()));
        }

        public void SubmitQuestionnaire(QuestionnaireType type, int[] answers)
        {
            var session = Session;
            var result = Questionnaires.Score(type, answers, session?.sessionId);
            if (session != null)
            {
                if (type == QuestionnaireType.SSQ)
                {
                    session.ssq = result;
                    session.hasSsq = true;
                    LastSsq = result;
                }
                else
                {
                    session.sus = result;
                    session.hasSus = true;
                }

                Data.SaveSession(session);
            }

            NextQuestionnaire();
        }

        void ShowDebrief()
        {
            SetState(AppState.Debrief);
            m_UI.Show(new DebriefScreen());
        }

        public void EndSession() => SessionHooks.RunBeforeLeaving(CompleteEndSession);

        void CompleteEndSession()
        {
            Data.EndSession();
            LastSsq = null;
            Data.SetCurrentParticipant(null);
            if (SchoolHandoff.ReturnHome()) // came from the school Home: finish there
                return;
            GoToSetup();
        }

        // ================================================================ withdraw

        public void RequestWithdraw()
        {
            m_UI.Confirm("Withdraw from the study?",
                "All of your RoadReady data on this headset will be permanently deleted. You can stop at any time without giving a reason.",
                "Withdraw and delete my data", Withdraw);
        }

        void Withdraw()
        {
            var participant = Participant;
            Time.timeScale = 1f;
            m_Scenarios.SetPaused(false);
            if (participant != null)
                Data.DeleteParticipant(participant.participantId);

            void ShowThanks() => ShowMessage(AppState.Excluded, "WITHDRAWN", "Your data has been deleted",
                "Thank you for your time. Please remove the headset carefully.", "Back to start", GoToSetup);

            if (m_Scenarios.HasScenarioLoaded)
            {
                m_UI.HideMenu();
                SetState(AppState.Loading);
                m_Scenarios.UnloadScenario(ShowThanks);
            }
            else
            {
                ShowThanks();
            }
        }
    }
}
