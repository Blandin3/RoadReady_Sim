using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// A one-line box to type into. Pointing at it selects it: a white caret then ticks at the end of the text, and the
    /// on-screen keyboard (or a real keyboard) types into it. Drawn by hand rather than as a TextField, because a
    /// TextField loses its caret as soon as a keyboard key is pressed with the controller.
    /// </summary>
    public class TypingBox : VisualElement
    {
        readonly Label m_Text;
        readonly VisualElement m_Caret;
        readonly string m_Placeholder;
        string m_Value = "";
        bool m_Active;
        bool m_CaretOn;
        IVisualElementScheduledItem m_Blink;

        /// <summary>Raised when the box is pointed at.</summary>
        public event Action Clicked;
        public event Action Changed;

        public TypingBox(string placeholder)
        {
            m_Placeholder = placeholder;
            focusable = true;
            AddToClassList("typing-box");
            m_Text = new Label();
            m_Text.AddToClassList("typing-box__text");
            m_Text.pickingMode = PickingMode.Ignore;
            Add(m_Text);
            m_Caret = new VisualElement();
            m_Caret.AddToClassList("typing-box__caret");
            m_Caret.pickingMode = PickingMode.Ignore;
            Add(m_Caret);

            RegisterCallback<PointerDownEvent>(_ =>
            {
                Focus();
                Clicked?.Invoke();
            });
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            Refresh();
        }

        public string Value
        {
            get => m_Value;
            set
            {
                m_Value = value ?? "";
                Refresh();
                Changed?.Invoke();
            }
        }

        public bool Active
        {
            get => m_Active;
            set
            {
                m_Active = value;
                EnableInClassList("typing-box--active", value);
                m_CaretOn = value;
                m_Blink ??= schedule.Execute(Tick).Every(500);
                if (value) m_Blink.Resume(); else m_Blink.Pause();
                Refresh();
            }
        }

        void Tick()
        {
            m_CaretOn = !m_CaretOn;
            m_Caret.style.opacity = m_Active && m_CaretOn ? 1f : 0f;
        }

        void Refresh()
        {
            var empty = m_Value.Length == 0;
            m_Text.text = empty ? (m_Active ? "" : m_Placeholder) : m_Value;
            m_Text.EnableInClassList("typing-box__text--placeholder", empty);
            m_Caret.style.opacity = m_Active ? 1f : 0f;
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (!m_Active)
                return;
            if (e.keyCode == KeyCode.Backspace)
            {
                if (m_Value.Length > 0)
                    Value = m_Value.Substring(0, m_Value.Length - 1);
            }
            else if (e.character >= ' ' && e.character < 127 && m_Value.Length < 60)
            {
                Value = m_Value + e.character;
            }
        }
    }
}
