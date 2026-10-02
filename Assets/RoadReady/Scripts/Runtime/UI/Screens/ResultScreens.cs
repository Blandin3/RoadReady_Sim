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

        protected override void Bind()
        {
            var r = m_Report;
            var s = r.score;
            var request = App.LastRequest;

            Text("score-value", r.isTutorial ? "-" : $"{s.total:0}");
            var ring = Q<VisualElement>("score-ring");
            ring.AddToClassList(r.isTutorial ? "score-ring" : s.total >= 85 ? "score-ring--safe" : s.total >= 50 ? "score-ring--borderline" : "score-ring--unsafe");
            Text("eyebrow", $"{r.scenarioName.ToUpperInvariant()}  |  {r.perspective.Pretty().ToUpperInvariant()}  |  {r.phase.Pretty()}{(r.level > 1 ? $"  |  LEVEL {r.level}" : "")}");
            Text("headline", r.headline);
            Text("grade", r.isTutorial ? "Tutorials are not scored." : $"{r.grade}  -  attempt {r.attemptNumber}");

            var metrics = Q<VisualElement>("metrics");
            if (!r.isTutorial)
            {
                if (s.hazardsTotal > 0)
                {
                    metrics.Add(UIX.Metric("Hazards handled safely", $"{s.hazardsDetected} of {s.hazardsTotal}", s.detectionRate));
                    metrics.Add(UIX.Metric("Reaction time", s.meanReactionTime >= 0f ? $"{s.meanReactionTime:0.0} s average" : "no responses", s.reactionComponent / 100f));
                }

                metrics.Add(UIX.Metric("Rule compliance", s.violationCount == 0 ? "no violations" : $"{s.violationCount} violation(s)", s.ruleComponent / 100f));
            }

            BuildHistory();
            BuildDetails();

            Btn("menu", App.BackToMenu);
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

        void BuildHistory()
        {
            var history = Q<VisualElement>("history");
            var values = m_Report.history;
            Q<Label>("history-title").SetVisible(values.Count > 1 && !m_Report.isTutorial);
            if (values.Count <= 1 || m_Report.isTutorial)
                return;
            for (var i = 0; i < values.Count; i++)
            {
                var bar = new VisualElement();
                bar.AddToClassList("history__bar");
                if (i == values.Count - 1)
                    bar.AddToClassList("history__bar--current");
                bar.style.height = Length.Percent(Mathf.Max(6f, values[i]));
                bar.Add(UIX.MakeLabel($"{values[i]:0}", "history__value"));
                history.Add(bar);
            }
        }

        void BuildDetails()
        {
            var details = Q<ScrollView>("details");
            var r = m_Report;

            if (r.unlockedNextLevel)
                details.Add(UIX.MakeLabel("New difficulty level unlocked - more traffic and less time to react.", "insight", "insight--swap"));

            if (r.hazardLines.Count > 0)
            {
                details.Add(UIX.MakeLabel("Hazards", "h2"));
                foreach (var line in r.hazardLines)
                    details.Add(UIX.DecisionRow(line.title, line.detail, line.decision, line.situationAwareness));
            }

            if (!string.IsNullOrEmpty(r.awarenessInsight))
                details.Add(UIX.MakeLabel(r.awarenessInsight, "insight"));

            if (!string.IsNullOrEmpty(r.otherPerspectiveInsight))
                details.Add(UIX.MakeLabel($"Seen from the other side: {r.otherPerspectiveInsight}", "insight", "insight--swap"));

            if (r.ruleLines.Count > 0)
            {
                details.Add(UIX.MakeLabel("Your decisions", "h2"));
                foreach (var line in r.ruleLines.OrderByDescending(l => l.decision))
                    details.Add(UIX.DecisionRow(line.title, line.detail, line.decision));
            }

            if (r.tips.Count > 0)
            {
                details.Add(UIX.MakeLabel("Next time", "h2"));
                foreach (var tip in r.tips)
                    details.Add(UIX.MakeLabel("-  " + tip, "body"));
            }
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
