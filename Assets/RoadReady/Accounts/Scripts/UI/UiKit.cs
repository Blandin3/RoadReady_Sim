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
        const float k_CanvasHeight = 1100f;

        /// <summary>
        /// Always a world-space canvas (1000 canvas units = 1 metre), so it shows in a headset (Quest, PC-VR over Link
        /// or Air Link) and in the editor's Game view alike. It is placed in front of the head once the headset is
        /// tracking. Mouse clicks and the VR controller rays both work: it has both raycasters. Returns the content
        /// area (a vertical layout) to add widgets to.
        /// </summary>
        public static RectTransform CreateScreen(string title)
        {
            EnsureEventSystem();

            // The camera only has to show a plain background behind the screen.
            var camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
            }

            var canvasGo = new GameObject("Account Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            var rect = canvasGo.GetComponent<RectTransform>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>(); // the VR controller rays (GraphicRaycaster above is the mouse)
            rect.sizeDelta = new Vector2(k_CanvasWidth, k_CanvasHeight);
            rect.localScale = Vector3.one * 0.001f;
            rect.position = new Vector3(0f, 1.4f, 1.4f); // a sensible spot until the placer below runs
            scaler.dynamicPixelsPerUnit = 3f;
            canvasGo.AddComponent<CanvasGroup>().alpha = 0f; // hidden until placed, so it never visibly jumps
            canvasGo.AddComponent<ScreenPlacer>(); // moves it in front of the head once the headset is tracking
            var panel = NewPanel(rect);
            Stretch(panel, 0f);

            // The three flag colours along the top, as on the other RoadReady screens.
            var bar = new GameObject("Flag Bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(panel, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(-80f, 10f); // inset so it sits inside the rounded corners
            barRect.anchoredPosition = new Vector2(0f, -18f);
            var barLayout = bar.GetComponent<HorizontalLayoutGroup>();
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = barLayout.childForceExpandHeight = true;
            foreach (var colour in new[] { Accent, Yellow, Green })
            {
                var segment = new GameObject("Segment", typeof(RectTransform), typeof(Image));
                segment.transform.SetParent(bar.transform, false);
                segment.GetComponent<Image>().color = colour;
                Round(segment.GetComponent<Image>(), 4.8f);
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

        /// <summary>The dark rounded panel every screen sits on.</summary>
        static RectTransform NewPanel(RectTransform parent)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = Background;
            Round(image, 0.9f);
            return go.GetComponent<RectTransform>();
        }

        static Sprite s_Rounded;

        /// <summary>A white rounded rectangle (9-sliced), tinted by each Image's colour, so every widget has the same soft corners as the rest of the UI.</summary>
        static Sprite RoundedSprite()
        {
            if (s_Rounded != null)
                return s_Rounded;
            const int size = 64;
            const float radius = 24f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var qx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    var qy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    var alpha = Mathf.Clamp01(0.5f - (Mathf.Sqrt(qx * qx + qy * qy) - radius));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            s_Rounded = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return s_Rounded;
        }

        // ---------------------------------------------------------------- liquid-glass buttons

        // The same look as the .btn style in RoadReady.uss: a translucent fill, a bright rim and a soft gloss on the top half.
        static readonly Color GlassPlain = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color GlassBlue = new Color(0f, 161f / 255f, 222f / 255f, 0.55f);
        static readonly Color GlassYellow = new Color(250f / 255f, 210f / 255f, 1f / 255f, 0.75f);
        static readonly Color RimPlain = new Color(1f, 1f, 1f, 0.28f);
        static readonly Color RimBlue = new Color(0.47f, 0.84f, 1f, 0.55f);
        static readonly Color RimYellow = new Color(1f, 0.93f, 0.55f, 0.65f);

        public enum GlassState { Plain, Primary, Highlight }

        static Sprite s_Ring;

        /// <summary>The outline of the rounded rectangle (a thin ring), for the glass rim.</summary>
        static Sprite RingSprite()
        {
            if (s_Ring != null)
                return s_Ring;
            const int size = 64;
            const float radius = 24f, thickness = 3f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var ax = Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                    var ay = Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                    var d = new Vector2(Mathf.Max(ax, 0f), Mathf.Max(ay, 0f)).magnitude + Mathf.Min(Mathf.Max(ax, ay), 0f) - radius;
                    var alpha = Mathf.Clamp01(Mathf.Clamp01(0.5f - d) - Mathf.Clamp01(0.5f - (d + thickness)));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            s_Ring = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return s_Ring;
        }

        /// <summary>Adds the gloss (top half) and the bright rim as children of a button, under its label.</summary>
        static void AddGlass(RectTransform button)
        {
            var sheen = new GameObject("Sheen", typeof(RectTransform), typeof(Image));
            sheen.transform.SetParent(button, false);
            var sheenRect = sheen.GetComponent<RectTransform>();
            sheenRect.anchorMin = new Vector2(0f, 0.5f);
            sheenRect.anchorMax = Vector2.one;
            sheenRect.offsetMin = new Vector2(4f, 0f);
            sheenRect.offsetMax = new Vector2(-4f, -4f);
            var sheenImage = sheen.GetComponent<Image>();
            sheenImage.color = new Color(1f, 1f, 1f, 0.09f);
            sheenImage.raycastTarget = false;
            Round(sheenImage, 2f);

            var rim = new GameObject("Rim", typeof(RectTransform), typeof(Image));
            rim.transform.SetParent(button, false);
            Stretch(rim.GetComponent<RectTransform>(), 0f);
            var rimImage = rim.GetComponent<Image>();
            rimImage.sprite = RingSprite();
            rimImage.type = Image.Type.Sliced;
            rimImage.pixelsPerUnitMultiplier = 1.4f;
            rimImage.raycastTarget = false;
        }

        /// <summary>Sets a button's glass tint: plain white glass, blue (primary) or yellow (highlighted, e.g. Shift is on).</summary>
        public static void SetGlass(Button button, GlassState state)
        {
            var fill = state == GlassState.Primary ? GlassBlue : state == GlassState.Highlight ? GlassYellow : GlassPlain;
            var rim = state == GlassState.Primary ? RimBlue : state == GlassState.Highlight ? RimYellow : RimPlain;
            button.GetComponent<Image>().color = fill;
            var rimTransform = button.transform.Find("Rim");
            if (rimTransform != null)
                rimTransform.GetComponent<Image>().color = rim;
        }

        /// <summary>Gives an Image rounded corners. A larger <paramref name="cornerScale"/> makes the corners smaller.</summary>
        static void Round(Image image, float cornerScale = 1.4f)
        {
            image.sprite = RoundedSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerScale;
        }

        /// <summary>
        /// Puts the screen in front of the head once the headset is tracking (PC-VR can take a couple of seconds to
        /// start), then leaves it still. Without a headset it sits a little closer so it fills the Game view.
        /// </summary>
        class ScreenPlacer : MonoBehaviour
        {
            System.Collections.IEnumerator Start()
            {
                for (var i = 0; i < 5; i++)
                    yield return null;
                for (var i = 0; i < 240 && !XRSettings.isDeviceActive && XRSettings.enabled; i++) // PC-VR can take a moment; no XR at all: do not wait
                    yield return null;
                // Let the rig finish its own height adjustment (floor offset) before reading where the head points.
                var settle = Time.unscaledTime + 1f;
                while (Time.unscaledTime < settle)
                    yield return null;

                var head = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
                if (head == null)
                {
                    Reveal();
                    yield break;
                }

                // Fix the screen to the rig (the XR Origin root) at a set height, not to the head and not to the
                // world: it can then neither follow the head nor drift when the head height or the rig moves.
                var root = head.transform.root;
                if (root == head.transform) // a bare camera with no rig: a fixed place in the world is just as steady
                {
                    var flat = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                    flat = flat == Vector3.zero ? Vector3.forward : flat;
                    transform.SetPositionAndRotation(head.transform.position + flat * 1.4f, Quaternion.LookRotation(flat, Vector3.up));
                    Reveal();
                    yield break;
                }

                var forward = root.InverseTransformDirection(head.transform.forward);
                forward.y = 0f;
                forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
                var distance = XRSettings.isDeviceActive ? 1.6f : 1.4f;
                transform.SetParent(root, false);
                transform.localScale = Vector3.one * 0.001f;
                transform.localPosition = new Vector3(0f, 1.4f, 0f) + forward * distance;
                transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                Reveal();
            }

            void Reveal()
            {
                var group = GetComponent<CanvasGroup>();
                if (group != null)
                    group.alpha = 1f;
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
            Round(image);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            AddGlass(go.GetComponent<RectTransform>());
            SetGlass(button, primary ? GlassState.Primary : GlassState.Plain);
            var colours = button.colors;
            colours.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1.35f);
            colours.pressedColor = new Color(0.8f, 0.8f, 0.8f, 0.8f);
            colours.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            colours.fadeDuration = 0f; // no colour fade: the button changes instantly
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var label = Label(go.GetComponent<RectTransform>(), text, 30f, TextColor, FontStyles.Normal, 0f);
            Destroy(label.GetComponent<LayoutElement>());
            Stretch(label.rectTransform, 0f);
            label.alignment = TextAlignmentOptions.Center;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return button;
        }

        /// <summary>A one-line text box. Click it (mouse or controller ray), then type on a keyboard or use <see cref="Keyboard"/>.</summary>
        public static TMP_InputField MakeInput(RectTransform parent, string placeholder, bool password = false)
        {
            // Built switched off and switched on at the end: a TMP_InputField that wakes up before its text and viewport
            // are assigned never sets itself up, and then nothing typed into it is shown.
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var fieldImage = go.GetComponent<Image>();
            fieldImage.color = Field;
            Round(fieldImage);
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 64f;
            element.preferredHeight = 64f;

            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            Stretch(viewport.GetComponent<RectTransform>(), 14f);

            var text = NewInputText(viewport.transform, "Text", TextColor, FontStyles.Normal);
            var hint = NewInputText(viewport.transform, "Placeholder", Muted, FontStyles.Italic);
            hint.text = placeholder;

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = go.GetComponent<Image>();
            var inputColours = input.colors;
            inputColours.fadeDuration = 0f; // no colour fade
            input.colors = inputColours;
            input.textViewport = viewport.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.characterLimit = 80;
            input.shouldHideSoftKeyboard = true; // the on-screen keyboard is used instead of the system one
            input.shouldHideMobileInput = true;
            go.AddComponent<InputFocus>().Init(input);
            go.SetActive(true);
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
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
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

        // ---------------------------------------------------------------- on-screen keyboard

        /// <summary>Remembers which text box was clicked last, so the on-screen keyboard knows where to type.</summary>
        class InputFocus : MonoBehaviour, IPointerClickHandler, ISelectHandler
        {
            public static TMP_InputField Current;
            TMP_InputField m_Input;

            public void Init(TMP_InputField input)
            {
                m_Input = input;
                if (Current == null)
                    Current = input;
            }

            public void OnPointerClick(PointerEventData eventData) => Current = m_Input;
            public void OnSelect(BaseEventData eventData) => Current = m_Input;
        }

        /// <summary>Makes the on-screen keyboard type into this text box (used when a form is shown).</summary>
        public static void Focus(TMP_InputField input) => InputFocus.Current = input;

        /// <summary>
        /// A small QWERTY keyboard that types into the text box clicked last. PC-VR has no system keyboard and a
        /// controller ray cannot type, so every screen with text boxes adds one of these.
        /// </summary>
        public static void Keyboard(RectTransform parent)
        {
            // Two pages: letters, and symbols (the "#+=" key switches, "ABC" switches back). Same key count per row.
            var pages = new[]
            {
                new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" },
                new[] { "1234567890", "!@#$%^&*()", "-_=+[]{}/", "?:;,.'\"" },
            };

            var root = Group(parent);
            root.name = "Keyboard";
            root.GetComponent<VerticalLayoutGroup>().spacing = 8f;
            var shift = false;
            var symbols = false;
            var keyLabels = new System.Collections.Generic.List<TMP_Text>[pages[0].Length];
            Button shiftButton = null;
            TMP_Text symbolsLabel = null;

            void Type(string key)
            {
                var field = InputFocus.Current;
                if (field == null || field.text.Length >= field.characterLimit)
                    return;
                field.text += key;
                field.caretPosition = field.text.Length;
                if (shift)
                {
                    shift = false;
                    Refresh();
                }
            }

            // Writes the right characters on the keys for the current page and shift state.
            void Refresh()
            {
                for (var r = 0; r < keyLabels.Length; r++)
                    for (var c = 0; c < keyLabels[r].Count; c++)
                    {
                        var ch = pages[symbols ? 1 : 0][r][c].ToString();
                        keyLabels[r][c].text = shift && !symbols ? ch.ToUpperInvariant() : ch;
                    }

                symbolsLabel.text = symbols ? "ABC" : "#+=";
                shiftButton.interactable = !symbols;
                SetGlass(shiftButton, shift ? GlassState.Highlight : GlassState.Plain);
            }

            RectTransform KeyRow()
            {
                var row = Row(root, 56f);
                row.GetComponent<HorizontalLayoutGroup>().spacing = 8f;
                return row;
            }

            for (var r = 0; r < pages[0].Length; r++)
            {
                var row = KeyRow();
                keyLabels[r] = new System.Collections.Generic.List<TMP_Text>();
                for (var c = 0; c < pages[0][r].Length; c++)
                {
                    TMP_Text label = null;
                    var button = MakeButton(row, pages[0][r][c].ToString(), () => Type(label.text), primary: false, height: 56f);
                    label = button.GetComponentInChildren<TMP_Text>();
                    label.fontSize = 28f;
                    keyLabels[r].Add(label);
                }
            }

            var last = KeyRow();
            shiftButton = MakeButton(last, "Shift", () => { shift = !shift; Refresh(); }, primary: false, height: 56f);
            var symbolsButton = MakeButton(last, "#+=", () => { symbols = !symbols; shift = false; Refresh(); }, primary: false, height: 56f);
            symbolsLabel = symbolsButton.GetComponentInChildren<TMP_Text>();
            foreach (var symbol in new[] { "@", ".", "-", "_" })
            {
                var k = symbol;
                MakeButton(last, k, () => Type(k), primary: false, height: 56f);
            }

            MakeButton(last, "Space", () => Type(" "), primary: false, height: 56f);
            MakeButton(last, "Delete", () =>
            {
                var field = InputFocus.Current;
                if (field != null && field.text.Length > 0)
                {
                    field.text = field.text.Substring(0, field.text.Length - 1);
                    field.caretPosition = field.text.Length;
                }
            }, primary: false, height: 56f);
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
