using System;
using System.Linq;
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

            // A plain list (not a ScrollView, which swallows VR controller presses): the six most recent participants.
            var list = Q<VisualElement>("participant-list");
            var postTest = Q<Toggle>("post-test");
            var participants = App.Data.LoadAllParticipants().Where(p => p.consentGiven).ToList();
            Q<Label>("empty-list").SetVisible(participants.Count == 0);
            foreach (var p in participants.Skip(Mathf.Max(0, participants.Count - 6)))
            {
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
            "Your data: RoadReady records your reactions, decisions and scores against an anonymous code (for example RR-7KQ2TX). Your name is never stored with it. If you choose to leave a name and email so we can send you your results report, they are kept in a separate file, never with your study data, and used only to send you that report. Data is kept in line with Rwanda's Law on the Protection of Personal Data and Privacy and is used only for this study.\n\n" +
            "Your rights: taking part is voluntary. You can withdraw at any time from the menu and all of your data will be deleted.";

        public override string TreeName => "Consent";

        protected override void Bind()
        {
            // One label per paragraph so each gets its own spacing; the "Lead-in:" of each paragraph is bold.
            var holder = Q<Label>("consent-text");
            var container = holder.parent;
            holder.RemoveFromHierarchy();
            foreach (var paragraph in k_Text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = paragraph.IndexOf(": ", StringComparison.Ordinal);
                var text = colon > 0 && colon < 24
                    ? $"<b>{paragraph.Substring(0, colon + 1)}</b>{paragraph.Substring(colon + 1)}"
                    : paragraph;
                container.Add(UIX.MakeLabel(text, "body", "consent__para"));
            }
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

    /// <summary>
    /// Optional: the participant leaves a name and an e-mail address so the researcher can send them an overall PDF
    /// of how they did. People tend to take the headset off before the on-screen feedback, so this is asked up front.
    /// The details are kept apart from the study data (see <see cref="Data.ContactStore"/>). A small card; the keyboard
    /// only appears once a box is pointed at. Built in code (no UXML to register).
    /// </summary>
    public class ContactScreen : UIScreen
    {
        readonly string m_ParticipantId;
        readonly Action m_OnDone;
        TypingBox m_Name, m_Email;
        OnScreenKeyboard m_Keyboard;
        Label m_Message;

        public ContactScreen(string participantId, Action onDone)
        {
            m_ParticipantId = participantId;
            m_OnDone = onDone;
        }

        public override string TreeName => "";
        public override bool Floating => true;

        protected override void Bind()
        {
            var existing = App.Data.Contacts.Get(m_ParticipantId);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;

            var card = new VisualElement();
            card.AddToClassList("contact");
            Root.Add(card);

            // The three flag colours along the top of the card.
            var bar = new VisualElement();
            bar.AddToClassList("contact__bar");
            foreach (var colour in new[] { "contact__bar--blue", "contact__bar--yellow", "contact__bar--green" })
            {
                var segment = new VisualElement();
                segment.AddToClassList(colour);
                bar.Add(segment);
            }

            card.Add(bar);
            card.Add(UIX.MakeLabel("Want your results by email?", "contact__title"));
            card.Add(UIX.MakeLabel("Optional. We send you a PDF of how you did, kept apart from the study data.", "contact__hint"));

            m_Name = Box(card, "Your name", "Tap here to type your name", existing?.name);
            m_Email = Box(card, "Your email", "Tap here to type your email", existing?.email);

            m_Keyboard = new OnScreenKeyboard();
            m_Keyboard.SetVisible(false); // only once a box is pointed at
            card.Add(m_Keyboard);
            m_Message = UIX.MakeLabel("", "contact__message");
            card.Add(m_Message);

            var buttons = new VisualElement();
            buttons.AddToClassList("row");
            buttons.Add(UIX.MakeButton("No thanks", () => m_OnDone?.Invoke(), "btn--ghost", "btn--small"));
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            buttons.Add(spacer);
            buttons.Add(UIX.MakeButton("Save and continue", Save, "btn--primary", "btn--small"));
            card.Add(buttons);
        }

        TypingBox Box(VisualElement card, string label, string placeholder, string value)
        {
            card.Add(UIX.MakeLabel(label, "contact__label"));
            var box = new TypingBox(placeholder) { Value = value ?? "" };
            box.Clicked += () => Select(box);
            card.Add(box);
            return box;
        }

        void Select(TypingBox box)
        {
            m_Keyboard.Target = box;
            m_Keyboard.AutoCapitalise = box == m_Name;
            m_Keyboard.SetCapitals(box == m_Name && (string.IsNullOrEmpty(box.Value) || box.Value.EndsWith(" ")));
            m_Name.Active = box == m_Name;
            m_Email.Active = box == m_Email;
            m_Keyboard.SetVisible(true);
            m_Message.text = "";
        }

        void Save()
        {
            var name = m_Name.Value.Trim();
            var email = m_Email.Value.Trim();
            if (name.Length == 0)
            {
                m_Message.text = "Please type your name.";
                Select(m_Name);
                return;
            }

            if (!RoadReady.Data.ContactStore.IsValidEmail(email))
            {
                m_Message.text = "That email does not look right (name@example.com).";
                Select(m_Email);
                return;
            }

            App.SaveReportContact(m_ParticipantId, name, email);
            m_OnDone?.Invoke();
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
