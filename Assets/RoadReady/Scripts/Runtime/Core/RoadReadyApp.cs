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

            m_Rig.ReturnToLobby();
            StartCoroutine(m_Fader.FadeTo(0f, 0.8f));
            GoToSetup();
        }

        void Update()
        {
            if (RoadReadyInput.Instance.Pause.WasPressedThisFrame())
            {
                if (State == AppState.InScenario)
                    Pause();
                else if (State == AppState.Paused)
                    Resume();
            }

            if (State != AppState.InScenario && RoadReadyInput.Instance.Recenter.WasPressedThisFrame() && m_UI.MenuVisible)
                m_UI.Reposition();
        }

        void SetState(AppState state)
        {
            var previous = State;
            State = state;
            RoadReadyEvents.RaiseAppStateChanged(previous, state);
        }

        // ================================================================ participant & consent

        public void GoToSetup()
        {
            SetState(AppState.ParticipantSetup);
            m_UI.Show(new ParticipantSetupScreen());
        }

        public void RegisterParticipant(Perspective group, StudyCondition condition, AgeBand age, DrivingExperience experience, bool guardianConsent)
        {
            Data.CreateParticipant(group, condition, age, experience, guardianConsent);
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
            ShowInstructions();
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

        public void Resume()
        {
            if (State != AppState.Paused)
                return;
            m_UI.HideMenu();
            SetState(AppState.InScenario);
            m_Scenarios.SetPaused(false);
            m_Rig.SetPointers(false);
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

        public void EndSession()
        {
            Data.EndSession();
            LastSsq = null;
            Data.SetCurrentParticipant(null);
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
