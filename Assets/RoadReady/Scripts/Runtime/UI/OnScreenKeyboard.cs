using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// A QWERTY keyboard drawn in the panel, so names and e-mail addresses can be typed by pointing at keys with the
    /// controller. It types into whichever <see cref="Target"/> field was touched last. The "#+=" key switches to a
    /// page of symbols and "ABC" switches back.
    /// </summary>
    public class OnScreenKeyboard : VisualElement
    {
        // Letters page and symbols page. Both pages have the same number of keys in each row.
        static readonly string[][] k_Pages =
        {
            new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" },
            new[] { "1234567890", "!@#$%^&*()", "-_=+[]{}/", "?:;,.'\"" },
        };

        TypingBox m_Target;
        bool m_Upper = true; // a name starts with a capital letter
        bool m_Symbols;
        Button m_Shift, m_PageKey;
        readonly List<List<Button>> m_Grid = new List<List<Button>>();

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
            var letters = k_Pages[0];
            for (var r = 0; r < letters.Length; r++)
            {
                var line = NewRow();
                var keys = new List<Button>();
                m_Grid.Add(keys);
                foreach (var key in letters[r])
                {
                    Button button = null;
                    button = Key(key.ToString(), () => Type(button.text[0]));
                    keys.Add(button);
                    line.Add(button);
                }

                if (r == letters.Length - 1)
                    line.Add(Key("Delete", Backspace, "key--wide"));
            }

            var bottom = NewRow();
            m_Shift = Key("Shift", () => { m_Upper = !m_Upper; Refresh(); }, "key--wide");
            m_PageKey = Key("#+=", () => { m_Symbols = !m_Symbols; Refresh(); }, "key--wide");
            bottom.Add(m_Shift);
            bottom.Add(m_PageKey);
            bottom.Add(Key("@", () => Type('@')));
            bottom.Add(Key(".", () => Type('.')));
            bottom.Add(Key("-", () => Type('-')));
            bottom.Add(Key("_", () => Type('_')));
            bottom.Add(Key("Space", () => Type(' '), "key--space"));
            bottom.Add(Key(".com", () => Append(".com"), "key--wide"));
            Refresh();
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

        /// <summary>Writes the right characters on the keys for the current page and capitals state.</summary>
        void Refresh()
        {
            var page = k_Pages[m_Symbols ? 1 : 0];
            for (var r = 0; r < m_Grid.Count; r++)
                for (var c = 0; c < m_Grid[r].Count; c++)
                {
                    var ch = page[r][c];
                    m_Grid[r][c].text = (!m_Symbols && m_Upper ? char.ToUpperInvariant(ch) : ch).ToString();
                }

            m_PageKey.text = m_Symbols ? "ABC" : "#+=";
            m_Shift.SetEnabled(!m_Symbols);
            m_Shift.EnableInClassList("key--on", m_Upper && !m_Symbols);
        }

        void Type(char ch)
        {
            if (char.IsLetter(ch) && m_Upper && !m_Symbols)
                ch = char.ToUpperInvariant(ch);
            Append(ch.ToString());
            // One capital, then lower case (and again after a space, for a surname).
            m_Upper = AutoCapitalise && ch == ' ';
            Refresh();
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
            Refresh();
        }
    }
}
