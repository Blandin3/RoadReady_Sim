using System;
using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>A screen shown inside the world-space menu panel. Each Show builds a fresh tree from its UXML.</summary>
    public abstract class UIScreen
    {
        protected RoadReadyApp App { get; private set; }
        protected RoadReadyUI UI { get; private set; }
        protected VisualElement Root { get; private set; }

        /// <summary>Name of the UXML asset (see RoadReadyUI screen library).</summary>
        public abstract string TreeName { get; }

        /// <summary>True for screens that draw their own card over the 3D world instead of the full dark panel.</summary>
        public virtual bool Floating => false;

        public void Mount(RoadReadyApp app, RoadReadyUI ui, VisualElement container, VisualTreeAsset tree)
        {
            App = app;
            UI = ui;
            Root = tree != null ? tree.Instantiate() : new VisualElement();
            Root.AddToClassList("screen");
            Root.style.flexGrow = 1f;
            container.Add(Root);
            Bind();
            UIX.AddGlassSheen(Root);
        }

        protected abstract void Bind();

        public virtual void OnHide() { }

        protected Button Btn(string name, Action onClick) => Root.Btn(name, onClick);

        protected Label Text(string name, string text) => Root.SetText(name, text);

        protected T Q<T>(string name) where T : VisualElement => Root.Q<T>(name);
    }

    public static class UIX
    {
        public static Button Btn(this VisualElement root, string name, Action onClick)
        {
            var button = root.Q<Button>(name);
            if (button != null && onClick != null)
                button.clicked += onClick;
            return button;
        }

        public static Label SetText(this VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null)
                label.text = text ?? string.Empty;
            return label;
        }

        public static void SetVisible(this VisualElement element, bool visible)
        {
            if (element != null)
                element.EnableInClassList("hidden", !visible);
        }

        public static Button MakeButton(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("btn");
            foreach (var c in classes)
                button.AddToClassList(c);
            AddGlassSheen(button);
            return button;
        }

        const string k_SheenClass = "btn--sheen";
        static readonly UnityEngine.Color k_SheenColor = new UnityEngine.Color(1f, 1f, 1f, 0.1f);

        /// <summary>
        /// Adds the gloss highlight of the liquid-glass button style to every .btn under root (idempotent).
        /// Drawn with generateVisualContent rather than a child element: a Button with children stops sizing
        /// itself to its text and collapses.
        /// </summary>
        public static void AddGlassSheen(VisualElement root)
        {
            root.Query<Button>(className: "btn").ForEach(button =>
            {
                if (button.ClassListContains(k_SheenClass))
                    return;
                button.AddToClassList(k_SheenClass);
                button.generateVisualContent += DrawSheen;
            });
        }

        static void DrawSheen(MeshGenerationContext context)
        {
            var size = context.visualElement.layout.size;
            if (size.x < 20f || size.y < 20f)
                return;

            // Rounded highlight over the top ~40% of the button, inset from the rim.
            const float inset = 6f;
            const float top = 4f;
            var x0 = inset;
            var x1 = size.x - inset;
            var y0 = top;
            var y1 = size.y * 0.42f;
            var topRadius = UnityEngine.Mathf.Min(26f, (y1 - y0) * 0.9f, (x1 - x0) * 0.5f);
            var bottomRadius = UnityEngine.Mathf.Min(12f, (y1 - y0) * 0.4f);

            var p = context.painter2D;
            p.fillColor = k_SheenColor;
            p.BeginPath();
            p.MoveTo(new UnityEngine.Vector2(x0 + topRadius, y0));
            p.ArcTo(new UnityEngine.Vector2(x1, y0), new UnityEngine.Vector2(x1, y1), topRadius);
            p.ArcTo(new UnityEngine.Vector2(x1, y1), new UnityEngine.Vector2(x0, y1), bottomRadius);
            p.ArcTo(new UnityEngine.Vector2(x0, y1), new UnityEngine.Vector2(x0, y0), bottomRadius);
            p.ArcTo(new UnityEngine.Vector2(x0, y0), new UnityEngine.Vector2(x1, y0), topRadius);
            p.ClosePath();
            p.Fill();
        }

        public static Label MakeLabel(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var c in classes)
                label.AddToClassList(c);
            return label;
        }

        /// <summary>Single-select chip row for any list of options.</summary>
        public static void BuildChips<T>(VisualElement container, IList<T> options, Func<T, string> label, T selected, Action<T> onSelect)
        {
            container.Clear();
            var buttons = new List<Button>();
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var chip = new Button { text = label(option) };
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--selected", EqualityComparer<T>.Default.Equals(option, selected));
                chip.clicked += () =>
                {
                    foreach (var b in buttons)
                        b.RemoveFromClassList("chip--selected");
                    chip.AddToClassList("chip--selected");
                    onSelect(option);
                };
                buttons.Add(chip);
                container.Add(chip);
            }
        }

        public static void BuildEnumChips<T>(VisualElement container, T selected, Func<T, string> label, Action<T> onSelect) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            BuildChips(container, values, label, selected, onSelect);
        }

        public static VisualElement DecisionRow(string title, string detail, DecisionClass decision, string tag = null)
        {
            var row = new VisualElement();
            row.AddToClassList("decision");
            row.AddToClassList(decision switch
            {
                DecisionClass.Safe => "decision--safe",
                DecisionClass.Borderline => "decision--borderline",
                _ => "decision--unsafe",
            });
            row.Add(MakeLabel(decision switch { DecisionClass.Safe => "OK", DecisionClass.Borderline => "!", _ => "X" }, "decision__icon"));
            var text = new VisualElement();
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            text.Add(MakeLabel(title, "decision__title"));
            if (!string.IsNullOrEmpty(detail))
                text.Add(MakeLabel(detail, "decision__detail"));
            if (!string.IsNullOrEmpty(tag))
                text.Add(MakeLabel(tag, "decision__tag"));
            row.Add(text);
            return row;
        }

        public static VisualElement Metric(string title, string valueText, float fill01)
        {
            var metric = new VisualElement();
            metric.AddToClassList("metric");
            var header = new VisualElement();
            header.AddToClassList("metric__header");
            header.Add(new Label(title));
            header.Add(new Label(valueText));
            metric.Add(header);
            var track = new VisualElement();
            track.AddToClassList("metric__track");
            var fill = new VisualElement();
            fill.AddToClassList("metric__fill");
            fill.style.width = Length.Percent(0f);
            track.Add(fill);
            metric.Add(track);
            // Animate after first layout so the USS width transition plays.
            fill.schedule.Execute(() => fill.style.width = Length.Percent(UnityEngine.Mathf.Clamp01(fill01) * 100f)).StartingIn(50);
            return metric;
        }

        public static string Pretty(this Perspective perspective) => perspective == Perspective.Driver ? "Driver" : "Pedestrian";

        public static string Pretty(this AttemptPhase phase) => phase switch
        {
            AttemptPhase.PreTest => "PRE-TEST (BASELINE)",
            AttemptPhase.PostTest => "POST-TEST",
            AttemptPhase.Tutorial => "TUTORIAL - NOT SCORED",
            _ => "TRAINING",
        };
    }
}
