using System;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// A QWERTY keyboard drawn in the panel, so names and e-mail addresses can be typed by pointing at keys with the
    /// controller. It types into whichever <see cref="Target"/> field was touched last.
    /// </summary>
    public class OnScreenKeyboard : VisualElement
    {
        static readonly string[] k_Rows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        TypingBox m_Target;
        bool m_Upper = true; // a name starts with a capital letter
        Button m_Shift;
        readonly System.Collections.Generic.List<Button> m_Letters = new System.Collections.Generic.List<Button>();

        public event Action Changed;

        /// <summary>True for names (a capital after each space), false for e-mail addresses.</summary>
        public bool AutoCapitalise { get; set; } = true;

        public TypingBox Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        public OnScreenKeyboard(bool startUpper = true)
        {
            m_Upper = startUpper;
            AddToClassList("keyboard");
            foreach (var row in k_Rows)
            {
                var line = NewRow();
                foreach (var key in row)
                {
                    var letter = key;
                    var button = Key(letter.ToString(), () => Type(letter));
                    if (char.IsLetter(letter))
                        m_Letters.Add(button);
                    line.Add(button);
                }

                if (row == k_Rows[k_Rows.Length - 1])
                    line.Add(Key("Delete", Backspace, "key--wide"));
            }

            var bottom = NewRow();
            m_Shift = Key("Shift", () => { m_Upper = !m_Upper; RefreshLetters(); }, "key--wide");
            bottom.Add(m_Shift);
            bottom.Add(Key("@", () => Type('@')));
            bottom.Add(Key(".", () => Type('.')));
            bottom.Add(Key("-", () => Type('-')));
            bottom.Add(Key("_", () => Type('_')));
            bottom.Add(Key("Space", () => Type(' '), "key--space"));
            bottom.Add(Key(".com", () => Append(".com"), "key--wide"));
            RefreshLetters();
        }

        VisualElement NewRow()
        {
            var row = new VisualElement();
            row.AddToClassList("keyboard__row");
            Add(row);
            return row;
        }

        static Button Key(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("key");
            foreach (var c in classes)
                button.AddToClassList(c);
            return button;
        }

        void RefreshLetters()
        {
            foreach (var button in m_Letters)
                button.text = m_Upper ? button.text.ToUpperInvariant() : button.text.ToLowerInvariant();
            m_Shift.EnableInClassList("key--on", m_Upper);
        }

        void Type(char ch)
        {
            if (char.IsLetter(ch) && m_Upper)
                ch = char.ToUpperInvariant(ch);
            Append(ch.ToString());
            // One capital, then lower case (and again after a space, for a surname).
            m_Upper = AutoCapitalise && ch == ' ';
            RefreshLetters();
        }

        void Append(string text)
        {
            if (m_Target == null)
                return;
            m_Target.Value += text;
            Changed?.Invoke();
        }

        void Backspace()
        {
            if (m_Target == null || string.IsNullOrEmpty(m_Target.Value))
                return;
            m_Target.Value = m_Target.Value.Substring(0, m_Target.Value.Length - 1);
            Changed?.Invoke();
        }

        /// <summary>E-mail addresses are lower case: stop capitalising.</summary>
        public void SetCapitals(bool upper)
        {
            m_Upper = upper;
            RefreshLetters();
        }
    }
}
