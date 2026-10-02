using System;
using System.Collections.Generic;
using System.Linq;
using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>Module and scenario selection (FR1) with baseline / training / post-test status and history (FR9).</summary>
    public class MainMenuScreen : UIScreen
    {
        public override string TreeName => "MainMenu";

        protected override void Bind()
        {
            var participant = App.Participant;
            var session = App.Session;
            Text("participant-id", participant != null ? participant.participantId : "Demo mode");
            Text("session-info", session != null
                ? $"Session {session.sessionNumber}  |  {(session.isPostTestSession ? "POST-TEST session" : "Baseline and training")}  |  {(participant.condition == StudyCondition.BriefingOnly ? "Briefing-only group" : "RoadReady training group")}"
                : "No participant selected - data will not be saved");

            Btn("perspective-driver", () => SetPerspective(Perspective.Driver));
            Btn("perspective-pedestrian", () => SetPerspective(Perspective.Pedestrian));
            Btn("tutorial", () => App.StartTutorial(App.SelectedPerspective));
            var briefing = Btn("briefing", App.OpenSafetyBriefing);
            briefing.SetVisible(participant != null && participant.condition == StudyCondition.BriefingOnly);
            Btn("comfort", () => App.OpenComfort(App.ShowMainMenu));
            Btn("withdraw", App.RequestWithdraw);
            Btn("finish", () => UI.Confirm("Finish this session?", "You will answer a few short questions about how you feel.", "Finish session", App.FinishSession));

            Refresh();
        }

        void SetPerspective(Perspective perspective)
        {
            App.SelectedPerspective = perspective;
            Refresh();
        }

        void Refresh()
        {
            var perspective = App.SelectedPerspective;
            Q<Button>("perspective-driver").EnableInClassList("chip--selected", perspective == Perspective.Driver);
            Q<Button>("perspective-pedestrian").EnableInClassList("chip--selected", perspective == Perspective.Pedestrian);

            var session = App.Session;
            Text("phase-hint", session != null && session.isPostTestSession
                ? "Post-test: your first attempt at each scenario today is measured. Take your time and behave as you would in real life."
                : "Your first attempt at each scenario is your baseline. After that, practise as often as you like and compare your scores.");

            var cards = Q<VisualElement>("scenario-cards");
            cards.Clear();
            foreach (var scenario in App.Config.scenarios)
            {
                if (scenario == null || !scenario.supportedPerspectives.Includes(perspective))
                    continue;
                cards.Add(BuildCard(scenario, perspective));
            }
        }

        VisualElement BuildCard(ScenarioDefinition scenario, Perspective perspective)
        {
            var history = App.GetHistory(scenario, perspective);
            var phase = App.DeterminePhase(scenario, perspective);
            var canAttempt = App.CanAttempt(scenario, perspective, out var reason);
            var level = App.GetUnlockedLevel(scenario, perspective);

            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList("scenario-card");
            card.AddToClassList("card--interactive");
            if (phase == AttemptPhase.PreTest || phase == AttemptPhase.PostTest)
                card.AddToClassList("card--highlight");

            var thumb = new VisualElement();
            thumb.AddToClassList("scenario-card__thumb");
            if (scenario.thumbnail != null)
                thumb.style.backgroundImage = new StyleBackground(Background.FromSprite(scenario.thumbnail));
            card.Add(thumb);
            card.Add(UIX.MakeLabel(scenario.displayName, "card__title"));
            card.Add(UIX.MakeLabel(scenario.location, "card__meta"));

            var badges = new VisualElement();
            badges.AddToClassList("row");
            badges.AddToClassList("row--wrap");
            var phaseBadge = UIX.MakeLabel(phase == AttemptPhase.PreTest ? "BASELINE" : phase == AttemptPhase.PostTest ? "POST-TEST" : "TRAINING", "badge");
            phaseBadge.AddToClassList(phase == AttemptPhase.PreTest ? "badge--pre" : phase == AttemptPhase.PostTest ? "badge--post" : "badge");
            badges.Add(phaseBadge);
            badges.Add(UIX.MakeLabel($"LEVEL {level}/{scenario.LevelCount}", "badge", "badge--level"));
            card.Add(badges);

            if (history.Count > 0)
            {
                var best = history.Max(a => a.score.total);
                var last = history[history.Count - 1].score.total;
                card.Add(UIX.MakeLabel($"{history.Count} attempt(s)  |  last {last:0}  |  best {best:0}", "card__meta"));
            }
            else
            {
                card.Add(UIX.MakeLabel("Not attempted yet", "card__meta"));
            }

            if (!canAttempt)
            {
                card.SetEnabled(false);
                card.Add(UIX.MakeLabel(reason, "card__meta"));
            }
            else
            {
                card.AddManipulator(new Clickable(() => App.OpenBriefing(scenario, perspective)));
            }

            return card;
        }
    }

    /// <summary>Scenario briefing before an attempt, with difficulty selection (difficulty progression, RQ2).</summary>
    public class BriefingScreen : UIScreen
    {
        readonly ScenarioDefinition m_Scenario;
        readonly Perspective m_Perspective;
        int m_Level;

        public BriefingScreen(ScenarioDefinition scenario, Perspective perspective)
        {
            m_Scenario = scenario;
            m_Perspective = perspective;
        }

        public override string TreeName => "Briefing";

        protected override void Bind()
        {
            var phase = App.DeterminePhase(m_Scenario, m_Perspective);
            var unlocked = App.GetUnlockedLevel(m_Scenario, m_Perspective);
            m_Level = phase == AttemptPhase.Training ? unlocked : 1;

            Text("phase", phase.Pretty());
            Text("scenario-name", m_Scenario.displayName);
            Text("meta", $"{m_Scenario.location}  |  as a {m_Perspective.Pretty().ToLowerInvariant()}");
            var briefing = m_Scenario.GetBriefing(m_Perspective);
            Text("briefing", string.IsNullOrEmpty(briefing) ? m_Scenario.description : briefing);
            Text("controls", InstructionLibrary.Get(m_Perspective == Perspective.Driver ? InstructionLibrary.ControlsDriver : InstructionLibrary.ControlsPedestrian).Replace(".  ", ".\n"));

            var levels = new List<int>();
            var maxSelectable = phase == AttemptPhase.Training ? unlocked : 1;
            for (var i = 1; i <= maxSelectable; i++)
                levels.Add(i);
            UIX.BuildChips(Q<VisualElement>("level-chips"), levels, l => $"Level {l}", m_Level, l => m_Level = l);

            Btn("back", App.ShowMainMenu);
            Btn("start", () => App.StartScenario(m_Scenario, m_Perspective, m_Level));
        }
    }

    /// <summary>
    /// Gerayo Amahoro-style verbal safety briefing for the briefing-only comparison group (proposal 3.2.2).
    /// </summary>
    public class SafetyBriefingScreen : UIScreen
    {
        static readonly (string title, string body)[] k_Pages =
        {
            ("Road safety is shared", "Every crash involves more than one decision. Drivers, moto-taxi riders, cyclists and pedestrians all share the road - and the responsibility for arriving safely."),
            ("At a zebra crossing", "Pedestrians have priority on a zebra crossing. Drivers must slow down early and stop for anyone waiting or crossing.\n\nPedestrians: make eye contact and make sure vehicles are actually stopping before you step out."),
            ("At traffic signals", "Drivers: red means stop behind the line. Amber means stop unless you are too close to stop safely.\n\nPedestrians: cross only on the green figure. Do not start when it is flashing."),
            ("At junctions", "Give way to traffic on the main road. Wait for a gap of at least three seconds.\n\nLook twice for moto-taxis - they are small, fast and often filter between vehicles."),
            ("Before you cross", "Stop at the kerb. Look left, right and left again - in Rwanda the nearest traffic comes from your left. Keep looking while you cross and do not use your phone."),
            ("Speed and space", "Keep to the speed limit - 40 km/h in town unless signs say less. Leave a two-second gap to the vehicle ahead: moto-taxis stop suddenly to pick up passengers."),
        };

        int m_Page;

        public override string TreeName => "SafetyBriefing";

        protected override void Bind()
        {
            Btn("back", () =>
            {
                if (m_Page == 0) App.ShowMainMenu();
                else { m_Page--; Render(); }
            });
            Btn("next", () =>
            {
                if (m_Page >= k_Pages.Length - 1) App.ShowMainMenu();
                else { m_Page++; Render(); }
            });
            Render();
        }

        void Render()
        {
            Text("title", k_Pages[m_Page].title);
            Text("body", k_Pages[m_Page].body);
            Text("counter", $"{m_Page + 1} / {k_Pages.Length}");
            Q<Button>("next").text = m_Page >= k_Pages.Length - 1 ? "Done" : "Next";
        }
    }

    public class PauseScreen : UIScreen
    {
        public override string TreeName => "Pause";

        protected override void Bind()
        {
            var request = App.Scenarios.Request;
            Text("context", request.scenario != null ? $"{request.scenario.displayName}  |  {request.perspective.Pretty()}  |  {request.phase.Pretty()}" : "");
            Btn("resume", App.Resume);
            Btn("recenter", App.RecenterView);
            Btn("restart", App.RestartAttempt);
            var skip = Btn("skip-tutorial", App.SkipTutorial);
            skip.SetVisible(request.phase == AttemptPhase.Tutorial);
            Btn("comfort", () => App.OpenComfort(() => UI.Show(new PauseScreen(), reposition: false)));
            Btn("quit", App.BackToMenu);
            Btn("withdraw", App.RequestWithdraw);
        }
    }

    /// <summary>NFR safety and comfort options + accessibility text size.</summary>
    public class ComfortScreen : UIScreen
    {
        readonly Action m_OnClose;
        ComfortSettings m_Settings;

        public ComfortScreen(Action onClose) => m_OnClose = onClose;

        public override string TreeName => "Comfort";

        protected override void Bind()
        {
            m_Settings = App.Comfort.Clone();
            BindToggle("vignette", m_Settings.vignetteEnabled, v => m_Settings.vignetteEnabled = v);
            BindSlider("vignette-strength", m_Settings.vignetteStrength, v => m_Settings.vignetteStrength = v, "0%", 1f);
            BindToggle("smooth-turn", m_Settings.turnMode == TurnMode.Smooth, v => m_Settings.turnMode = v ? TurnMode.Smooth : TurnMode.Snap);
            BindSlider("snap-degrees", m_Settings.snapTurnDegrees, v => m_Settings.snapTurnDegrees = Mathf.Round(v / 15f) * 15f, "0 deg", 1f);
            BindToggle("assisted-crossing", m_Settings.assistedCrossing, v => m_Settings.assistedCrossing = v);
            BindToggle("free-walking", m_Settings.freeWalking, v => m_Settings.freeWalking = v);
            BindSlider("walk-speed", m_Settings.walkSpeed, v => m_Settings.walkSpeed = v, "0.0 m/s", 1f);
            BindToggle("seated", m_Settings.seatedMode, v => m_Settings.seatedMode = v);
            BindSlider("eye-height", m_Settings.seatedEyeHeight, v => m_Settings.seatedEyeHeight = v, "0.00 m", 1f);
            BindSlider("text-scale", m_Settings.textScale, v => m_Settings.textScale = v, "0%", 1f);
            BindSlider("volume", m_Settings.masterVolume, v => m_Settings.masterVolume = v, "0%", 1f);
            BindToggle("voice", m_Settings.voiceInstructions, v => m_Settings.voiceInstructions = v);

            Btn("back", () => m_OnClose?.Invoke());
            Btn("save", () =>
            {
                App.SaveComfort(m_Settings);
                m_OnClose?.Invoke();
            });
        }

        void BindToggle(string name, bool value, Action<bool> set)
        {
            var toggle = Q<Toggle>(name);
            if (toggle == null) return;
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
        }

        void BindSlider(string name, float value, Action<float> set, string format, float displayMultiplier)
        {
            var slider = Q<Slider>(name);
            if (slider == null) return;
            var baseLabel = slider.label;
            slider.SetValueWithoutNotify(value);
            slider.label = $"{baseLabel}: {(value * displayMultiplier).ToString(format)}";
            slider.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                slider.label = $"{baseLabel}: {(e.newValue * displayMultiplier).ToString(format)}";
            });
        }
    }
}
