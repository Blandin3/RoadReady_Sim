using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoadReady.Accounts
{
    /// <summary>
    /// Small helpers that build the account screens out of plain uGUI (Canvas, Button, TMP_InputField), so the scenes
    /// stay almost empty and every screen looks the same: one accent colour, default fonts, no icons.
    /// </summary>
    public static class UiKit
    {
        // The same palette as the rest of RoadReady (RoadReady.uss): dark navy panel, flag blue / yellow / green.
        public static readonly Color Accent = new Color32(0, 161, 222, 255);      // --rr-blue
        public static readonly Color Yellow = new Color32(250, 210, 1, 255);      // --rr-yellow
        public static readonly Color Green = new Color32(32, 96, 61, 255);        // --rr-green
        public static readonly Color Background = new Color32(12, 18, 26, 255);   // --rr-bg
        public static readonly Color Field = new Color32(40, 52, 66, 255);        // --rr-surface-2
        public static readonly Color TextColor = new Color32(245, 247, 250, 255); // --rr-text
        public static readonly Color Muted = new Color32(186, 198, 210, 255);     // --rr-muted
        public static readonly Color Error = new Color32(229, 72, 77, 255);       // --rr-unsafe

        const float k_CanvasWidth = 1000f;
        const float k_CanvasHeight = 820f;

        /// <summary>
        /// On a monitor (no headset): a normal screen-space canvas, so it can never drift or leave the view. On a
        /// headset: a world-space canvas about 1.6 m in front of the learner at eye height, with the raycaster the VR
        /// controller rays need. Returns the content area (a vertical layout) to add widgets to.
        /// </summary>
        public static RectTransform CreateScreen(string title)
        {
            EnsureEventSystem();
            var vr = XRSettings.isDeviceActive;

            // The camera only has to show a plain background behind the screen.
            var camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
            }

            var canvasGo = new GameObject("Account Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            var rect = canvasGo.GetComponent<RectTransform>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            RectTransform panel;
            if (vr)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
                rect.sizeDelta = new Vector2(k_CanvasWidth, k_CanvasHeight);
                rect.localScale = Vector3.one * 0.001f; // 1000 canvas units = 1 metre
                scaler.dynamicPixelsPerUnit = 3f;
                canvasGo.AddComponent<ScreenPlacer>(); // puts it in front of the head once the headset is tracking
                panel = NewPanel(rect);
                Stretch(panel, 0f);
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(k_CanvasWidth + 200f, k_CanvasHeight + 100f);
                scaler.matchWidthOrHeight = 0.5f;
                panel = NewPanel(rect);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
                panel.sizeDelta = new Vector2(k_CanvasWidth, k_CanvasHeight);
                panel.anchoredPosition = Vector2.zero;
            }

            // The three flag colours along the top, as on the other RoadReady screens.
            var bar = new GameObject("Flag Bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(panel, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 10f);
            barRect.anchoredPosition = Vector2.zero;
            var barLayout = bar.GetComponent<HorizontalLayoutGroup>();
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = barLayout.childForceExpandHeight = true;
            foreach (var colour in new[] { Accent, Yellow, Green })
            {
                var segment = new GameObject("Segment", typeof(RectTransform), typeof(Image));
                segment.transform.SetParent(bar.transform, false);
                segment.GetComponent<Image>().color = colour;
            }

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(panel, false);
            Stretch(content.GetComponent<RectTransform>(), 40f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            Label(content.GetComponent<RectTransform>(), title, 46f, TextColor, FontStyles.Bold, 70f);
            return content.GetComponent<RectTransform>();
        }

        /// <summary>The dark rounded-corner-free panel every screen sits on.</summary>
        static RectTransform NewPanel(RectTransform parent)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Background;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Puts a world-space screen in front of the head a moment after the scene starts (the headset needs a few frames to track), then leaves it still.</summary>
        class ScreenPlacer : MonoBehaviour
        {
            System.Collections.IEnumerator Start()
            {
                for (var i = 0; i < 5; i++)
                    yield return null;
                var head = Camera.main;
                if (head == null)
                    yield break;
                var forward = head.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
                transform.position = head.transform.position + forward * 1.6f;
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        /// <summary>The XR ray / mouse need an EventSystem with the XR UI input module; add one when the scene has none.</summary>
        static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }

        public static TMP_Text Label(RectTransform parent, string text, float size = 28f, Color? colour = null,
            FontStyles style = FontStyles.Normal, float height = 0f)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = colour ?? TextColor;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height > 0f ? height : size * 1.4f;
            return label;
        }

        public static Button MakeButton(RectTransform parent, string text, Action onClick, bool primary = true, float height = 64f)
        {
            var go = new GameObject(text + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = primary ? Accent : Field;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colours = button.colors;
            colours.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colours.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colours.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var label = Label(go.GetComponent<RectTransform>(), text, 30f, TextColor, FontStyles.Normal, 0f);
            Destroy(label.GetComponent<LayoutElement>());
            Stretch(label.rectTransform, 0f);
            label.alignment = TextAlignmentOptions.Center;

            go.AddComponent<LayoutElement>().minHeight = height;
            return button;
        }

        /// <summary>A one-line text box. On the Quest, selecting it opens the system keyboard.</summary>
        public static TMP_InputField MakeInput(RectTransform parent, string placeholder, bool password = false)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Field;
            go.AddComponent<LayoutElement>().minHeight = 64f;

            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            Stretch(viewport.GetComponent<RectTransform>(), 14f);

            var text = NewInputText(viewport.transform, "Text", TextColor, FontStyles.Normal);
            var hint = NewInputText(viewport.transform, "Placeholder", Muted, FontStyles.Italic);
            hint.text = placeholder;

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = viewport.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.characterLimit = 80;
            return input;
        }

        static TextMeshProUGUI NewInputText(Transform parent, string name, Color colour, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>(), 0f);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 28f;
            text.color = colour;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        /// <summary>A horizontal strip for buttons that sit side by side.</summary>
        public static RectTransform Row(RectTransform parent, float height = 64f)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            go.AddComponent<LayoutElement>().minHeight = height;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>A vertical group that can be shown or hidden as a whole (used for the two forms on SchoolSetup).</summary>
        public static RectTransform Group(RectTransform parent)
        {
            var go = new GameObject("Group", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>The red line under a form: a short error, or empty.</summary>
        public static void ShowMessage(TMP_Text label, string message, bool isError = true)
        {
            label.text = message ?? "";
            label.color = isError ? Error : Muted;
        }

        static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        static void Destroy(UnityEngine.Object o) => UnityEngine.Object.Destroy(o);
    }
}
