using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// Settings shared by every ScrollView a screen builds: a normal vertical scroll bar that shows when the list is longer
    /// than its box. (There are no extra up / down buttons any more; the controller thumbstick scrolls too, see
    /// <see cref="RoadReadyUI"/>.)
    /// </summary>
    public static class ScrollBars
    {
        public const float WheelStep = 60f;

        public static void Attach(ScrollView view)
        {
            if (view == null)
                return;
            view.mode = ScrollViewMode.Vertical;
            view.verticalScrollerVisibility = ScrollerVisibility.Auto;
            view.mouseWheelScrollSize = WheelStep;
        }

        /// <summary>How far this ScrollView can still scroll (0 when everything fits).</summary>
        public static float MaxOffset(ScrollView view) => view == null ? 0f : Mathf.Max(0f, view.verticalScroller.highValue);

        /// <summary>Scrolls a ScrollView by delta pixels (positive = down). Returns false when it has nothing to scroll.</summary>
        public static bool ScrollBy(ScrollView view, float delta)
        {
            if (view == null || !(view.layout.height > 0f) || MaxOffset(view) <= 2f) // not shown (or inside a hidden tab) or nothing to scroll
                return false;
            view.scrollOffset = new Vector2(view.scrollOffset.x, Mathf.Clamp(view.scrollOffset.y + delta, 0f, MaxOffset(view)));
            return true;
        }
    }

    /// <summary>
    /// Holds a menu screen and scrolls it when it is taller than the panel, with a normal scroll bar (drag the thumb, tap
    /// the track to move a page) and the mouse wheel or controller thumbstick. A plain clipped box that is moved by
    /// the scroll bar (a ScrollView would swallow the controller's press, so buttons and sliders inside would not
    /// respond). Screens that bring their own ScrollView are fitted to the panel instead, so their buttons stay put.
    /// </summary>
    public class ScrollPane : VisualElement
    {
        readonly VisualElement m_Viewport;
        readonly VisualElement m_Content;
        readonly VisualElement m_Track, m_Thumb;
        float m_Offset;
        bool m_Fit;
        bool m_Dragging;
        float m_DragStartY, m_DragStartOffset;

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

            // A normal scroll bar: a track with a thumb you can drag; tap the track to move a page.
            m_Track = new VisualElement { name = "scroll-track" };
            m_Track.AddToClassList("scroll-track");
            m_Thumb = new VisualElement { name = "scroll-thumb" };
            m_Thumb.AddToClassList("scroll-thumb");
            m_Track.Add(m_Thumb);
            Add(m_Track);
            m_Track.SetVisible(false);
            m_Track.RegisterCallback<GeometryChangedEvent>(_ => UpdateThumb());
            m_Track.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target != m_Track)
                    return;
                var page = m_Viewport.layout.height * 0.9f;
                SetOffset(m_Offset + (e.localPosition.y < m_Thumb.layout.y ? -page : page));
                e.StopPropagation();
            });
            m_Thumb.RegisterCallback<PointerDownEvent>(e =>
            {
                m_Dragging = true;
                m_DragStartY = e.position.y;
                m_DragStartOffset = m_Offset;
                m_Thumb.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            m_Thumb.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!m_Dragging)
                    return;
                var travel = Mathf.Max(1f, m_Track.layout.height - m_Thumb.layout.height);
                SetOffset(m_DragStartOffset + (e.position.y - m_DragStartY) * (MaxOffset / travel));
            });
            m_Thumb.RegisterCallback<PointerUpEvent>(e =>
            {
                m_Dragging = false;
                m_Thumb.ReleasePointer(e.pointerId);
            });
            m_Thumb.RegisterCallback<PointerCaptureOutEvent>(_ => m_Dragging = false);

            m_Viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            m_Content.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            m_Viewport.RegisterCallback<WheelEvent>(e =>
            {
                SetOffset(m_Offset + e.delta.y * 40f);
                e.StopPropagation();
            });
        }

        float MaxOffset => Mathf.Max(0f, m_Content.layout.height - m_Viewport.layout.height);

        /// <summary>Scrolls by delta pixels (positive = down); used by the controller thumbstick. False when nothing can scroll.</summary>
        public bool ScrollBy(float delta)
        {
            if (MaxOffset <= 2f)
                return false;
            SetOffset(m_Offset + delta);
            return true;
        }

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
            UpdateThumb();
        }

        void UpdateThumb()
        {
            var max = MaxOffset;
            m_Track.SetVisible(max > 2f);
            var viewport = m_Viewport.layout.height;
            var content = m_Content.layout.height;
            var track = m_Track.layout.height;
            if (max <= 2f || float.IsNaN(track) || track <= 0f || content <= 0f)
                return;
            var thumb = Mathf.Clamp(track * viewport / content, 64f, track);
            m_Thumb.style.height = thumb;
            m_Thumb.style.top = (track - thumb) * Mathf.Clamp01(m_Offset / max);
        }
    }
}
