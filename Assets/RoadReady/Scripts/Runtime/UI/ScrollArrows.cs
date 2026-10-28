using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// A pair of up / down arrow buttons for scrolling with a controller ray: hold one to keep scrolling. They only
    /// show while there is something to scroll to. Used by <see cref="ScrollPane"/> and by every ScrollView a screen
    /// builds (a drag on a ScrollView is not dependable in VR).
    /// </summary>
    public class ScrollArrows : VisualElement
    {
        const float k_Step = 70f;
        const float k_FirstDelay = 250f;
        const float k_Interval = 60f;

        readonly Func<float> m_Offset;
        readonly Func<float> m_Max;
        readonly Action<float> m_SetOffset;
        readonly VisualElement m_Up, m_Down;

        public ScrollArrows(Func<float> offset, Func<float> max, Action<float> setOffset)
        {
            m_Offset = offset;
            m_Max = max;
            m_SetOffset = setOffset;
            pickingMode = PickingMode.Ignore;
            AddToClassList("scroll-arrows");
            m_Up = MakeArrow(true);
            m_Down = MakeArrow(false);
            Add(m_Up);
            Add(m_Down);
            this.SetVisible(false);
        }

        RepeatButton MakeArrow(bool up)
        {
            var button = new RepeatButton(() => Move(up ? -k_Step : k_Step), (long)k_FirstDelay, (long)k_Interval);
            button.AddToClassList("scroll-arrow");
            button.generateVisualContent += context => DrawTriangle(context, up);
            return button;
        }

        void Move(float delta) => m_SetOffset(Mathf.Clamp(m_Offset() + delta, 0f, m_Max()));

        /// <summary>Call after the offset or the size of the content changed.</summary>
        public void Refresh()
        {
            var max = m_Max();
            var scrollable = max > (this.ClassListContains("hidden") ? 8f : 2f);
            this.SetVisible(scrollable);
            m_Up.EnableInClassList("scroll-arrow--off", m_Offset() <= 1f);
            m_Down.EnableInClassList("scroll-arrow--off", m_Offset() >= max - 1f);
            m_Up.MarkDirtyRepaint();
            m_Down.MarkDirtyRepaint();
        }

        static void DrawTriangle(MeshGenerationContext context, bool up)
        {
            var size = context.visualElement.contentRect.size;
            if (size.x < 8f || size.y < 8f)
                return;
            var c = new Vector2(size.x * 0.5f, size.y * 0.5f);
            var half = Mathf.Min(size.x, size.y) * 0.24f;
            var dir = up ? -1f : 1f; // screen y grows downwards
            var p = context.painter2D;
            p.fillColor = new Color(0.98f, 0.82f, 0f, 1f);
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y + dir * half));
            p.LineTo(new Vector2(c.x - half * 1.25f, c.y - dir * half * 0.7f));
            p.LineTo(new Vector2(c.x + half * 1.25f, c.y - dir * half * 0.7f));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>Gives a ScrollView the arrow buttons on its right edge (it keeps its own scroller).</summary>
        public static void Attach(ScrollView view)
        {
            if (view == null || view.Q<ScrollArrows>() != null)
                return;
            var arrows = new ScrollArrows(
                () => view.scrollOffset.y,
                () => Mathf.Max(0f, view.verticalScroller.highValue),
                y => view.scrollOffset = new Vector2(view.scrollOffset.x, y));
            view.hierarchy.Add(arrows);
            view.schedule.Execute(arrows.Refresh).Every(150);
        }
    }

    /// <summary>
    /// Holds a menu screen and scrolls it when it is taller than the panel. A plain clipped box that is moved by the
    /// arrow buttons (a ScrollView would swallow the controller's press, so buttons and sliders inside would not
    /// respond). Screens that bring their own ScrollView are fitted to the panel instead, so their buttons stay put.
    /// </summary>
    public class ScrollPane : VisualElement
    {
        readonly VisualElement m_Viewport;
        readonly VisualElement m_Content;
        readonly ScrollArrows m_Arrows;
        float m_Offset;
        bool m_Fit;

        public VisualElement Content => m_Content;

        public ScrollPane()
        {
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            style.flexBasis = 0f;
            style.minHeight = 0f;

            m_Viewport = new VisualElement { name = "scroll-viewport" };
            m_Viewport.style.flexGrow = 1f;
            m_Viewport.style.flexBasis = 0f;
            m_Viewport.style.minHeight = 0f;
            m_Viewport.style.overflow = Overflow.Hidden;
            Add(m_Viewport);

            m_Content = new VisualElement { name = "scroll-content" };
            m_Content.style.flexShrink = 0f;
            m_Viewport.Add(m_Content);

            m_Arrows = new ScrollArrows(() => m_Offset, () => MaxOffset, SetOffset);
            Add(m_Arrows);

            m_Viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            m_Content.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            m_Viewport.RegisterCallback<WheelEvent>(e =>
            {
                SetOffset(m_Offset + e.delta.y * 40f);
                e.StopPropagation();
            });
        }

        float MaxOffset => Mathf.Max(0f, m_Content.layout.height - m_Viewport.layout.height);

        /// <summary>Called when a new screen is shown: back to the top, and fitted or free-height.</summary>
        public void Reset(bool fit)
        {
            m_Fit = fit;
            m_Offset = 0f;
            Layout();
        }

        void Layout()
        {
            var height = m_Viewport.layout.height;
            if (float.IsNaN(height) || height <= 0f)
                return;
            if (m_Fit)
            {
                m_Content.style.height = height;
                m_Content.style.minHeight = StyleKeyword.Null;
            }
            else
            {
                m_Content.style.height = StyleKeyword.Auto;
                m_Content.style.minHeight = height;
            }

            SetOffset(m_Offset);
        }

        void SetOffset(float value)
        {
            m_Offset = Mathf.Clamp(value, 0f, MaxOffset);
            m_Content.style.translate = new Translate(0f, -m_Offset);
            m_Arrows.Refresh();
        }
    }
}
