using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Builds the whole Car Sales UI from code (theme colours, procedurally generated rounded sprites, icons,
    /// buttons, windows, lists...). No prefabs or Inspector wiring needed.
    /// Colours come from <see cref="UITheme"/>.
    /// </summary>
    public static partial class UIFactory
    {
        // ------------------------------------------------------------------ THEME
        // Colours come from the active UITheme (Settings → Theme). Screens must use these tokens, never literal colours.
        static UIPalette T => UITheme.Current;
        public static Color WindowBg => T.windowBg;
        public static Color HeaderBg => T.headerBg;
        public static Color CardBg => T.cardBg;
        public static Color CardLight => T.cardLight;
        public static Color MenuBg => T.menuBg;
        public static Color HudBg => T.hudBg;
        public static Color HudBgStrong => T.hudBgStrong;
        public static Color ToastBg => T.toastBg;
        public static Color PictureBg => T.pictureBg;
        public static Color Overlay => T.overlay;
        public static Color Line => T.line;
        public static Color Accent => T.accent;
        public static Color AccentDark => T.accentDark;
        public static Color Good => T.good;
        public static Color Warn => T.warn;
        public static Color Bad => T.bad;
        public static Color Neutral => T.neutral;
        public static Color TextMain => T.textMain;
        public static Color TextDim => T.textDim;
        public static Color TextFaint => T.textFaint;
        public static Color ChatPlayer => T.chatPlayer;
        public static Color Gold => T.gold;
        public static Color Info => T.info;
        public static Color Special => T.special;
        public static Color Personal => T.personal;
        public static Color Home => T.home;
        public static Color Crosshair => T.crosshair;
        public static Color MapBg => T.mapBg;
        public static Color MapFrame => T.mapFrame;
        public static Color Backdrop => T.backdrop;
        public static Color Ghost => T.ghost;
        public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>Readable text colour on top of <paramref name="background"/> (dark text on light buttons).</summary>
        public static Color ContrastText(Color background)
        {
            float lum = 0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b;
            return lum > 0.62f ? new Color(0.08f, 0.1f, 0.16f) : Color.white;
        }

        // ------------------------------------------------------------------ SPRITES (generated once)
        static Sprite rounded, circle, ring, arrow, check;
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded(64, 18));
        public static Sprite Circle  => circle ? circle : (circle = MakeCircle(128, 0f, "Circle"));
        public static Sprite Ring    => ring ? ring : (ring = MakeCircle(128, 0.78f, "Ring"));
        public static Sprite Arrow   => arrow ? arrow : (arrow = MakeArrow(64));
        public static Sprite Check   => check ? check : (check = MakeCheck(128));

        static Texture2D NewTex(int size, string name) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "CarSales_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        static Sprite ToSprite(Texture2D tex, Color32[] px, Vector4 border)
        {
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, border);
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = NewTex(size, "Rounded");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, radius, size - radius), cy = Mathf.Clamp(fy, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255));
                }
            float b = radius + 2;
            return ToSprite(tex, px, new Vector4(b, b, b, b));
        }

        static Sprite MakeCircle(int size, float innerRatio, string name)
        {
            var tex = NewTex(size, name);
            var px = new Color32[size * size];
            float r = size / 2f, inner = r * innerRatio;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d - 0.5f);
                    if (innerRatio > 0f) a *= Mathf.Clamp01(d - inner + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            return ToSprite(tex, px, Vector4.zero);
        }

        static Sprite MakeArrow(int size)
        {
            var tex = NewTex(size, "Arrow");
            var px = new Color32[size * size];
            Vector2 a = new Vector2(size * 0.5f, size * 0.95f), b = new Vector2(size * 0.12f, size * 0.08f),
                    c = new Vector2(size * 0.5f, size * 0.28f), d = new Vector2(size * 0.88f, size * 0.08f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTriangle(p, a, b, c) || InTriangle(p, a, c, d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                }
            return ToSprite(tex, px, Vector4.zero);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float s2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float s3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
            return !(neg && pos);
        }

        static Sprite MakeCheck(int size)
        {
            var tex = NewTex(size, "Check");
            var px = new Color32[size * size];
            Vector2 p0 = new Vector2(0.2f, 0.52f) * size, p1 = new Vector2(0.42f, 0.28f) * size, p2 = new Vector2(0.82f, 0.74f) * size;
            float thick = size * 0.085f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(SegmentDistance(p, p0, p1), SegmentDistance(p, p1, p2));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(thick - d + 0.5f) * 255));
                }
            return ToSprite(tex, px, Vector4.zero);
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ BASICS
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            if (parent) go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Rounded panel. radiusScale > 1 = smaller corners.</summary>
        public static Image Panel(Transform parent, string name, Color color, float radiusScale = 1f)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = radiusScale;
            img.color = color;
            return img;
        }

        public static Image Fill(Transform parent, string name, Color color)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image SpriteImage(Transform parent, string name, Sprite sprite, Color color, float size)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false, string name = "Text")
        {
            var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        // ------------------------------------------------------------------ LAYOUT
        public static LayoutElement LE(Component c, float h = -1, float w = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (!le) le = c.gameObject.AddComponent<LayoutElement>();
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup Vertical(Component c, int pad = 0, float spacing = 8f, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup Horizontal(Component c, int pad = 0, float spacing = 8f,
            TextAnchor align = TextAnchor.MiddleLeft, bool expandWidth = false)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = expandWidth; h.childForceExpandHeight = true;
            return h;
        }

        public static RectTransform HRow(Transform parent, float height, float spacing = 10f, string name = "Row", bool expand = true)
        {
            var rt = Rect(name, parent);
            Horizontal(rt, 0, spacing, TextAnchor.MiddleLeft, expand);
            if (height > 0) LE(rt, h: height);
            return rt;
        }

        public static RectTransform VCol(Transform parent, float spacing = 8f, string name = "Column", float width = -1, float flexW = 1)
        {
            var rt = Rect(name, parent);
            Vertical(rt, 0, spacing);
            LE(rt, w: width, flexW: width > 0 ? 0 : flexW);
            return rt;
        }

        public static RectTransform Spacer(Transform parent, float flexH = 1f, float flexW = 0f)
        {
            var rt = Rect("Spacer", parent);
            LE(rt, flexH: flexH, flexW: flexW);
            return rt;
        }

        public static Image Divider(Transform parent)
        {
            var d = Fill(parent, "Divider", Line);
            LE(d, h: 1);
            return d;
        }

        // ------------------------------------------------------------------ CONTROLS
        public static Button Btn(Transform parent, string label, Color color, UnityAction onClick, float height = 46f, float fontSize = 20f)
            => Btn(parent, label, color, onClick, out _, height, fontSize);

        public static Button Btn(Transform parent, string label, Color color, UnityAction onClick, out TextMeshProUGUI labelText,
                                 float height = 46f, float fontSize = 20f)
        {
            var img = Panel(parent, label.Replace("\n", " ") + "Button", color, 2.2f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = T.buttonHover;
            cb.selectedColor = Color.white;
            cb.pressedColor = T.buttonPressed;
            cb.disabledColor = T.buttonDisabled;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(() => CarSalesServices.PlaySfx(Sfx.Click, 0.5f));
            LE(img, h: height);
            labelText = Text(img.transform, label, fontSize, ContrastText(color), TextAlignmentOptions.Center, true, "Label");
            Stretch(labelText.rectTransform, 4f);
            return btn;
        }

        /// <summary>Recolours a button made with Btn (keeps hover tint behaviour).</summary>
        public static void SetButtonColor(Button b, Color c)
        {
            if (!b || !b.targetGraphic) return;
            b.targetGraphic.color = c;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label) label.color = ContrastText(c);
        }

        public static Image Icon(Transform parent, Color color, string glyph, float size, bool useCheck = false)
        {
            var bg = SpriteImage(parent, "Icon", Circle, color, size);
            LE(bg, h: size, w: size, flexW: 0);
            if (useCheck)
            {
                var c = SpriteImage(bg.transform, "Check", Check, Color.white, size * 0.8f);
                c.rectTransform.anchoredPosition = Vector2.zero;
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                var t = Text(bg.transform, glyph, size * 0.5f, Color.white, TextAlignmentOptions.Center, true);
                Stretch(t.rectTransform);
            }
            return bg;
        }

        public static Image Chip(Transform parent, string text, Color color, out TextMeshProUGUI label, float fontSize = 14f)
        {
            var p = Panel(parent, "Chip", color, 3f);
            var h = Horizontal(p, 0, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(12, 12, 4, 4);
            label = Text(p.transform, text, fontSize, Color.white, TextAlignmentOptions.Center, true);
            var fitter = p.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return p;
        }

        public static TextMeshProUGUI SpecRow(Transform parent, string label, float height = 32f)
        {
            var row = HRow(parent, height, 10, "Spec_" + label, false);
            var l = Text(row, label, 18, TextDim, TextAlignmentOptions.Left);
            LE(l, flexW: 1);
            var v = Text(row, "-", 18, TextMain, TextAlignmentOptions.Right, true);
            LE(v, flexW: 1);
            return v;
        }

        public static TextMeshProUGUI KeyHint(Transform parent, string key, string action)
        {
            var row = HRow(parent, 34, 10, "Key_" + key, false);
            var cap = Panel(row, "Key", T.keyCap, 3f);
            LE(cap, h: 32, w: Mathf.Max(34, key.Length * 11 + 18), flexW: 0);
            var k = Text(cap.transform, key, 16, T.keyCapText, TextAlignmentOptions.Center, true);
            Stretch(k.rectTransform);
            var t = Text(row, action, 17, TextMain, TextAlignmentOptions.Left);
            LE(t, flexW: 1);
            return t;
        }

        public class Bar
        {
            public Image background, fill;
            public void Set(float value01) => fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
            public void SetColor(Color c) => fill.color = c;
        }

        public static Bar ProgressBar(Transform parent, Color color, float height = 14f)
        {
            var bg = Panel(parent, "Bar", Ghost, 4f);
            LE(bg, h: height);
            var fill = Panel(bg.transform, "Fill", color, 4f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return new Bar { background = bg, fill = fill };
        }

        /// <summary>Rounded picture frame that shows a live-rendered photo of the car.</summary>
        public static RawImage CarPicture(Transform parent, float height, float width = -1)
        {
            var frame = Panel(parent, "CarPicture", T.pictureBg, 1.4f);
            LE(frame, h: height, w: width, flexW: width > 0 ? 0 : 1);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var raw = Rect("Photo", frame.transform).gameObject.AddComponent<RawImage>();
            Stretch(raw.rectTransform);
            raw.raycastTarget = false;
            raw.color = Clear;
            return raw;
        }

        public static void SetCarPicture(RawImage raw, CarInstance car)
        {
            if (!raw) return;
            var tex = ThumbnailService.Instance && car != null ? ThumbnailService.Instance.Get(car) : null;
            raw.texture = tex;
            raw.color = tex ? Color.white : Clear;
        }

        public static RectTransform ScrollList(Transform parent, float spacing = 10f, string name = "Scroll", bool vertical = true)
        {
            var go = DefaultControls.CreateScrollView(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            SetLayer(go, 5);
            go.GetComponent<Image>().color = Clear;
            LE(go.GetComponent<RectTransform>(), flexH: 1, flexW: 1);

            var sr = go.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 35f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            if (sr.horizontalScrollbar) { Object.Destroy(sr.horizontalScrollbar.gameObject); sr.horizontalScrollbar = null; }
            if (sr.verticalScrollbar)
            {
                sr.verticalScrollbar.GetComponent<Image>().color = Ghost;
                if (sr.verticalScrollbar.handleRect)
                {
                    var h = sr.verticalScrollbar.handleRect.GetComponent<Image>();
                    h.color = Accent; h.sprite = Rounded; h.type = Image.Type.Sliced; h.pixelsPerUnitMultiplier = 4f;
                }
                sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            }

            var content = sr.content;
            if (vertical)
            {
                Vertical(content, 0, spacing);
                content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(2, 14, 2, 2);
            }
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        public static RectTransform ScrollGrid(Transform parent, Vector2 cell, int columns, float spacing = 16f)
        {
            var content = ScrollList(parent, spacing, "Grid", false);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.padding = new RectOffset(2, 14, 2, 2);
            return content;
        }

        public static Slider MakeSlider(Transform parent, Color fill, float height = 30f)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            SetLayer(go, 5);
            var slider = go.GetComponent<Slider>();
            var bg = go.transform.Find("Background");
            if (bg) { var i = bg.GetComponent<Image>(); i.color = Ghost; i.sprite = Rounded; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 5f; }
            if (slider.fillRect) { var i = slider.fillRect.GetComponent<Image>(); i.color = fill; i.sprite = Rounded; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 5f; }
            if (slider.handleRect)
            {
                var i = slider.handleRect.GetComponent<Image>();
                i.sprite = Circle; i.color = Color.white;
                slider.handleRect.sizeDelta = new Vector2(26f, 0f);
            }
            LE(go.GetComponent<RectTransform>(), h: height);
            return slider;
        }

        public static TMP_InputField MakeInput(Transform parent, string placeholder, float height = 44f)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            SetLayer(go, 5);
            var bg = go.GetComponent<Image>();
            bg.sprite = Rounded; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 2.5f; bg.color = CardLight;
            var input = go.GetComponent<TMP_InputField>();
            input.pointSize = 19;
            if (input.textComponent) { input.textComponent.font = TMP_Settings.defaultFontAsset; input.textComponent.color = TextMain; input.textComponent.fontSize = 19; }
            if (input.placeholder is TMP_Text ph) { ph.font = TMP_Settings.defaultFontAsset; ph.text = placeholder; ph.color = TextDim; ph.fontSize = 19; }
            input.caretColor = TextMain;
            input.selectionColor = T.selection;
            LE(go.GetComponent<RectTransform>(), h: height, flexW: 1);
            return input;
        }

        /// <summary>Full-screen panel root + centred window with header, title and close button. Returns the body.</summary>
        public static RectTransform Window(RectTransform panelRoot, string title, Vector2 size, UnityAction onClose,
                                           out TextMeshProUGUI titleText, out RectTransform header)
        {
            var backdrop = panelRoot.GetComponent<Image>();
            if (!backdrop) backdrop = panelRoot.gameObject.AddComponent<Image>();
            backdrop.color = Backdrop;

            var win = Panel(panelRoot, "Window", WindowBg, 1f);
            Place(win.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var outline = win.gameObject.AddComponent<Outline>();
            outline.effectColor = Line;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var shadow = win.gameObject.AddComponent<Shadow>();
            shadow.effectColor = T.shadow;
            shadow.effectDistance = new Vector2(0f, -8f);

            var head = Panel(win.transform, "Header", HeaderBg, 1f);
            head.rectTransform.anchorMin = new Vector2(0f, 1f);
            head.rectTransform.anchorMax = new Vector2(1f, 1f);
            head.rectTransform.pivot = new Vector2(0.5f, 1f);
            head.rectTransform.anchoredPosition = Vector2.zero;
            head.rectTransform.sizeDelta = new Vector2(0f, 60f);
            header = head.rectTransform;

            var line = Fill(win.transform, "HeaderLine", Accent);
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = new Vector2(1f, 1f);
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.anchoredPosition = new Vector2(0f, -60f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);

            titleText = Text(head.transform, title, 25, TextMain, TextAlignmentOptions.Left, true, "Title");
            Stretch(titleText.rectTransform);
            titleText.rectTransform.offsetMin = new Vector2(24f, 0f);

            if (onClose != null)
            {
                var close = Btn(head.transform, "X", Ghost, onClose, 38f, 18f);
                Place((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(40f, 38f));
            }

            var body = Rect("Body", win.transform);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(24f, 22f);
            body.offsetMax = new Vector2(-24f, -80f);
            return body;
        }

        public static RectTransform Window(RectTransform panelRoot, string title, Vector2 size, UnityAction onClose)
            => Window(panelRoot, title, size, onClose, out _, out _);

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Money(int amount) => amount < 0 ? $"-${-amount:N0}" : $"${amount:N0}";

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
