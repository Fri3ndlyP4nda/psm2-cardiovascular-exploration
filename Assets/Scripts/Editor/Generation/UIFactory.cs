using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Builds uGUI hierarchies from code.
    ///
    /// Why generate the UI instead of hand-building it in the editor: the whole
    /// interface can be recreated from source at any time, it is reviewable in
    /// the report as code, and merge conflicts on .unity files (the usual pain
    /// of a Unity project under version control) largely disappear.
    ///
    /// The palette is deliberately flat and low contrast-noise so the HUD stays
    /// readable on top of a dark red heart interior.
    /// </summary>
    public static class UIFactory
    {
        // ---- Palette ----
        public static readonly Color ColorBackdrop = new Color(0.07f, 0.05f, 0.07f, 0.94f);
        public static readonly Color ColorPanel = new Color(0.12f, 0.10f, 0.13f, 0.96f);
        public static readonly Color ColorClipboard = new Color(0.93f, 0.91f, 0.85f, 0.95f);
        public static readonly Color ColorAccent = new Color(0.78f, 0.16f, 0.22f);
        public static readonly Color ColorAccentDim = new Color(0.35f, 0.12f, 0.16f);
        public static readonly Color ColorTextLight = new Color(0.93f, 0.92f, 0.92f);
        public static readonly Color ColorTextDim = new Color(0.68f, 0.66f, 0.68f);
        public static readonly Color ColorTextDark = new Color(0.16f, 0.17f, 0.20f);

        private static TMP_FontAsset _font;
        private static TMP_FontAsset Font => _font != null ? _font : (_font = ProjectAssets.ResolveFont());

        /// <summary>Clears the cached font. Call between generator runs.</summary>
        public static void ResetCache() => _font = null;

        // ------------------------------------------------------------------
        // Built-in sprites
        // ------------------------------------------------------------------

        private static Sprite Builtin(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        public static Sprite RoundedSprite => Builtin("UI/Skin/UISprite.psd");
        public static Sprite BackgroundSprite => Builtin("UI/Skin/Background.psd");
        public static Sprite KnobSprite => Builtin("UI/Skin/Knob.psd");
        public static Sprite CheckmarkSprite => Builtin("UI/Skin/Checkmark.psd");

        // ------------------------------------------------------------------
        // Structure
        // ------------------------------------------------------------------

        /// <summary>Creates a screen-space canvas that scales with a 1920x1080 reference.</summary>
        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>Adds the EventSystem a scene needs for any UI input to work.</summary>
        public static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>Applies anchors and size in one call.</summary>
        public static RectTransform SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
                                            Vector2 anchoredPosition, Vector2 sizeDelta, Vector2? pivot = null)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        /// <summary>Stretches a rect to fill its parent with optional padding.</summary>
        public static RectTransform Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
            return rt;
        }

        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        // ------------------------------------------------------------------
        // Widgets
        // ------------------------------------------------------------------

        public static Image CreateImage(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;

            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        /// <summary>Full-parent panel with a sliced background.</summary>
        public static Image CreatePanel(Transform parent, string name, Color color, float padding = 0f)
        {
            Image panel = CreateImage(parent, name, color, BackgroundSprite);
            Stretch(panel.rectTransform, padding);
            return panel;
        }

        public static TMP_Text CreateText(Transform parent, string name, string text, float fontSize,
                                          TextAlignmentOptions alignment, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) label.font = Font;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.fontStyle = style;
            label.raycastTarget = false;      // labels never need to block clicks

            return label;
        }

        public static Button CreateButton(Transform parent, string name, string label, Vector2 size, float fontSize = 26f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = ColorAccentDim;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            // Explicit colour states: the defaults are almost invisible on a dark panel.
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.75f, 0.7f, 0.7f, 1f);
            colors.selectedColor = new Color(1.15f, 1.05f, 1.05f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TMP_Text text = CreateText(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center, ColorTextLight, FontStyles.Bold);
            Stretch(text.rectTransform, 8f);

            return button;
        }

        /// <summary>Stacks children vertically. Used for menu button columns.</summary>
        public static VerticalLayoutGroup AddVerticalLayout(GameObject target, float spacing, RectOffset padding = null,
                                                            TextAnchor alignment = TextAnchor.UpperCenter)
        {
            var layout = target.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static Slider CreateSlider(Transform parent, string name, float min, float max, float value, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;

            Image background = CreateImage(go.transform, "Background", new Color(0.22f, 0.20f, 0.23f), BackgroundSprite);
            Stretch(background.rectTransform);
            background.rectTransform.anchorMin = new Vector2(0f, 0.35f);
            background.rectTransform.anchorMax = new Vector2(1f, 0.65f);
            background.rectTransform.offsetMin = Vector2.zero;
            background.rectTransform.offsetMax = Vector2.zero;

            RectTransform fillArea = CreateRect(go.transform, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = new Vector2(8f, 0f);
            fillArea.offsetMax = new Vector2(-8f, 0f);

            Image fill = CreateImage(fillArea, "Fill", ColorAccent, BackgroundSprite);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

            RectTransform handleArea = CreateRect(go.transform, "Handle Slide Area");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            Image handle = CreateImage(handleArea, "Handle", ColorTextLight, KnobSprite);
            handle.rectTransform.sizeDelta = new Vector2(22f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Clamp(value, min, max);

            return slider;
        }

        public static Toggle CreateToggle(Transform parent, string name, string label, bool isOn, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;

            Image background = CreateImage(go.transform, "Background", new Color(0.22f, 0.20f, 0.23f), BackgroundSprite);
            SetRect(background.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(26f, 26f));

            Image checkmark = CreateImage(background.transform, "Checkmark", ColorAccent, CheckmarkSprite);
            checkmark.type = Image.Type.Simple;
            Stretch(checkmark.rectTransform, 2f);

            TMP_Text text = CreateText(go.transform, "Label", label, 22f, TextAlignmentOptions.MidlineLeft, ColorTextLight);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(50f, 0f);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = isOn;

            return toggle;
        }

        public static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, Vector2 size,
                                                      TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;

            var background = go.GetComponent<Image>();
            background.sprite = BackgroundSprite;
            background.type = Image.Type.Sliced;
            background.color = new Color(0.18f, 0.16f, 0.19f);

            // TMP_InputField needs a masked viewport, otherwise long text spills
            // outside the field instead of scrolling.
            RectTransform viewport = CreateRect(go.transform, "Text Area");
            Stretch(viewport, 10f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TMP_Text placeholderLabel = CreateText(viewport, "Placeholder", placeholder, 22f, TextAlignmentOptions.MidlineLeft, ColorTextDim, FontStyles.Italic);
            Stretch(placeholderLabel.rectTransform);

            TMP_Text textLabel = CreateText(viewport, "Text", string.Empty, 22f, TextAlignmentOptions.MidlineLeft, ColorTextLight);
            Stretch(textLabel.rectTransform);

            var field = go.GetComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = viewport;
            field.textComponent = textLabel;
            field.placeholder = placeholderLabel;
            field.contentType = contentType;
            if (Font != null) field.fontAsset = Font;

            return field;
        }

        /// <summary>A label + value row used throughout the settings and HUD panels.</summary>
        public static (TMP_Text label, TMP_Text value) CreateLabelValueRow(Transform parent, string name, string labelText,
                                                                          string valueText, float fontSize = 22f)
        {
            RectTransform row = CreateRect(parent, name);
            row.sizeDelta = new Vector2(0f, fontSize + 10f);

            TMP_Text label = CreateText(row, "Label", labelText, fontSize, TextAlignmentOptions.MidlineLeft, ColorTextDim);
            Stretch(label.rectTransform);

            TMP_Text value = CreateText(row, "Value", valueText, fontSize, TextAlignmentOptions.MidlineRight, ColorTextLight, FontStyles.Bold);
            Stretch(value.rectTransform);

            return (label, value);
        }
    }
}
