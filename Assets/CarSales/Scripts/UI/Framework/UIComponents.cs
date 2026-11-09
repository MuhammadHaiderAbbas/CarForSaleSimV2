using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Component library (spec §6.14.1): tabs / segmented control, sortable table, modal dialog, stat tile,
    /// mini bar chart, empty state, badge. Built from the same theme tokens as the rest of UIFactory.
    /// </summary>
    public static partial class UIFactory
    {
        // ------------------------------------------------------------------ TABS / SEGMENTED
        public class TabBar
        {
            public readonly List<Button> buttons = new List<Button>();
            public readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
            public int Index { get; private set; } = -1;
            public event Action<int> OnChanged;

            public void Select(int i, bool notify = true)
            {
                if (i < 0 || i >= buttons.Count) return;
                Index = i;
                for (int k = 0; k < buttons.Count; k++) SetButtonColor(buttons[k], k == i ? Accent : CardLight);
                if (notify) OnChanged?.Invoke(i);
            }

            public void SetLabel(int i, string text) { if (i >= 0 && i < labels.Count) labels[i].text = text; }
            public void SetInteractable(int i, bool on) { if (i >= 0 && i < buttons.Count) buttons[i].interactable = on; }
        }

        /// <summary>A row of tab buttons. Width 0 = buttons share the row equally.</summary>
        public static TabBar Tabs(Transform parent, string[] names, Action<int> onChanged, float height = 44f, float width = 0f, float fontSize = 17f)
        {
            var bar = new TabBar();
            var row = HRow(parent, height, 6f, "Tabs", width <= 0f);
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = Btn(row, names[i], CardLight, () => bar.Select(index), out var label, height, fontSize);
                if (width > 0f) LE(b, h: height, w: width, flexW: 0);
                bar.buttons.Add(b);
                bar.labels.Add(label);
            }
            if (width > 0f) Spacer(row, 0f, 1f);
            if (onChanged != null) bar.OnChanged += onChanged;
            return bar;
        }

        // ------------------------------------------------------------------ TABLE
        public class TableColumn
        {
            public string header;
            public float width;          // 0 = flexible
            public TextAlignmentOptions align = TextAlignmentOptions.Left;
            public TableColumn(string header, float width = 0f, TextAlignmentOptions align = TextAlignmentOptions.Left)
            { this.header = header; this.width = width; this.align = align; }
        }

        /// <summary>
        /// Sortable table: click a header to sort (click again to reverse). Rows are plain strings (rich text
        /// allowed); sort keys are separate so money/dates sort as numbers. Shows at most <see cref="MaxRows"/>.
        /// </summary>
        public class Table
        {
            public const int MaxRows = 250;
            public RectTransform content;
            public TextMeshProUGUI footer;
            readonly List<TableColumn> columns;
            readonly List<TextMeshProUGUI> headerLabels = new List<TextMeshProUGUI>();
            readonly List<Row> rows = new List<Row>();
            int sortColumn = -1;
            bool descending;
            float rowHeight;
            string emptyText = "Nothing to show.";

            public class Row
            {
                public string[] cells;
                public IComparable[] keys;
                public Color? tint;
                public UnityAction onClick;
            }

            public Table(List<TableColumn> columns) { this.columns = columns; }

            internal void BuildHeader(Transform parent, float headerHeight)
            {
                var head = Panel(parent, "TableHeader", HeaderBg, 2.5f);
                LE(head, h: headerHeight);
                var h = Horizontal(head, 0, 8f);
                h.padding = new RectOffset(14, 26, 0, 0);
                for (int i = 0; i < columns.Count; i++)
                {
                    int col = i;
                    var b = Btn(head.transform, columns[i].header, Clear, () => SortBy(col), out var label, headerHeight, 15f);
                    label.color = TextDim;
                    label.alignment = columns[i].align;
                    ApplyWidth(b, columns[i]);
                    headerLabels.Add(label);
                }
            }

            static void ApplyWidth(Component c, TableColumn col)
            {
                if (col.width > 0f) LE(c, w: col.width, flexW: 0);
                else LE(c, flexW: 1);
            }

            public void SetEmptyText(string text) => emptyText = text;

            public void SetRows(List<Row> newRows)
            {
                rows.Clear();
                rows.AddRange(newRows);
                Render();
            }

            public void SortBy(int column)
            {
                if (sortColumn == column) descending = !descending;
                else { sortColumn = column; descending = false; }
                Render();
            }

            /// <summary>Sets the sort without reversing (for the initial order).</summary>
            public void SetSort(int column, bool desc) { sortColumn = column; descending = desc; }

            void Render()
            {
                for (int i = 0; i < headerLabels.Count; i++)
                    headerLabels[i].text = columns[i].header + (i == sortColumn ? (descending ? "  v" : "  ^") : "");

                var list = new List<Row>(rows);
                if (sortColumn >= 0)
                {
                    int c = sortColumn;
                    list.Sort((a, b) =>
                    {
                        var ka = a.keys != null && c < a.keys.Length ? a.keys[c] : (a.cells.Length > c ? a.cells[c] : "");
                        var kb = b.keys != null && c < b.keys.Length ? b.keys[c] : (b.cells.Length > c ? b.cells[c] : "");
                        int r = ka == null ? (kb == null ? 0 : -1) : kb == null ? 1 : ka.GetType() == kb.GetType() ? ka.CompareTo(kb) : string.CompareOrdinal(ka.ToString(), kb.ToString());
                        return descending ? -r : r;
                    });
                }

                ClearChildren(content);
                if (list.Count == 0)
                {
                    EmptyState(content, emptyText, null);
                    if (footer) footer.text = "";
                    return;
                }
                int shown = Mathf.Min(list.Count, MaxRows);
                for (int i = 0; i < shown; i++) RenderRow(list[i], i);
                if (footer) footer.text = list.Count > shown ? $"Showing {shown} of {list.Count} rows" : $"{list.Count} rows";
            }

            void RenderRow(Row r, int index)
            {
                Color bg = r.tint ?? (index % 2 == 0 ? CardBg : CardLight);
                var row = Panel(content, "Row", bg, 2.5f);
                LE(row, h: rowHeight);
                var h = Horizontal(row, 0, 8f);
                h.padding = new RectOffset(14, 14, 0, 0);
                if (r.onClick != null)
                {
                    var b = row.gameObject.AddComponent<Button>();
                    b.targetGraphic = row;
                    var cb = b.colors; cb.highlightedColor = UITheme.Current.buttonHover; cb.pressedColor = UITheme.Current.buttonPressed; b.colors = cb;
                    b.onClick.AddListener(r.onClick);
                }
                for (int i = 0; i < columns.Count; i++)
                {
                    var t = Text(row.transform, i < r.cells.Length ? r.cells[i] : "", 16f, TextMain, columns[i].align, i == 0);
                    t.overflowMode = TextOverflowModes.Ellipsis;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    ApplyWidth(t, columns[i]);
                }
            }

            internal void SetRowHeight(float h) => rowHeight = h;
        }

        public static Table MakeTable(Transform parent, List<TableColumn> columns, float rowHeight = 40f, bool withFooter = true)
        {
            var table = new Table(columns);
            table.SetRowHeight(rowHeight);
            table.BuildHeader(parent, 38f);
            table.content = ScrollList(parent, 2f, "TableRows");
            if (withFooter)
            {
                table.footer = Text(parent, "", 14, TextDim, TextAlignmentOptions.Right);
                LE(table.footer, h: 20);
            }
            return table;
        }

        // ------------------------------------------------------------------ MODAL
        /// <summary>
        /// Centred dialog over everything, blocking clicks behind it. Returns the dialog root (destroyed when a
        /// button is pressed). Buttons: (label, colour, action) – the action may be null for "Cancel".
        /// </summary>
        public static GameObject Modal(Transform canvas, string title, string body, params (string label, Color color, UnityAction action)[] buttons)
        {
            var root = Rect("Modal", canvas);
            Stretch(root);
            var block = root.gameObject.AddComponent<Image>();
            block.color = Backdrop;
            var box = Panel(root, "Dialog", WindowBg, 1f);
            Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 0f));
            box.gameObject.AddComponent<Outline>().effectColor = Line;
            var v = Vertical(box, 28, 14f);
            var fit = box.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Text(box.transform, title, 26, TextMain, TextAlignmentOptions.Left, true);
            LE(t, h: 36);
            Fill(box.transform, "Line", Accent).gameObject.AddComponent<LayoutElement>().preferredHeight = 2;
            var b = Text(box.transform, body, 18, TextDim, TextAlignmentOptions.TopLeft);
            b.textWrappingMode = TextWrappingModes.Normal;
            var row = HRow(box.transform, 50, 12, "Buttons", true);
            var go = root.gameObject;
            // Opened from the world (no menu open): act like a menu, so the cursor is free and the player stands still.
            var ui = CarSalesServices.UI;
            bool standalone = ui && canvas == ui.transform && !ui.IsMenuOpen;
            foreach (var (label, color, action) in buttons)
            {
                var a = action;
                Btn(row, label, color, () =>
                {
                    if (standalone && ui && ui.OpenPanel == go) ui.Close();
                    UnityEngine.Object.Destroy(go);
                    a?.Invoke();
                }, 50f, 19f);
            }
            root.SetAsLastSibling();
            if (standalone) ui.Open(go);
            else UITransition.Play(go);
            return go;
        }

        /// <summary>Yes/No confirmation on the game's UI canvas.</summary>
        public static GameObject Confirm(string title, string body, string okLabel, Color okColor, UnityAction onOk)
        {
            var ui = CarSalesServices.UI;
            if (!ui) { onOk?.Invoke(); return null; }
            return Modal(ui.transform, title, body, ("Cancel", Neutral, null), (okLabel, okColor, onOk));
        }

        // ------------------------------------------------------------------ STAT TILE / BADGE / EMPTY
        public class StatTileRefs { public Image panel; public TextMeshProUGUI label, value, sub; }

        public static StatTileRefs StatTile(Transform parent, string label, float height = 112f)
        {
            var card = Panel(parent, "Stat_" + label, CardBg, 1.4f);
            LE(card, h: height, flexW: 1);
            var v = Vertical(card, 16, 2f);
            v.padding = new RectOffset(18, 14, 14, 12);
            var refs = new StatTileRefs { panel = card };
            refs.label = Text(card.transform, label.ToUpperInvariant(), 13, TextDim, TextAlignmentOptions.Left, true);
            refs.value = Text(card.transform, "-", 30, TextMain, TextAlignmentOptions.Left, true);
            refs.sub = Text(card.transform, "", 14, TextDim, TextAlignmentOptions.Left);
            return refs;
        }

        /// <summary>Small status pill with a text label (status is never shown by colour alone).</summary>
        public static TextMeshProUGUI Badge(Transform parent, string text, Color color, float fontSize = 13f)
        {
            Chip(parent, text, color, out var label, fontSize);
            label.color = ContrastText(color);
            return label;
        }

        public static RectTransform EmptyState(Transform parent, string title, string hint)
        {
            var box = Rect("Empty", parent);
            var v = Vertical(box, 24, 6f, TextAnchor.MiddleCenter);
            LE(box, h: string.IsNullOrEmpty(hint) ? 90 : 120, flexW: 1);
            var t = Text(box, title, 20, TextDim, TextAlignmentOptions.Center, true);
            LE(t, h: 30);
            if (!string.IsNullOrEmpty(hint)) { var h = Text(box, hint, 15, TextFaint, TextAlignmentOptions.Center); LE(h, h: 40); }
            return box;
        }

        public static TextMeshProUGUI SectionTitle(Transform parent, string text)
        {
            var t = Text(parent, text.ToUpperInvariant(), 14, Accent, TextAlignmentOptions.Left, true);
            LE(t, h: 22);
            return t;
        }

        // ------------------------------------------------------------------ MINI CHART
        /// <summary>Bar chart for a short series (e.g. profit per day). Negative values go below the baseline.</summary>
        public class MiniChart
        {
            public RectTransform area;
            public TextMeshProUGUI caption;

            public void Set(IList<float> values, IList<string> tips = null)
            {
                ClearChildren(area);
                if (values == null || values.Count == 0) return;
                float max = 0.0001f, min = 0f;
                foreach (var v in values) { max = Mathf.Max(max, v); min = Mathf.Min(min, v); }
                float range = max - min;
                float zero = -min / range;   // baseline position 0..1
                float w = 1f / values.Count;
                var axis = Fill(area, "Axis", Line);
                axis.rectTransform.anchorMin = new Vector2(0f, zero); axis.rectTransform.anchorMax = new Vector2(1f, zero);
                axis.rectTransform.sizeDelta = new Vector2(0f, 1f);
                for (int i = 0; i < values.Count; i++)
                {
                    float v = values[i];
                    var bar = Panel(area, "Bar" + i, v >= 0f ? Good : Bad, 6f);
                    bar.raycastTarget = false;
                    float y0 = v >= 0f ? zero : zero + v / range, y1 = v >= 0f ? zero + v / range : zero;
                    bar.rectTransform.anchorMin = new Vector2(i * w + w * 0.15f, y0);
                    bar.rectTransform.anchorMax = new Vector2((i + 1) * w - w * 0.15f, Mathf.Max(y1, y0 + 0.01f));
                    bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
                }
            }
        }

        public static MiniChart Chart(Transform parent, string caption, float height = 120f)
        {
            var card = Panel(parent, "Chart", CardBg, 1.4f);
            LE(card, h: height + 40f, flexW: 1);
            var v = Vertical(card, 12, 4f);
            v.padding = new RectOffset(16, 16, 10, 12);
            var chart = new MiniChart { caption = Text(card.transform, caption, 14, TextDim, TextAlignmentOptions.Left, true) };
            LE(chart.caption, h: 20);
            chart.area = Rect("Bars", card.transform);
            LE(chart.area, h: height, flexW: 1);
            return chart;
        }
    }
}
