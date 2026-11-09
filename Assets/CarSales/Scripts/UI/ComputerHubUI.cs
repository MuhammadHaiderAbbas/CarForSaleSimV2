using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// The dealer computer as a desktop (spec §6.14.2): app icons, widgets (clock, today, goal, messages) and a
    /// taskbar. Apps open in a window on the desktop; Esc / B in an app returns to the desktop.
    /// Pages are the existing <see cref="ComputerPage"/>s, so <c>ui.inventory.Show()</c> etc. still open them directly.
    /// </summary>
    public class ComputerHubUI : UIPanel, IBackHandler
    {
        public HomePageUI Home { get; private set; }
        public MarketplaceUI Marketplace { get; private set; }
        public InventoryUI Inventory { get; private set; }
        public MessagesUI Messages { get; private set; }
        public UpgradesUI Upgrades { get; private set; }
        public BankUI Bank { get; private set; }
        public StatsUI Stats { get; private set; }
        public RealEstateUI RealEstate { get; private set; }
        public AccountsPageUI Accounts { get; private set; }
        public WorkshopPageUI Workshop { get; private set; }
        public MarketPageUI MarketApp { get; private set; }
        public CrmPageUI Customers { get; private set; }
        public StaffPageUI Staff { get; private set; }
        public BusinessPageUI Business { get; private set; }

        TextMeshProUGUI moneyText, titleText, clockText, taskClock, taskApp, todayText, goalText, mailText, unreadBadge;
        RectTransform content, appWindow, desktop;
        ComputerPage current;
        readonly List<ComputerPage> pages = new List<ComputerPage>();
        readonly Dictionary<ComputerPage, TextMeshProUGUI> tileBadges = new Dictionary<ComputerPage, TextMeshProUGUI>();
        float refreshTimer;

        public override void Build()
        {
            var wall = Root.gameObject.AddComponent<Image>();
            wall.color = UIFactory.WindowBg;

            // Wallpaper decoration
            var glowA = UIFactory.SpriteImage(Root, "GlowA", UIFactory.Circle, UIFactory.WithAlpha(UIFactory.Accent, 0.14f), 1400f);
            UIFactory.Place(glowA.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-200f, -120f), new Vector2(1400f, 1400f));
            var glowB = UIFactory.SpriteImage(Root, "GlowB", UIFactory.Circle, UIFactory.WithAlpha(UIFactory.Special, 0.1f), 900f);
            UIFactory.Place(glowB.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(160f, 160f), new Vector2(900f, 900f));
            var mark = UIFactory.Text(Root, "AUTO WORLD", 150, UIFactory.WithAlpha(UIFactory.TextMain, 0.05f), TextAlignmentOptions.BottomRight, true, "Watermark");
            UIFactory.Place(mark.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 70f), new Vector2(1300f, 180f));

            BuildDesktop();
            BuildTaskbar();
            BuildAppWindow();

            Home = AddPage<HomePageUI>("Dashboard");
            Marketplace = AddPage<MarketplaceUI>("Marketplace");
            Inventory = AddPage<InventoryUI>("My Cars");
            Messages = AddPage<MessagesUI>("Messages");
            Upgrades = AddPage<UpgradesUI>("Upgrades");
            Bank = AddPage<BankUI>("Bank & Loans");
            RealEstate = AddPage<RealEstateUI>("Real Estate");
            Stats = AddPage<StatsUI>("Statistics");
            Accounts = AddPage<AccountsPageUI>("Accounts");
            Workshop = AddPage<WorkshopPageUI>("Workshop");
            MarketApp = AddPage<MarketPageUI>("Market & competitors");
            Customers = AddPage<CrmPageUI>("Customers");
            Staff = AddPage<StaffPageUI>("Staff");
            Business = AddPage<BusinessPageUI>("Business");

            BuildIcons();
        }

        // ------------------------------------------------------------------ DESKTOP
        RectTransform iconGrid;

        void BuildDesktop()
        {
            desktop = UIFactory.Rect("Desktop", Root);
            UIFactory.Stretch(desktop);
            desktop.offsetMin = new Vector2(48f, 90f);
            desktop.offsetMax = new Vector2(-48f, -48f);

            iconGrid = UIFactory.Rect("Icons", desktop);
            UIFactory.Place(iconGrid, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(950f, 760f));
            var grid = iconGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(170f, 150f);
            grid.spacing = new Vector2(16f, 16f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;   // 17 apps

            // Widgets (right)
            var widgets = UIFactory.Rect("Widgets", desktop);
            UIFactory.Place(widgets, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(420f, 760f));
            UIFactory.Vertical(widgets, 0, 16f);

            var clock = Widget(widgets, "Clock", 150f);
            clockText = UIFactory.Text(clock, "", 54, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(clockText, h: 110);

            var today = Widget(widgets, "Today", 150f);
            UIFactory.SectionTitle(today, "Today");
            todayText = UIFactory.Text(today, "", 17, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            UIFactory.LE(todayText, flexH: 1);

            var goal = Widget(widgets, "Goal", 130f);
            UIFactory.SectionTitle(goal, "Current goal");
            goalText = UIFactory.Text(goal, "", 17, UIFactory.TextMain, TextAlignmentOptions.TopLeft, true);
            UIFactory.LE(goalText, flexH: 1);

            var mail = Widget(widgets, "Mail", 110f);
            UIFactory.SectionTitle(mail, "Messages");
            mailText = UIFactory.Text(mail, "", 17, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            UIFactory.LE(mailText, flexH: 1);
            var mailBtn = mail.gameObject.AddComponent<Button>();
            mailBtn.targetGraphic = mail.GetComponent<Image>();
            mailBtn.onClick.AddListener(() => ShowPage(Messages));
        }

        static RectTransform Widget(Transform parent, string name, float height)
        {
            var p = UIFactory.Panel(parent, name, UIFactory.WithAlpha(UIFactory.CardBg, 0.88f), 1.4f);
            UIFactory.LE(p, h: height);
            var v = UIFactory.Vertical(p, 18, 4f);
            v.padding = new RectOffset(20, 18, 14, 14);
            return p.rectTransform;
        }

        void BuildIcons()
        {
            Tile(Loc.T("app.dashboard", "Dashboard"), "D", UIFactory.Accent, () => ShowPage(Home), Home);
            Tile(Loc.T("app.mycars", "My Cars"), "S", UIFactory.Good, () => ShowPage(Inventory), Inventory);
            Tile(Loc.T("app.marketplace", "Marketplace"), "M", UIFactory.Info, () => ShowPage(Marketplace), Marketplace);
            Tile(Loc.T("app.auctions", "Auctions"), "A", UIFactory.Bad, () => CarSalesServices.UI.auction.Show());
            Tile(Loc.T("app.messages", "Messages"), "@", UIFactory.Gold, () => ShowPage(Messages), Messages);
            Tile(Loc.T("app.bank", "Bank"), "B", UIFactory.Personal, () => ShowPage(Bank), Bank);
            Tile(Loc.T("app.accounts", "Accounts"), "%", UIFactory.Home, () => ShowPage(Accounts), Accounts);
            Tile(Loc.T("app.realestate", "Real Estate"), "R", UIFactory.Special, () => ShowPage(RealEstate), RealEstate);
            Tile(Loc.T("app.upgrades", "Upgrades"), "+", UIFactory.Gold, () => ShowPage(Upgrades), Upgrades);
            Tile(Loc.T("app.statistics", "Statistics"), "#", UIFactory.AccentDark, () => ShowPage(Stats), Stats);
            Tile(Loc.T("app.settings", "Settings"), "*", UIFactory.Neutral, () => CarSalesServices.UI.settings.Show());
            Tile(Loc.T("app.workshop", "Workshop"), "W", UIFactory.Warn, () => ShowPage(Workshop));
            Tile(Loc.T("app.customers", "Customers"), "C", UIFactory.Personal, () => ShowPage(Customers));
            Tile(Loc.T("app.staff", "Staff"), "T", UIFactory.Good, () => ShowPage(Staff));
            Tile(Loc.T("app.market", "Market"), "V", UIFactory.Info, () => ShowPage(MarketApp));
            Tile(Loc.T("app.business", "Business"), "L", UIFactory.Gold, () => ShowPage(Business));
            Tile(Loc.T("app.classifieds", "Classifieds"), "K", UIFactory.Gold, () => { ShowPage(MarketApp); MarketApp.ShowTab(3); });
        }

        void Tile(string label, string glyph, Color color, UnityAction onClick, ComputerPage page = null, bool soon = false)
        {
            var bg = UIFactory.Panel(iconGrid, "App_" + label, UIFactory.WithAlpha(UIFactory.CardBg, 0.6f), 1.6f);
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            var cb = b.colors; cb.highlightedColor = UITheme.Current.buttonHover; cb.pressedColor = UITheme.Current.buttonPressed; b.colors = cb;
            b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => CarSalesServices.PlaySfx(Sfx.Click, 0.5f));
            var v = UIFactory.Vertical(bg, 10, 8f, TextAnchor.MiddleCenter);
            v.childForceExpandWidth = false;
            v.childControlWidth = false;
            var icon = UIFactory.Panel(bg.transform, "Icon", soon ? UIFactory.WithAlpha(color, 0.35f) : color, 1.2f);
            icon.rectTransform.sizeDelta = new Vector2(72f, 72f);
            UIFactory.LE(icon, h: 72, w: 72, flexW: 0);
            var g = UIFactory.Text(icon.transform, glyph, 34, UIFactory.ContrastText(color), TextAlignmentOptions.Center, true);
            UIFactory.Stretch(g.rectTransform);
            var t = UIFactory.Text(bg.transform, label, 17, soon ? UIFactory.TextDim : UIFactory.TextMain, TextAlignmentOptions.Center, true);
            t.rectTransform.sizeDelta = new Vector2(160f, 24f);
            UIFactory.LE(t, h: 24, w: 160);
            if (page != null || soon)
            {
                UIFactory.Chip(bg.transform, soon ? "SOON" : "", soon ? UIFactory.Neutral : UIFactory.Bad, out var badge, 12f);
                var chipRt = (RectTransform)badge.transform.parent;
                chipRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UIFactory.Place(chipRt, Vector2.one, Vector2.one, new Vector2(-10f, -10f), new Vector2(40f, 22f));
                badge.color = UIFactory.TextMain;
                chipRt.gameObject.SetActive(soon);
                if (page != null) tileBadges[page] = badge;
            }
        }

        void Soon(string label, string glyph, int phase) =>
            Tile(label, glyph, UIFactory.Neutral, () => UIFactory.Confirm(label, $"The {label} app arrives with a later update (V5 phase {phase}). " +
                "Until then use the other apps on this desktop.", "OK", UIFactory.Accent, null), null, true);

        // ------------------------------------------------------------------ TASKBAR
        void BuildTaskbar()
        {
            var bar = UIFactory.Fill(Root, "Taskbar", UIFactory.HeaderBg);
            bar.raycastTarget = true;
            var rt = bar.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 62f); rt.anchoredPosition = Vector2.zero;
            var h = UIFactory.Horizontal(bar, 0, 14f);
            h.padding = new RectOffset(16, 16, 8, 8);

            var start = UIFactory.Btn(rt, "AUTO WORLD", UIFactory.Accent, () => Show(), 46f, 18f);
            UIFactory.LE(start, w: 180, flexW: 0);
            taskApp = UIFactory.Text(rt, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(taskApp, flexW: 1);
            unreadBadge = UIFactory.Text(rt, "", 17, UIFactory.Gold, TextAlignmentOptions.Right, true);
            UIFactory.LE(unreadBadge, w: 200, flexW: 0);
            moneyText = UIFactory.Text(rt, "$0", 24, UIFactory.Good, TextAlignmentOptions.Right, true);
            UIFactory.LE(moneyText, w: 220, flexW: 0);
            taskClock = UIFactory.Text(rt, "", 18, UIFactory.TextDim, TextAlignmentOptions.Right, true);
            UIFactory.LE(taskClock, w: 150, flexW: 0);
            var off = UIFactory.Btn(rt, "Log off", UIFactory.AccentDark, CloseAll, 46f, 16f);
            UIFactory.LE(off, w: 110, flexW: 0);
        }

        // ------------------------------------------------------------------ APP WINDOW
        void BuildAppWindow()
        {
            var win = UIFactory.Panel(Root, "AppWindow", UIFactory.WindowBg, 1f);
            appWindow = win.rectTransform;
            appWindow.anchorMin = Vector2.zero; appWindow.anchorMax = Vector2.one;
            appWindow.offsetMin = new Vector2(70f, 84f); appWindow.offsetMax = new Vector2(-70f, -30f);
            win.gameObject.AddComponent<Outline>().effectColor = UIFactory.Line;
            win.gameObject.AddComponent<Shadow>().effectColor = UITheme.Current.shadow;

            var head = UIFactory.Panel(win.transform, "TitleBar", UIFactory.HeaderBg, 1f);
            var hr = head.rectTransform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = Vector2.one; hr.pivot = new Vector2(0.5f, 1f);
            hr.sizeDelta = new Vector2(0f, 58f); hr.anchoredPosition = Vector2.zero;
            var line = UIFactory.Fill(win.transform, "TitleLine", UIFactory.Accent);
            line.rectTransform.anchorMin = new Vector2(0f, 1f); line.rectTransform.anchorMax = Vector2.one;
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.anchoredPosition = new Vector2(0f, -58f); line.rectTransform.sizeDelta = new Vector2(0f, 2f);

            var back = UIFactory.Btn(hr, "< Desktop", UIFactory.Ghost, () => Show(), 38f, 16f);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(130f, 38f));
            titleText = UIFactory.Text(hr, "", 24, UIFactory.TextMain, TextAlignmentOptions.Left, true, "Title");
            UIFactory.Stretch(titleText.rectTransform);
            titleText.rectTransform.offsetMin = new Vector2(160f, 0f);
            var close = UIFactory.Btn(hr, "X", UIFactory.Ghost, CloseAll, 38f, 18f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(40f, 38f));

            content = UIFactory.Rect("Content", win.transform);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(26f, 22f); content.offsetMax = new Vector2(-26f, -80f);
            appWindow.gameObject.SetActive(false);
        }

        T AddPage<T>(string title) where T : ComputerPage
        {
            var rt = UIFactory.Rect(title, content);
            UIFactory.Stretch(rt);
            var page = rt.gameObject.AddComponent<T>();
            page.pageTitle = title;
            page.Build();
            rt.gameObject.SetActive(false);
            pages.Add(page);
            return page;
        }

        /// <summary>Opens the computer on its desktop.</summary>
        public void Show()
        {
            if (!gameObject.activeSelf || CarSalesServices.UI.OpenPanel != gameObject) OpenSelf();
            current = null;
            appWindow.gameObject.SetActive(false);
            desktop.gameObject.SetActive(true);
            taskApp.text = "Desktop";
            RefreshDesktop();
        }

        public void ShowPage(ComputerPage page)
        {
            if (!gameObject.activeSelf || CarSalesServices.UI.OpenPanel != gameObject) OpenSelf();
            current = page;
            desktop.gameObject.SetActive(false);
            appWindow.gameObject.SetActive(true);
            foreach (var p in pages) p.gameObject.SetActive(p == page);
            titleText.text = page.pageTitle;
            taskApp.text = page.pageTitle;
            UITransition.Play(appWindow.gameObject);
            page.Refresh();
        }

        public bool HandleBack()
        {
            if (current == null) return false;
            Show();
            return true;
        }

        public void RefreshCurrent() { if (current) current.Refresh(); }

        void RefreshDesktop()
        {
            var day = CarSalesServices.DayCycle;
            if (!day) return;
            float hod = day.HourOfDay;
            int h = Mathf.FloorToInt(hod), m = Mathf.FloorToInt((hod - h) * 60f);
            clockText.text = $"{h:00}:{m:00}\n<size=35%><color={UIFactory.Hex(UIFactory.TextDim)}>{CarSales.Foundation.GameCalendar.WeekdayShort(day.CurrentDay)}, day {day.CurrentDay}  -  " +
                             (day.IsOpen ? "dealerships open" : "dealerships closed") + "</color></size>";
            var t = day.Today;
            todayText.text = $"Sold <b>{t.carsSold}</b>   Bought <b>{t.carsBought}</b>\nRevenue <b>{UIFactory.Money(t.revenue)}</b>\n" +
                             $"Expenses <b>{UIFactory.Money(t.expenses)}</b>   Profit <b><color={UIFactory.Hex(t.profit >= 0 ? UIFactory.Good : UIFactory.Bad)}>{UIFactory.Money(t.profit)}</color></b>";
            goalText.text = CarSalesServices.Progress ? CarSalesServices.Progress.CurrentObjectiveText : "";
            int unread = CarSalesServices.Messages ? CarSalesServices.Messages.UnreadCount : 0;
            mailText.text = unread > 0 ? $"<color={UIFactory.Hex(UIFactory.Gold)}><b>{unread} unread</b></color> - sellers and buyers are waiting." : "No new messages.";
        }

        void Update()
        {
            moneyText.text = UIFactory.Money(CarSalesServices.Economy.Money);
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.5f;
            var day = CarSalesServices.DayCycle;
            if (day)
            {
                float hod = day.HourOfDay;
                taskClock.text = $"{CarSales.Foundation.GameCalendar.WeekdayShort(day.CurrentDay)} {Mathf.FloorToInt(hod):00}:{Mathf.FloorToInt((hod % 1f) * 60f):00}";
            }
            int unread = CarSalesServices.Messages ? CarSalesServices.Messages.UnreadCount : 0;
            unreadBadge.text = unread > 0 ? $"{unread} new message{(unread == 1 ? "" : "s")}" : "";
            if (tileBadges.TryGetValue(Messages, out var b))
            {
                b.text = unread.ToString();
                b.transform.parent.gameObject.SetActive(unread > 0);
            }
            if (desktop.gameObject.activeSelf) RefreshDesktop();
        }
    }
}
