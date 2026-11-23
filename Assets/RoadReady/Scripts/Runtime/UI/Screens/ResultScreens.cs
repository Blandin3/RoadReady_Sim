using System.Linq;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Scoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>End-of-attempt feedback report (FR8) with comparison to previous attempts (FR9).</summary>
    public class FeedbackScreen : UIScreen
    {
        readonly FeedbackReport m_Report;

        public FeedbackScreen(FeedbackReport report) => m_Report = report;

        public override string TreeName => "Feedback";
        public override float ViewDistance => 1.8f; // held a little further back so the whole report is in view
        public override bool ScrollsAsWhole => true; // the whole report scrolls, so the buttons at the bottom are always reachable

        protected override void Bind()
        {
            var r = m_Report;
            var s = r.score;
            var request = App.LastRequest;

            Text("score-value", r.isTutorial ? "-" : $"{s.total:0}");
            var ring = Q<VisualElement>("score-ring");
            ring.AddToClassList(r.isTutorial ? "score-ring" : s.total >= 85 ? "score-ring--safe" : s.total >= 50 ? "score-ring--borderline" : "score-ring--unsafe");
            Text("eyebrow", $"{r.scenarioName}  |  {r.perspective.Pretty()}{(r.level > 1 ? $"  |  Level {r.level}" : "")}");
            Text("headline", r.headline);
            Text("grade", r.isTutorial ? "Not scored" : r.grade);

            var metrics = Q<VisualElement>("metrics");
            if (!r.isTutorial)
            {
                if (s.hazardsTotal > 0)
                {
                    metrics.Add(UIX.Metric("Hazards", $"{s.hazardsDetected} of {s.hazardsTotal}", s.detectionRate));
                    metrics.Add(UIX.Metric("Reaction", s.meanReactionTime >= 0f ? $"{s.meanReactionTime:0.0} s" : "-", s.reactionComponent / 100f));
                }

                metrics.Add(UIX.Metric("Rules", s.violationCount == 0 ? "no mistakes" : $"{s.violationCount} mistake(s)", s.ruleComponent / 100f));
            }

            BuildDetails();

            // Close (top right): back to the list of scenarios, to choose a different one.
            Btn("close", App.OpenScenarioListAfterAttempt);

            // Restart (next to Close): run the same scenario again from the start, same as Try again.
            var restart = Btn("restart", App.Retry);
            if (request.scenario != null && !request.scenario.isTutorial && !App.CanAttempt(request.scenario, request.perspective, out _))
                restart.SetEnabled(false);

            var menu = Btn("menu", App.BackToMenu);
            if (App.Participant != null && !r.isTutorial)
            {
                // Name + email so the researcher can send this participant their overall PDF.
                var saved = App.Data.Contacts.Get(App.Participant.participantId) != null;
                menu.parent.Insert(menu.parent.IndexOf(menu) + 1,
                    UIX.MakeButton(saved ? "Change report email" : "Email me my report", App.OpenReportContact, "btn--ghost", "btn--small"));
            }

            var retry = Btn("retry", App.Retry);
            retry.text = r.isTutorial ? "Repeat tutorial" : "Try again";
            if (request.scenario != null && !request.scenario.isTutorial && !App.CanAttempt(request.scenario, request.perspective, out _))
                retry.SetEnabled(false);

            var other = r.perspective.Other();
            var switchButton = Btn("switch", App.SwitchPerspective);
            switchButton.text = $"Now try it as a {other.Pretty().ToLowerInvariant()}";
            switchButton.SetVisible(request.scenario != null && request.scenario.supportedPerspectives.Includes(other));

            var next = Btn("next-level", App.NextLevel);
            var canLevelUp = request.scenario != null && !r.isTutorial && r.phase == AttemptPhase.Training && request.level < App.GetUnlockedLevel(request.scenario, request.perspective);
            next.SetVisible(canLevelUp || r.unlockedNextLevel && r.phase != AttemptPhase.PreTest && r.phase != AttemptPhase.PostTest);
            next.text = $"Level {request.level + 1}";
        }

        const int k_MaxMistakes = 5;

        /// <summary>Kept short on purpose: the full list of mistakes is in the report sent at the end of the session.</summary>
        void BuildDetails()
        {
            var details = Q<ScrollView>("details");
            var r = m_Report;
            if (r.isTutorial)
                return;

            if (r.unlockedNextLevel)
                details.Add(UIX.MakeLabel("Next level unlocked", "h2"));

            var mistakes = r.hazardLines.Concat(r.ruleLines)
                .Where(l => l.decision != DecisionClass.Safe)
                .OrderByDescending(l => l.decision)
                .ToList();
            if (mistakes.Count == 0)
            {
                details.Add(UIX.MakeLabel("No mistakes", "h2"));
                return;
            }

            // A pedestrian sees every mistake with the reason it can get them hurt (every one of them is a way to be hit);
            // a driver sees a short list, with the rest in the emailed report.
            if (r.perspective == Perspective.Pedestrian)
            {
                details.Add(UIX.MakeLabel("What could have got you hurt", "h2"));
                foreach (var line in mistakes)
                    details.Add(UIX.DecisionRow(line.title, !string.IsNullOrWhiteSpace(line.tip) ? line.tip : line.detail, line.decision, line.isHazard ? line.situationAwareness : null));
                return;
            }

            details.Add(UIX.MakeLabel("Mistakes", "h2"));
            foreach (var line in mistakes.Take(k_MaxMistakes))
                details.Add(UIX.DecisionRow(line.title, null, line.decision));
            if (mistakes.Count > k_MaxMistakes)
                details.Add(UIX.MakeLabel($"+ {mistakes.Count - k_MaxMistakes} more in your report", "muted", "small"));
        }
    }

    /// <summary>SSQ after each session and SUS after the final session, one item at a time (easier to point at in VR).</summary>
    public class QuestionnaireScreen : UIScreen
    {
        readonly QuestionnaireType m_Type;
        QuestionnaireDefinition m_Definition;
        int[] m_Answers;
        int m_Index;

        public QuestionnaireScreen(QuestionnaireType type) => m_Type = type;

        public override string TreeName => "Questionnaire";
        public override float ViewDistance => 1.3f;

        protected override void Bind()
        {
            m_Definition = Questionnaires.Get(m_Type);
            m_Answers = Enumerable.Repeat(-1, m_Definition.items.Length).ToArray();
            Text("eyebrow", m_Type == QuestionnaireType.SSQ ? "COMFORT CHECK" : "USABILITY");
            Text("title", m_Definition.title);
            Text("instructions", m_Definition.instructions);
            Btn("back", () =>
            {
                if (m_Index > 0)
                {
                    m_Index--;
                    Render();
                }
            });
            Render();
        }

        void Render()
        {
            Text("counter", $"{m_Index + 1} of {m_Definition.items.Length}");
            Text("item", m_Definition.items[m_Index]);
            Q<VisualElement>("progress-fill").style.width = Length.Percent(100f * m_Index / m_Definition.items.Length);
            Q<Button>("back").SetEnabled(m_Index > 0);

            var scale = Q<VisualElement>("scale");
            scale.Clear();
            for (var i = 0; i < m_Definition.scaleLabels.Length; i++)
            {
                var value = m_Definition.scaleMin + i;
                var option = UIX.MakeButton(m_Definition.scaleLabels[i], () => Answer(value), "q-option");
                if (m_Answers[m_Index] == value)
                    option.AddToClassList("btn--primary");
                scale.Add(option);
            }
        }

        void Answer(int value)
        {
            m_Answers[m_Index] = value;
            if (m_Index < m_Answers.Length - 1)
            {
                m_Index++;
                Render();
                return;
            }

            App.SubmitQuestionnaire(m_Type, m_Answers);
        }
    }

    public class DebriefScreen : UIScreen
    {
        public override string TreeName => "Debrief";
        public override float ViewDistance => 1.5f;

        protected override void Bind()
        {
            var participant = App.Participant;
            var session = App.Session;
            var attempts = participant != null && session != null
                ? App.Data.GetAttempts(participant.participantId).Where(a => a.sessionId == session.sessionId && a.phase != AttemptPhase.Tutorial).ToList()
                : new System.Collections.Generic.List<AttemptRecord>();

            var driver = attempts.Count(a => a.perspective == Perspective.Driver);
            var pedestrian = attempts.Count(a => a.perspective == Perspective.Pedestrian);
            Text("summary", $"Today you completed {attempts.Count} scenario attempt(s): {driver} as a driver and {pedestrian} as a pedestrian. Please take the headset off slowly and sit for a moment.");

            var results = Q<ScrollView>("results");
            // Only this list gives way when it is long: it takes the space left between the summary and the End session
            // button (basis 0, may shrink to nothing), so the button is always on screen and the list scrolls inside its card.
            results.style.flexGrow = 1f;
            results.style.flexShrink = 1f;
            results.style.flexBasis = 0f;
            results.style.minHeight = 0f;
            results.mode = ScrollViewMode.Vertical;
            results.verticalScrollerVisibility = ScrollerVisibility.Auto;
            foreach (var group in attempts.GroupBy(a => (a.scenarioName, a.perspective)))
            {
                var list = group.OrderBy(a => a.startUtc, System.StringComparer.Ordinal).ToList();
                var first = list.First().score.total;
                var last = list.Last().score.total;
                var decision = last >= 85 ? DecisionClass.Safe : last >= 50 ? DecisionClass.Borderline : DecisionClass.Unsafe;
                var trend = list.Count > 1 ? $"{first:0} -> {last:0} over {list.Count} attempts" : $"Score {last:0}";
                results.Add(UIX.DecisionRow($"{group.Key.scenarioName} as a {group.Key.perspective.Pretty().ToLowerInvariant()}", trend, decision));
            }

            var ssq = App.LastSsq;
            var warning = Q<Label>("ssq-warning");
            if (ssq != null && ssq.total >= App.Config.ssqWarningTotalScore)
            {
                warning.text = $"Researcher: SSQ total {ssq.total:0} (nausea {ssq.nausea:0}, disorientation {ssq.disorientation:0}). Check the participant is well before they leave and consider a shorter next session.";
                warning.SetVisible(true);
            }

            Text("takeaway", "Remember: road safety is shared. The driver who slows at the zebra and the pedestrian who waits for a real gap both prevent the same crash.");
            Btn("done", App.EndSession);
        }
    }
}
