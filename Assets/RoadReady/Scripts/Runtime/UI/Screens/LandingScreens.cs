using System;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// First page after the splash. A learner only types a name (it goes on the driving school's list for the day);
    /// the driving school's own account logs in below, or creates itself the very first time the app is used.
    /// Built in code, like <see cref="ContactScreen"/>.
    /// </summary>
    public class LandingScreen : UIScreen
    {
        TypingBox m_Name, m_User, m_Password;
        OnScreenKeyboard m_Keyboard;
        Label m_Message;
        bool m_SchoolMode;

        public override string TreeName => "";
        public override bool Floating => true;

        protected override void Bind() => Build();

        void Build()
        {
            Root.Clear();
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;

            var card = new VisualElement();
            card.AddToClassList("contact");
            Root.Add(card);
            card.Add(FlagBar());

            if (m_SchoolMode)
                BuildSchool(card);
            else
                BuildLearner(card);

            m_Keyboard = new OnScreenKeyboard(!m_SchoolMode);
            m_Keyboard.SetVisible(false); // only once a box is pointed at
            card.Add(m_Keyboard);
            m_Message = UIX.MakeLabel("", "contact__message");
            card.Add(m_Message);
        }

        void BuildLearner(VisualElement card)
        {
            card.Add(UIX.MakeLabel("Welcome to RoadReady", "contact__title"));
            card.Add(UIX.MakeLabel("Type your name so your driving school knows you took part today.", "contact__hint"));
            m_Name = Box(card, "Your name", "Tap here to type your name", false);

            var buttons = Row(card);
            buttons.Add(UIX.MakeButton("Driving school login", () => { m_SchoolMode = true; Build(); }, "btn--ghost", "btn--small"));
            buttons.Add(UIX.MakeButton("Register school", OpenSchoolRegistration, "btn--ghost", "btn--small"));
            buttons.Add(Spacer());
            buttons.Add(UIX.MakeButton("Start", StartLearner, "btn--primary", "btn--small"));
        }

        /// <summary>Opens the online school account screens (Register school / School login, in the Accounts scenes).</summary>
        void OpenSchoolRegistration()
        {
            const string scene = "SchoolSetup";
            if (!UnityEngine.Application.CanStreamedLevelBeLoaded(scene))
            {
                m_Message.text = "The school accounts scenes are missing from Build Settings. Run RoadReady > Accounts > Add Account Scenes To Build Settings.";
                return;
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }

        void BuildSchool(VisualElement card)
        {
            var create = !App.Data.School.HasAccount;
            card.Add(UIX.MakeLabel(create ? "Set up the driving school account" : "Driving school login", "contact__title"));
            card.Add(UIX.MakeLabel(create
                ? $"Choose a username and a password (at least {RoadReady.Data.SchoolStore.MinPasswordLength} characters). Only this account can see the learner list."
                : "Log in to see the learners who used RoadReady today.", "contact__hint"));
            m_User = Box(card, "Username", "Tap here to type the username", false);
            m_Password = Box(card, "Password", "Tap here to type the password", true);

            var buttons = Row(card);
            buttons.Add(UIX.MakeButton("Back", () => { m_SchoolMode = false; Build(); }, "btn--ghost", "btn--small"));
            buttons.Add(Spacer());
            buttons.Add(UIX.MakeButton(create ? "Create account" : "Log in", () => LogIn(create), "btn--primary", "btn--small"));
        }

        static VisualElement FlagBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("contact__bar");
            foreach (var colour in new[] { "contact__bar--blue", "contact__bar--yellow", "contact__bar--green" })
            {
                var segment = new VisualElement();
                segment.AddToClassList(colour);
                bar.Add(segment);
            }

            return bar;
        }

        static VisualElement Row(VisualElement card)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            card.Add(row);
            return row;
        }

        static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            return spacer;
        }

        TypingBox Box(VisualElement card, string label, string placeholder, bool masked)
        {
            card.Add(UIX.MakeLabel(label, "contact__label"));
            var box = new TypingBox(placeholder) { Masked = masked };
            box.Clicked += () => Select(box);
            card.Add(box);
            return box;
        }

        void Select(TypingBox box)
        {
            m_Keyboard.Target = box;
            m_Keyboard.AutoCapitalise = box == m_Name;
            m_Keyboard.SetCapitals(box == m_Name && (string.IsNullOrEmpty(box.Value) || box.Value.EndsWith(" ")));
            if (m_Name != null) m_Name.Active = box == m_Name;
            if (m_User != null) m_User.Active = box == m_User;
            if (m_Password != null) m_Password.Active = box == m_Password;
            m_Keyboard.SetVisible(true);
            m_Message.text = "";
        }

        void StartLearner()
        {
            var name = m_Name.Value.Trim();
            if (name.Length == 0)
            {
                m_Message.text = "Please type your name.";
                Select(m_Name);
                return;
            }

            App.LearnerStarted(name);
        }

        void LogIn(bool create)
        {
            var user = m_User.Value.Trim();
            var password = m_Password.Value;
            if (create)
            {
                if (user.Length == 0 || password.Length < RoadReady.Data.SchoolStore.MinPasswordLength)
                {
                    m_Message.text = $"Type a username and a password of at least {RoadReady.Data.SchoolStore.MinPasswordLength} characters.";
                    return;
                }

                App.Data.School.CreateAccount(user, password);
                App.ShowSchoolLearners();
                return;
            }

            if (!App.Data.School.Verify(user, password))
            {
                m_Message.text = "Wrong username or password.";
                m_Password.Value = "";
                return;
            }

            App.ShowSchoolLearners();
        }
    }

    /// <summary>The driving school's view after logging in: the learners who used RoadReady today.</summary>
    public class SchoolLearnersScreen : UIScreen
    {
        const int k_MaxRows = 10;

        public override string TreeName => "";
        public override bool Floating => true;

        protected override void Bind()
        {
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;

            var card = new VisualElement();
            card.AddToClassList("contact");
            Root.Add(card);

            var bar = new VisualElement();
            bar.AddToClassList("contact__bar");
            foreach (var colour in new[] { "contact__bar--blue", "contact__bar--yellow", "contact__bar--green" })
            {
                var segment = new VisualElement();
                segment.AddToClassList(colour);
                bar.Add(segment);
            }

            card.Add(bar);

            var today = DateTime.Now;
            var learners = App.Data.School.LearnersOn(today);
            card.Add(UIX.MakeLabel($"Learners today - {today:dddd d MMMM}", "contact__title"));
            card.Add(UIX.MakeLabel(learners.Count == 0 ? "Nobody has used RoadReady yet today." : $"{learners.Count} learner(s) used RoadReady today.", "contact__hint"));

            // Search: magnifying-glass icon and a box; the keyboard opens when the box is pointed at.
            var searchRow = new VisualElement();
            searchRow.AddToClassList("row");
            searchRow.style.alignItems = Align.Center;
            searchRow.Add(ParticipantSetupScreen.BuildSearchIcon());
            var search = new TypingBox("Search by name");
            search.style.flexGrow = 1;
            search.style.marginLeft = 10;
            searchRow.Add(search);
            card.Add(searchRow);
            var keyboard = new OnScreenKeyboard(false) { AutoCapitalise = false };
            keyboard.SetVisible(false);
            keyboard.Target = search;
            card.Add(keyboard);
            search.Clicked += () =>
            {
                search.Active = true;
                keyboard.SetVisible(true);
            };

            // A plain list (not a ScrollView, which swallows VR controller presses): the latest matching entries.
            var list = new VisualElement();
            card.Add(list);
            void Render()
            {
                list.Clear();
                var query = search.Value.Trim();
                var numbered = new System.Collections.Generic.List<(int number, RoadReady.Data.AttendanceEntry entry)>();
                for (var i = 0; i < learners.Count; i++)
                    if (query.Length == 0 || (learners[i].name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        numbered.Add((i + 1, learners[i]));
                if (numbered.Count == 0 && learners.Count > 0)
                    list.Add(UIX.MakeLabel("No learner matches that search.", "contact__hint"));
                for (var i = Math.Max(0, numbered.Count - k_MaxRows); i < numbered.Count; i++)
                    list.Add(UIX.MakeLabel($"{numbered[i].number}.  {numbered[i].entry.name}   -   {numbered[i].entry.Local:HH:mm}", "contact__label"));
                if (numbered.Count > k_MaxRows)
                    list.Add(UIX.MakeLabel($"+ {numbered.Count - k_MaxRows} more - type to narrow the search", "contact__hint"));
            }

            search.Changed += Render;
            Render();

            var buttons = new VisualElement();
            buttons.AddToClassList("row");
            buttons.Add(UIX.MakeButton("Log out", App.ShowLanding, "btn--ghost", "btn--small"));
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            buttons.Add(spacer);
            buttons.Add(UIX.MakeButton("Continue", App.ShowWelcome, "btn--primary", "btn--small"));
            card.Add(buttons);
        }
    }
}
