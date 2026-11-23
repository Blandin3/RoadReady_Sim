using System;
using System.Linq;
using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// Participant setup. The learner's details (group, condition, age band, experience) are saved against their name or
    /// member code the first time; when they come back the same details are filled in and they only approve or edit them.
    /// The list on the right is for picking an earlier study record directly.
    /// </summary>
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

            // Details saved earlier for this learner are filled in: they only approve them, or change what is different.
            var saved = App.FindLearnerProfile();
            if (saved != null)
            {
                m_Group = saved.group;
                m_Condition = saved.condition;
                m_Age = saved.ageBand;
                m_Experience = saved.drivingExperience;
            }

            var who = string.IsNullOrWhiteSpace(App.LearnerName) ? "" : " " + App.LearnerName;
            var hint = Q<Label>("profile-hint");
            hint.text = saved != null
                ? $"Welcome back{who}. These are your saved details: change anything that is wrong, then confirm."
                : string.IsNullOrWhiteSpace(App.LearnerName) ? "" : $"Hello{who}. Choose your details once; they are saved for next time.";
            hint.SetVisible(!string.IsNullOrEmpty(hint.text));

            var guardian = Q<Toggle>("guardian");
            guardian.value = saved != null && saved.guardianConsentVerified;

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

            var postTest = Q<Toggle>("post-test");
            Btn("create", () => OnConfirm(guardian.value, postTest.value));
            Btn("back", App.ShowWelcome);
            Btn("comfort", () => App.OpenComfort(App.GoToSetup));
            Btn("admin", App.OpenAdmin); // researcher panel (asks for the PIN)

            // A plain list (not a ScrollView, which swallows VR controller presses): the six most recent participants.
            var list = Q<VisualElement>("participant-list");
            var participants = App.Data.LoadAllParticipants().Where(p => p.consentGiven).ToList();
            var empty = Q<Label>("empty-list");
            var card = Q<VisualElement>("returning-card");

            // Search (top-left of the card): matches the participant code or the name left for the report.
            // Typed with the on-screen keyboard, which opens when the box is pointed at.
            var searchRow = Q<VisualElement>("search-row");
            searchRow.Insert(0, BuildSearchIcon());
            var search = new TypingBox("Search by code or name");
            search.style.flexGrow = 1;
            search.style.marginLeft = 10;
            searchRow.Add(search);
            var keyboard = new OnScreenKeyboard(false) { AutoCapitalise = false };
            keyboard.SetVisible(false);
            keyboard.Target = search;
            Q<VisualElement>("search-keyboard").Add(keyboard);
            search.Clicked += () =>
            {
                search.Active = true;
                keyboard.SetVisible(true);
                if (card != null) card.style.width = 880; // wide enough for the keyboard
            };

            void Render()
            {
                list.Clear();
                var query = search.Value.Trim();
                var matches = participants.Where(p => query.Length == 0 || Matches(p.participantId, query)).ToList();
                empty.text = participants.Count == 0 ? "No participants registered on this headset yet." : "No participant matches that search.";
                empty.SetVisible(matches.Count == 0);
                // The six most recent (or the six most recent matches).
                foreach (var p in matches.Skip(Mathf.Max(0, matches.Count - 6)))
                {
                    var id = p.participantId;
                    var button = UIX.MakeButton($"{id}   {p.group.Pretty()} | {(p.condition == StudyCondition.BriefingOnly ? "Briefing" : "Training")} | {p.sessionCount} session(s)",
                        () => App.SelectReturningParticipant(id, postTest.value), "btn--ghost", "btn--small");
                    button.style.unityTextAlign = TextAnchor.MiddleLeft;
                    list.Add(button);
                }
            }

            search.Changed += Render;
            Render();
        }

        bool Matches(string participantId, string query)
        {
            if (participantId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var contact = App.Data.Contacts.Get(participantId);
            return contact != null && !string.IsNullOrEmpty(contact.name) && contact.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A magnifying-glass icon drawn from two shapes (no font or image needed).</summary>
        internal static VisualElement BuildSearchIcon()
        {
            var icon = new VisualElement();
            icon.pickingMode = PickingMode.Ignore;
            icon.style.width = 44;
            icon.style.height = 44;
            var lens = new VisualElement();
            lens.pickingMode = PickingMode.Ignore;
            lens.style.position = Position.Absolute;
            lens.style.left = 4;
            lens.style.top = 4;
            lens.style.width = 26;
            lens.style.height = 26;
            lens.style.borderTopLeftRadius = lens.style.borderTopRightRadius = lens.style.borderBottomLeftRadius = lens.style.borderBottomRightRadius = 13;
            lens.style.borderTopWidth = lens.style.borderRightWidth = lens.style.borderBottomWidth = lens.style.borderLeftWidth = 4;
            lens.style.borderTopColor = lens.style.borderRightColor = lens.style.borderBottomColor = lens.style.borderLeftColor = Color.white;
            var handle = new VisualElement();
            handle.pickingMode = PickingMode.Ignore;
            handle.style.position = Position.Absolute;
            handle.style.left = 26;
            handle.style.top = 33;
            handle.style.width = 16;
            handle.style.height = 5;
            handle.style.backgroundColor = Color.white;
            handle.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            icon.Add(lens);
            icon.Add(handle);
            return icon;
        }

        void OnConfirm(bool guardian, bool postTest)
        {
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

            App.ConfirmParticipant(m_Group, m_Condition, m_Age, m_Experience, guardian, postTest);
        }
    }

    /// <summary>Informed consent (use case "Give consent"). Declining excludes and deletes the record.</summary>
    public class ConsentScreen : UIScreen
    {
        const string k_Text =
            "About: RoadReady is a virtual-reality app from the African Leadership University for practising road safety. You will drive or walk through street scenes modelled on Kigali.\n\n" +
            "Risks: VR can make some people dizzy. Tell the instructor if you feel unwell and stop at once. Nothing in the simulation can hurt you.\n\n" +
            "Your data: we save your decisions and scores to give you feedback. If you give an email, it is used only to send you your report. Your data is not shared with anyone else.\n\n" +
            "Your choice: taking part is voluntary. You can stop or withdraw at any time and your data will be deleted.";

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

            // The learner already typed their name on the landing page: reuse it, so it is only asked for once.
            var knownName = !string.IsNullOrWhiteSpace(existing?.name) ? existing.name : App.LearnerName;
            if (!string.IsNullOrWhiteSpace(knownName))
                m_Name = new TypingBox("") { Value = knownName.Trim() }; // kept, but not shown
            else
                m_Name = Box(card, "Your name", "Tap here to type your name", null);
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
