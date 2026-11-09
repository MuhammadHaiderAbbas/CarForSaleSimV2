using System.Linq;
using CarSales.Foundation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// The phone (P): a home screen with the time, money, notifications and apps. Tow, Taxi, Bank and Messages run on
    /// the phone; Map, Stock, Market and Settings open their full screens. (Spec §6.14.3, first version.)
    /// </summary>
    public class PhoneUI : UIPanel, IBackHandler
    {
        enum App { Home, Tow, Taxi, Bank, Messages }

        App app;
        CarDisplay selected;
        RectTransform screen;
        TextMeshProUGUI clockText, statusText, titleText;
        Button backButton;
        float statusTimer;

        public override void Build()
        {
            var dim = UIFactory.Fill(Root, "Backdrop", UIFactory.Backdrop);
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            // Phone body: bezel, status bar, screen, home bar.
            var phone = UIFactory.Panel(Root, "Phone", UIFactory.HeaderBg, 3.2f);
            UIFactory.Place(phone.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(460f, 900f));
            phone.gameObject.AddComponent<Outline>().effectColor = UIFactory.Line;
            var v = UIFactory.Vertical(phone, 18, 10f);
            v.padding = new RectOffset(18, 18, 16, 14);

            var status = UIFactory.HRow(phone.transform, 26f, 8f, "StatusBar");
            clockText = UIFactory.Text(status, "", 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            statusText = UIFactory.Text(status, "", 15, UIFactory.TextDim, TextAlignmentOptions.Right);

            var titleRow = UIFactory.HRow(phone.transform, 40f, 8f, "TitleRow");
            backButton = UIFactory.Btn(titleRow, "<", UIFactory.Neutral, () => Open(App.Home), 38f, 22f);
            UIFactory.LE(backButton, w: 44);
            titleText = UIFactory.Text(titleRow, "", 22, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(titleText, flexW: 1);

            var screenBg = UIFactory.Panel(phone.transform, "Screen", UIFactory.WindowBg, 2.2f);
            UIFactory.LE(screenBg, flexH: 1);
            UIFactory.Vertical(screenBg, 12, 0f);
            screen = UIFactory.ScrollList(screenBg.transform, 8f, "ScreenScroll");

            var bottom = UIFactory.HRow(phone.transform, 44f, 10f, "HomeBar");
            UIFactory.Btn(bottom, "Home", UIFactory.Neutral, () => Open(App.Home), 40f, 17f);
            UIFactory.Btn(bottom, "Close (P)", UIFactory.Neutral, CloseAll, 40f, 17f);
        }

        public void Show() { selected = null; OpenSelf(); Open(App.Home); }

        /// <summary>Opens the Tow app with a car already chosen (car menu → "Tow somewhere...").</summary>
        public void ShowTow(CarDisplay car)
        {
            selected = car;
            OpenSelf();
            Open(App.Tow);
        }

        void Update()
        {
            statusTimer -= Time.unscaledDeltaTime;
            if (statusTimer > 0f) return;
            statusTimer = 0.5f;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var day = CarSalesServices.DayCycle;
            if (!clockText || !day) return;
            float h = day.HourOfDay;
            clockText.text = $"{Mathf.FloorToInt(h):00}:{Mathf.FloorToInt((h % 1f) * 60f):00}";
            statusText.text = $"{UIFactory.Money(CarSalesServices.Economy.Money)}   Energy {day.Energy:0}%";
        }

        /// <summary>Esc / B inside an app returns to the phone's home screen.</summary>
        public bool HandleBack()
        {
            if (app == App.Home) return false;
            Open(App.Home);
            return true;
        }

        void Open(App target)
        {
            app = target;
            backButton.gameObject.SetActive(app != App.Home);
            titleText.text = app == App.Home ? "" : app.ToString();
            UpdateStatus();
            UIFactory.ClearChildren(screen);
            switch (app)
            {
                case App.Tow: BuildTow(); break;
                case App.Taxi: BuildTaxi(); break;
                case App.Bank: BuildBank(); break;
                case App.Messages: BuildMessages(); break;
                default: BuildHome(); break;
            }
        }

        // ------------------------------------------------------------------ HOME
        void BuildHome()
        {
            var day = CarSalesServices.DayCycle;
            int d = day ? day.CurrentDay : 1;
            float h = day ? day.HourOfDay : 8f;
            Line($"<size=260%><b>{Mathf.FloorToInt(h):00}:{Mathf.FloorToInt((h % 1f) * 60f):00}</b></size>", 20, UIFactory.TextMain, 110f, TextAlignmentOptions.Center);
            Line($"{GameCalendar.WeekdayOf(d)} · Day {d} · Week {GameCalendar.WeekOf(d)}", 17, UIFactory.TextDim, 28f, TextAlignmentOptions.Center);

            var msgs = CarSalesServices.Messages;
            if (msgs && msgs.UnreadCount > 0)
            {
                Line("NOTIFICATIONS", 14, UIFactory.TextDim, 24f);
                foreach (var m in msgs.Messages.Where(x => !x.read).OrderByDescending(x => x.day).Take(3))
                {
                    var card = Card(58f, () => Open(App.Messages));
                    UIFactory.Text(card, $"<b>{m.title}</b>\n<size=85%>Day {m.day}</size>", 15, UIFactory.TextMain, TextAlignmentOptions.Left);
                }
            }

            Line("APPS", 14, UIFactory.TextDim, 24f);
            var grid = UIFactory.Rect("AppGrid", screen);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(118f, 104f);
            g.spacing = new Vector2(8f, 8f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            UIFactory.LE(grid, h: 4 * 104f + 3 * 8f);
            var ui = CarSalesServices.UI;
            int unread = msgs ? msgs.UnreadCount : 0;
            AppIcon(grid, "Messages" + (unread > 0 ? $" ({unread})" : ""), "M", UIFactory.Accent, () => Open(App.Messages));
            AppIcon(grid, "Bank", "$", UIFactory.Good, () => Open(App.Bank));
            AppIcon(grid, "Tow truck", "T", UIFactory.Warn, () => Open(App.Tow));
            AppIcon(grid, "Taxi", "Tx", UIFactory.Gold, () => Open(App.Taxi));
            AppIcon(grid, "Map", "Mp", UIFactory.Info, () => ui.map.Show());
            AppIcon(grid, "My cars", "C", UIFactory.Special, () => ui.inventory.Show());
            AppIcon(grid, "Market", "Mk", UIFactory.AccentDark, () => ui.marketplace.Show());
            AppIcon(grid, "Real estate", "H", UIFactory.Home, () => ui.realEstate.Show());
            AppIcon(grid, "Auctions", "A", UIFactory.Bad, () => ui.auction.Show());
            AppIcon(grid, "Workshop", "W", UIFactory.Warn, () => ui.computerHub.Workshop.ShowBoard());
            AppIcon(grid, "Settings", "S", UIFactory.Neutral, () => ui.settings.Show());
        }

        void AppIcon(Transform grid, string label, string glyph, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var cell = UIFactory.Rect("App_" + label, grid);
            var img = cell.gameObject.AddComponent<Image>();
            img.color = UIFactory.Clear;
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(() => CarSalesServices.PlaySfx(Sfx.Click, 0.5f));
            UIFactory.Vertical(cell, 4, 4f, TextAnchor.UpperCenter).childForceExpandWidth = false;
            var icon = UIFactory.Icon(cell, color, glyph, 58f);
            icon.raycastTarget = false;
            var t = UIFactory.Text(cell, label, 14, UIFactory.TextMain, TextAlignmentOptions.Center);
            UIFactory.LE(t, h: 34, w: 114);
        }

        // ------------------------------------------------------------------ TOW
        void BuildTow()
        {
            if (!selected)
            {
                Line("Which car should the tow truck move?", 16, UIFactory.TextDim, 30f);
                foreach (var d in CarSalesServices.Vehicles.All.OrderByDescending(x => x.Data.personal))
                {
                    var car = d;
                    var card = Card(68f, () => { selected = car; Open(App.Tow); });
                    UIFactory.Text(card, (car.Data.personal ? "[Your car] " : "") + $"<b>{car.Definition.brand} {car.Definition.displayName}</b>\n" +
                                         $"<size=85%>{CarSalesServices.Vehicles.LocationText(car.Data)} · Fuel {car.Data.fuel:0}%</size>",
                                   15, UIFactory.TextMain, TextAlignmentOptions.Left);
                }
                return;
            }
            Line($"Tow the <b>{selected.Definition.displayName}</b> to:", 16, UIFactory.TextMain, 30f);
            foreach (var dest in CarSalesServices.Tow.Destinations())
            {
                var dst = dest;
                int cost = CarSalesServices.Tow.TowCost(selected.transform.position, dst.Position);
                var b = UIFactory.Btn(screen, $"{dst.name}  -  {UIFactory.Money(cost)}", UIFactory.CardLight, () => Tow(dst), 48f, 16f);
                b.interactable = CarSalesServices.Economy.CanAfford(cost);
            }
            UIFactory.Btn(screen, "Choose another car", UIFactory.Neutral, () => { selected = null; Open(App.Tow); }, 42f, 15f);
        }

        void Tow(TowService.Destination dest)
        {
            if (CarSalesServices.Tow.Tow(selected, dest, out string msg)) { CarSalesServices.UI.Success("Tow truck", msg); selected = null; }
            else CarSalesServices.UI.Error(msg);
            Open(App.Tow);
        }

        // ------------------------------------------------------------------ TAXI
        void BuildTaxi()
        {
            Line("A taxi takes you anywhere in town. It costs money and time.", 15, UIFactory.TextDim, 44f);
            var player = FindFirstObjectByType<PlayerInteractor>();
            Vector3 me = player ? player.transform.position : Vector3.zero;
            foreach (var place in MapPlace.All.Where(p => p && p.IsVisible).OrderBy(p => Vector3.Distance(me, p.transform.position)))
            {
                var p = place;
                int fare = CarSalesServices.Tow.TaxiCost(me, p.ArrivalPosition);
                var b = UIFactory.Btn(screen, $"{p.placeName}  -  {UIFactory.Money(fare)}", UIFactory.CardLight, () =>
                {
                    if (CarSalesServices.Tow.Taxi(p, out string msg)) { CloseAll(); CarSalesServices.UI.Success("Taxi", msg); }
                    else CarSalesServices.UI.Error(msg);
                }, 48f, 16f);
                b.interactable = CarSalesServices.Economy.CanAfford(fare);
            }
        }

        // ------------------------------------------------------------------ BANK
        void BuildBank()
        {
            var eco = CarSalesServices.Economy;
            var bank = CarSalesServices.Bank;
            var day = CarSalesServices.DayCycle;
            Line($"<size=180%><b>{UIFactory.Money(eco.Money)}</b></size>", 18, eco.Money < 0 ? UIFactory.Bad : UIFactory.Good, 64f, TextAlignmentOptions.Center);
            if (bank) Line($"Loan {UIFactory.Money(bank.Loan)} · interest tonight {UIFactory.Money(bank.TomorrowInterest)}", 15, UIFactory.TextDim, 26f, TextAlignmentOptions.Center);
            int today = day ? day.CurrentDay : 1;
            var s = eco.Ledger.Summarize(today, today);
            Line($"Today  <color={UIFactory.Hex(UIFactory.Good)}>+{UIFactory.Money(s.income)}</color>   <color={UIFactory.Hex(UIFactory.Bad)}>-{UIFactory.Money(s.expenses)}</color>",
                 16, UIFactory.TextMain, 28f, TextAlignmentOptions.Center);
            Line("RECENT", 14, UIFactory.TextDim, 24f);
            foreach (var t in eco.Ledger.Recent(8))
            {
                var row = UIFactory.HRow(screen, 40f, 8f, "Tx");
                var name = UIFactory.Text(row, $"{TxCategories.DisplayName(t.category)}\n<size=80%><color={UIFactory.Hex(UIFactory.TextDim)}>{t.memo}</color></size>",
                                          14, UIFactory.TextMain, TextAlignmentOptions.Left);
                name.overflowMode = TextOverflowModes.Ellipsis;
                UIFactory.LE(name, flexW: 1);
                UIFactory.LE(UIFactory.Text(row, (t.amount > 0 ? "+" : "") + UIFactory.Money(t.amount), 15, t.amount > 0 ? UIFactory.Good : UIFactory.Bad,
                                            TextAlignmentOptions.Right, true), w: 110);
            }
            UIFactory.Btn(screen, "Open full bank (loans, statement)", UIFactory.Accent, () => CarSalesServices.UI.bank.Show(), 46f, 16f);
        }

        // ------------------------------------------------------------------ MESSAGES
        void BuildMessages()
        {
            var msgs = CarSalesServices.Messages;
            if (!msgs || msgs.Messages.Count == 0) { Line("No messages.", 16, UIFactory.TextDim, 40f); return; }
            foreach (var m in msgs.Messages.OrderByDescending(x => x.day).Take(10))
            {
                var card = Card(64f, () => CarSalesServices.UI.messages.Show());
                UIFactory.Text(card, (m.read ? "" : "NEW  ") + $"<b>{m.title}</b>\n<size=85%>Day {m.day}" +
                                     (m.type == MessageType.SellerOffer && !m.resolved ? $" · offer {UIFactory.Money(m.offerPrice)}" : "") + "</size>",
                               15, m.read ? UIFactory.TextDim : UIFactory.TextMain, TextAlignmentOptions.Left);
            }
            UIFactory.Btn(screen, "Open all messages", UIFactory.Accent, () => CarSalesServices.UI.messages.Show(), 46f, 16f);
        }

        // ------------------------------------------------------------------ HELPERS
        void Line(string text, float size, Color color, float height, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var t = UIFactory.Text(screen, text, size, color, align);
            UIFactory.LE(t, h: height);
        }

        Transform Card(float height, UnityEngine.Events.UnityAction onClick)
        {
            var card = UIFactory.Panel(screen, "Card", UIFactory.CardBg, 1.8f);
            UIFactory.LE(card, h: height);
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            btn.onClick.AddListener(onClick);
            var v = UIFactory.Vertical(card, 10, 0f);
            v.childAlignment = TextAnchor.MiddleLeft;
            return card.transform;
        }
    }
}
