using System;
using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>Researcher registers a new participant or picks a returning one (anonymised ids only).</summary>
    public class ParticipantSetupScreen : UIScreen
    {
        Perspective m_Group = Perspective.Driver;
        StudyCondition m_Condition = StudyCondition.RoadReadyTraining;
        AgeBand m_Age = AgeBand.Age18To24;
        DrivingExperience m_Experience = DrivingExperience.Learner;

        public override string TreeName => "ParticipantSetup";

        protected override void Bind()
        {
            Text("study-name", App.Config.studyName);
            Text("version", "RoadReady v" + Application.version);

            UIX.BuildEnumChips(Q<VisualElement>("group-chips"), m_Group, p => p == Perspective.Driver ? "Driver group" : "Pedestrian group", v => m_Group = v);
            UIX.BuildEnumChips(Q<VisualElement>("condition-chips"), m_Condition, c => c == StudyCondition.RoadReadyTraining ? "RoadReady training" : "Briefing only", v => m_Condition = v);
            UIX.BuildEnumChips(Q<VisualElement>("age-chips"), m_Age, a => a switch
            {
                AgeBand.Age16To17 => "16-17",
                AgeBand.Age18To24 => "18-24",
                AgeBand.Age25To34 => "25-34",
                _ => "35+",
            }, v => m_Age = v);
            UIX.BuildEnumChips(Q<VisualElement>("experience-chips"), m_Experience, e => e switch
            {
                DrivingExperience.None => "None",
                DrivingExperience.Learner => "Learner",
                DrivingExperience.LicensedUnder2Years => "Licensed < 2 yrs",
                _ => "Licensed 2+ yrs",
            }, v => m_Experience = v);

            Btn("create", OnCreate);
            Btn("admin", App.OpenAdmin);
            Btn("comfort", () => App.OpenComfort(App.GoToSetup));

            var list = Q<ScrollView>("participant-list");
            var postTest = Q<Toggle>("post-test");
            var participants = App.Data.LoadAllParticipants();
            Q<Label>("empty-list").SetVisible(participants.Count == 0);
            foreach (var p in participants)
            {
                if (!p.consentGiven)
                    continue;
                var id = p.participantId;
                var button = UIX.MakeButton($"{id}   {p.group.Pretty()} | {(p.condition == StudyCondition.BriefingOnly ? "Briefing" : "Training")} | {p.sessionCount} session(s)",
                    () => App.SelectReturningParticipant(id, postTest.value), "btn--ghost", "btn--small");
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                list.Add(button);
            }
        }

        void OnCreate()
        {
            var guardian = Q<Toggle>("guardian").value;
            if (m_Age == AgeBand.Age16To17 && m_Group == Perspective.Driver)
            {
                UI.Confirm("Driver group is 18+", "Learner and licensed drivers must be 18 or older (proposal 3.2.5). Register this person in the pedestrian group instead.", "OK", null, null, null);
                return;
            }

            if (m_Age == AgeBand.Age16To17 && !guardian)
            {
                UI.Confirm("Guardian consent needed", "Participants aged 16-17 need a signed guardian consent form before they can take part.", "OK", null, null, null);
                return;
            }

            App.RegisterParticipant(m_Group, m_Condition, m_Age, m_Experience, guardian);
        }
    }

    /// <summary>Informed consent (use case "Give consent"). Declining excludes and deletes the record.</summary>
    public class ConsentScreen : UIScreen
    {
        const string k_Text =
            "RoadReady is a research project at the African Leadership University. It studies whether practising road-safety decisions in virtual reality helps people make safer decisions as drivers and pedestrians.\n\n" +
            "What you will do: wear a VR headset for about 20-30 minutes, complete a short tutorial, then drive and/or walk through three street scenarios modelled on Kimironko. You may repeat scenarios and see your results.\n\n" +
            "Risks: some people feel dizzy or sick in VR. Tell the researcher immediately if you feel unwell - you can stop at any time. Nothing in the simulation can physically harm you.\n\n" +
            "Your data: RoadReady records your reactions, decisions and scores against an anonymous code (for example RR-7KQ2TX). Your name is never stored in the headset. Data is kept in line with Rwanda's Law on the Protection of Personal Data and Privacy and is used only for this study.\n\n" +
            "Your rights: taking part is voluntary. You can withdraw at any time from the menu and all of your data will be deleted.";

        public override string TreeName => "Consent";

        protected override void Bind()
        {
            Text("consent-text", k_Text);
            var agree = Btn("agree", () => App.SubmitConsent(true));
            Btn("decline", () => App.SubmitConsent(false));

            var toggles = new[] { Q<Toggle>("age-ok"), Q<Toggle>("health-ok"), Q<Toggle>("understand") };
            void Refresh()
            {
                var all = true;
                foreach (var t in toggles)
                    all &= t != null && t.value;
                agree.SetEnabled(all);
            }

            foreach (var t in toggles)
                t?.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
        }
    }

    public class MessageScreen : UIScreen
    {
        readonly string m_Eyebrow, m_Title, m_Body, m_Button;
        readonly Action m_OnContinue;

        public MessageScreen(string eyebrow, string title, string body, string button, Action onContinue)
        {
            m_Eyebrow = eyebrow;
            m_Title = title;
            m_Body = body;
            m_Button = button;
            m_OnContinue = onContinue;
        }

        public override string TreeName => "Message";

        protected override void Bind()
        {
            Text("eyebrow", m_Eyebrow);
            Text("title", m_Title);
            Text("body", m_Body);
            var button = Btn("primary", () => m_OnContinue?.Invoke());
            button.text = m_Button;
        }
    }

    public class InstructionsScreen : UIScreen
    {
        public override string TreeName => "Instructions";

        protected override void Bind()
        {
            Text("welcome", InstructionLibrary.Get(InstructionLibrary.Welcome) +
                            " Each scenario can be played as a driver and as a pedestrian - trying both shows you what the other road user sees.");
            Text("driver-controls", InstructionLibrary.Get(InstructionLibrary.ControlsDriver).Replace(".  ", ".\n"));
            Text("pedestrian-controls", InstructionLibrary.Get(InstructionLibrary.ControlsPedestrian).Replace(".  ", ".\n"));
            Btn("replay", App.ReplayVoice);
            Btn("continue", App.ContinueFromInstructions);
        }
    }
}
