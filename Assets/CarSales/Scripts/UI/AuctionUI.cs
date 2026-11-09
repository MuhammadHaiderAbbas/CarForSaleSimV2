using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Auction terminal (desk, computer, phone): Calendar, Catalogue (lots with condition reports, max bids, watch),
    /// My bids, Invoices (pay, collection) and Accounts (register, buyer numbers, deposits). Live hall bidding is
    /// done in the hall itself (see AuctionHallHUD).
    /// </summary>
    public class AuctionUI : UIPanel
    {
        static readonly string[] Views = { "Calendar", "Catalogue", "My bids", "Invoices", "Accounts" };
        UIFactory.TabBar tabs, eventTabs;
        RectTransform calendarView, catalogueView, bidsView, invoicesView, accountsView;
        RectTransform calendarList, bidsList, invoicesList, accountsList, lotList, detail;
        UIFactory.Table bidsTable;
        readonly List<AuctionEvent> catalogueEvents = new List<AuctionEvent>();
        string selectedEvent, selectedLot;
        CarDisplay selectedDisplay;
        TMP_InputField maxInput;
        float refreshTimer;

        static AuctionManager A => CarSalesServices.Auction;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Auctions", new Vector2(1560f, 900f), CloseAll);
            UIFactory.Vertical(body, 0, 10f);
            tabs = UIFactory.Tabs(body, Views, _ => Refresh(), 42f, 180f, 16f);
            var host = UIFactory.Rect("Views", body);
            UIFactory.LE(host, flexH: 1, flexW: 1);

            calendarView = View(host, "Calendar");
            UIFactory.Vertical(calendarView, 0, 8f);
            calendarList = UIFactory.ScrollList(calendarView, 8f);

            catalogueView = View(host, "Catalogue");
            UIFactory.Vertical(catalogueView, 0, 8f);
            var evRow = UIFactory.Rect("EventRow", catalogueView);
            UIFactory.LE(evRow, h: 40);
            UIFactory.Vertical(evRow, 0, 0f);
            eventTabsHost = evRow;
            var split = UIFactory.Rect("Split", catalogueView);
            UIFactory.LE(split, flexH: 1, flexW: 1);
            UIFactory.Horizontal(split, 0, 16f, TextAnchor.UpperLeft).childForceExpandHeight = true;
            var left = UIFactory.VCol(split, 6f, "Lots", 560f);
            lotList = UIFactory.ScrollList(left, 6f, "LotList");
            var right = UIFactory.Panel(split, "Detail", UIFactory.CardBg, 1.4f);
            UIFactory.LE(right, flexW: 1, flexH: 1);
            UIFactory.Vertical(right, 16, 6f);
            detail = UIFactory.ScrollList(right.transform, 6f, "DetailScroll");

            bidsView = View(host, "Bids");
            UIFactory.Vertical(bidsView, 0, 4f);
            bidsTable = UIFactory.MakeTable(bidsView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Lot", 80f), new UIFactory.TableColumn("Car"), new UIFactory.TableColumn("Sale", 260f),
                new UIFactory.TableColumn("Current", 130f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Your max", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Status", 200f),
            }, 40f);
            bidsTable.SetEmptyText("No bids or watched lots. Open the Catalogue.");

            invoicesView = View(host, "Invoices");
            UIFactory.Vertical(invoicesView, 0, 8f);
            invoicesList = UIFactory.ScrollList(invoicesView, 8f);

            accountsView = View(host, "Accounts");
            UIFactory.Vertical(accountsView, 0, 8f);
            accountsList = UIFactory.ScrollList(accountsView, 8f);
            tabs.Select(0, false);
        }

        RectTransform eventTabsHost;

        static RectTransform View(Transform host, string name)
        {
            var v = UIFactory.Rect(name, host);
            UIFactory.Stretch(v);
            return v;
        }

        // ------------------------------------------------------------------ OPENING
        public void Show()
        {
            if (!A) { CarSalesServices.UI.Error("There are no auctions in this town."); return; }
            OpenSelf();
            Refresh();
        }

        /// <summary>Opens a lot (from the yard car, or the catalogue).</summary>
        public void ShowLot(string lotId, CarDisplay display = null)
        {
            if (!A) return;
            var lot = A.Lot(lotId);
            if (lot == null) return;
            selectedLot = lotId;
            selectedEvent = lot.eventId;
            selectedDisplay = display;
            OpenSelf();
            tabs.Select(1, false);
            Refresh();
        }

        void OnEnable() { if (A) A.OnChanged += MarkDirty; }
        void OnDisable() { if (A) A.OnChanged -= MarkDirty; }
        bool dirty;
        void MarkDirty() => dirty = true;

        void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (!dirty || refreshTimer > 0f) return;
            refreshTimer = 0.5f;
            dirty = false;
            // Don't rebuild while the player is typing a bid.
            if (maxInput && maxInput.isFocused) return;
            Refresh();
        }

        void Refresh()
        {
            if (!A || tabs == null) return;
            calendarView.gameObject.SetActive(tabs.Index == 0);
            catalogueView.gameObject.SetActive(tabs.Index == 1);
            bidsView.gameObject.SetActive(tabs.Index == 2);
            invoicesView.gameObject.SetActive(tabs.Index == 3);
            accountsView.gameObject.SetActive(tabs.Index == 4);
            int open = A.Invoices.Count(i => !i.paid && !i.cancelled);
            tabs.SetLabel(3, open > 0 ? $"Invoices ({open})" : "Invoices");
            switch (tabs.Index)
            {
                case 1: RenderCatalogue(); break;
                case 2: RenderBids(); break;
                case 3: RenderInvoices(); break;
                case 4: RenderAccounts(); break;
                default: RenderCalendar(); break;
            }
        }

        static string When(AuctionEvent e)
        {
            int d = e.day - CarRecords.Today;
            string day = d == 0 ? "Today" : d == 1 ? "Tomorrow" : $"{CarSales.Foundation.GameCalendar.WeekdayShort(e.day)} (day {e.day})";
            return e.online ? $"{day}, online, lots close 14:00-20:00" : $"{day} {e.startHour:00}:00 at the hall";
        }

        // ------------------------------------------------------------------ CALENDAR
        void RenderCalendar()
        {
            UIFactory.ClearChildren(calendarList);
            foreach (var e in A.Events.Where(x => !x.finished).OrderBy(x => x.day).ThenBy(x => x.startHour))
            {
                var venue = AuctionRules.Venue(e.venueId);
                var row = UIFactory.Panel(calendarList, e.id, UIFactory.CardBg, 1.8f);
                UIFactory.LE(row, h: 74);
                var h = UIFactory.Horizontal(row, 10, 12f);
                h.padding = new RectOffset(16, 12, 8, 8);
                h.childForceExpandHeight = false;
                UIFactory.Icon(row.transform, e.Type == AuctionType.Salvage ? UIFactory.Bad : e.online ? UIFactory.Info : UIFactory.Gold, e.online ? "@" : "A", 40f);
                var col = UIFactory.VCol(row.transform, 2f);
                UIFactory.Text(col, $"<b>{AuctionRules.TypeName(e.Type)}</b>  -  {venue.Name}", 18, UIFactory.TextMain, TextAlignmentOptions.Left);
                UIFactory.Text(col, $"{When(e)}  -  {(e.lots.Count > 0 ? $"{e.lots.Count} lots" : "catalogue not out yet")}" +
                                    (venue.GuaranteesTitle ? "" : $"   <color={UIFactory.Hex(UIFactory.Warn)}>No title guarantee</color>"), 14, UIFactory.TextDim, TextAlignmentOptions.Left);
                var ev = e;
                var b = UIFactory.Btn(row.transform, "Catalogue", UIFactory.Accent, () => { selectedEvent = ev.id; selectedLot = null; tabs.Select(1, false); Refresh(); }, 42f, 15f);
                UIFactory.LE(b, h: 42, w: 140, flexW: 0);
                b.interactable = e.lots.Count > 0;
            }
            if (calendarList.childCount == 0) UIFactory.EmptyState(calendarList, "No sales planned", null);
        }

        // ------------------------------------------------------------------ CATALOGUE
        void RenderCatalogue()
        {
            catalogueEvents.Clear();
            catalogueEvents.AddRange(A.Events.Where(e => !e.finished && e.lots.Count > 0).OrderBy(e => e.day).ThenBy(e => e.startHour).Take(6));
            if (selectedEvent == null || !catalogueEvents.Exists(e => e.id == selectedEvent)) selectedEvent = catalogueEvents.FirstOrDefault()?.id;
            UIFactory.ClearChildren(eventTabsHost);
            eventTabs = UIFactory.Tabs(eventTabsHost, catalogueEvents.Select(e => $"{(e.day == CarRecords.Today ? "Today" : "Day " + e.day)} {AuctionRules.TypeName(e.Type).Replace(" sale", "")}").ToArray(),
                                       i => { selectedEvent = catalogueEvents[i].id; selectedLot = null; Refresh(); }, 38f, 0f, 13f);
            int idx = catalogueEvents.FindIndex(e => e.id == selectedEvent);
            if (idx >= 0) eventTabs.Select(idx, false);

            UIFactory.ClearChildren(lotList);
            var ev = catalogueEvents.Find(e => e.id == selectedEvent);
            if (ev == null) { UIFactory.EmptyState(lotList, "No catalogues out", "Catalogues appear two days before each sale."); UIFactory.ClearChildren(detail); return; }
            foreach (var lot in A.LotsOf(ev).OrderBy(l => l.lotNo)) LotRow(lot, ev);
            if (selectedLot == null) selectedLot = A.LotsOf(ev).FirstOrDefault()?.id;
            RenderDetail();
        }

        void LotRow(AuctionLot lot, AuctionEvent ev)
        {
            var car = A.CarOf(lot);
            string name = car != null ? car.Name : "(sold)";
            string price = lot.Status == LotStatus.Sold || lot.Status == LotStatus.Provisional ? $"{StatusText(lot)} {UIFactory.Money(lot.hammer)}"
                         : string.IsNullOrEmpty(lot.leader) ? $"guide {UIFactory.Money(lot.estimate)}" : $"{UIFactory.Money(lot.currentBid)} ({A.BidderName(lot.leader)})";
            var l = lot;
            var b = UIFactory.Btn(lotList, $"<b>Lot {lot.lotNo}</b>  {name}  <size=80%>grade {lot.report.grade}</size>\n<size=80%>{price}{(lot.playerMax > 0 ? $"   your max {UIFactory.Money(lot.playerMax)}" : "")}{(lot.watched ? "   [watching]" : "")}</size>",
                                  lot.id == selectedLot ? UIFactory.Accent : UIFactory.CardLight, () => { selectedLot = l.id; selectedDisplay = null; Refresh(); }, out var label, 56f, 15f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.margin = new Vector4(12f, 0f, 8f, 0f);
        }

        static string StatusText(AuctionLot l)
        {
            switch (l.Status)
            {
                case LotStatus.Live: return "LIVE";
                case LotStatus.Sold: return l.buyer == AuctionRules.PlayerId ? "WON" : "Sold";
                case LotStatus.Provisional: return "Provisional";
                case LotStatus.Unsold: return "Unsold";
                case LotStatus.Withdrawn: return "Withdrawn";
                default: return "Upcoming";
            }
        }

        void Line(string label, string value)
        {
            var v = UIFactory.SpecRow(detail, label, 28f);
            v.text = value;
            v.fontSize = 16;
        }

        void RenderDetail()
        {
            UIFactory.ClearChildren(detail);
            maxInput = null;
            var lot = A.Lot(selectedLot);
            if (lot == null) { UIFactory.EmptyState(detail, "Pick a lot", null); return; }
            var ev = A.EventOf(lot);
            var car = A.CarOf(lot);
            var venue = AuctionRules.Venue(ev.venueId);
            var def = car != null ? CarSalesServices.Database.Get(car.carId) : null;

            var top = UIFactory.HRow(detail, 190, 16, "Top", false);
            var pic = UIFactory.CarPicture(top, 180f, 320f);
            if (car != null) UIFactory.SetCarPicture(pic, car);
            var head = UIFactory.VCol(top, 4f);
            UIFactory.Text(head, $"<b>Lot {lot.lotNo}</b>  -  {StatusText(lot)}", 20, UIFactory.Accent, TextAlignmentOptions.Left);
            UIFactory.Text(head, def ? $"{def.year} {def.brand} {def.displayName}" : "(no longer here)", 24, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Text(head, $"{AuctionRules.TypeName(ev.Type)}  -  {When(ev)}", 15, UIFactory.TextDim, TextAlignmentOptions.Left);
            string current = string.IsNullOrEmpty(lot.leader) ? $"Opens at {UIFactory.Money(lot.startPrice)}" : $"Current bid {UIFactory.Money(lot.currentBid)} ({A.BidderName(lot.leader)})";
            UIFactory.Text(head, $"Guide {UIFactory.Money(lot.estimate)}   -   {current}", 17, UIFactory.Gold, TextAlignmentOptions.Left, true);
            if (ev.online && lot.Status == LotStatus.Live)
                UIFactory.Text(head, $"Closes at {Mathf.FloorToInt(lot.endHour):00}:{Mathf.FloorToInt((lot.endHour % 1f) * 60f):00} (late bids add 2 minutes)", 14, UIFactory.TextDim, TextAlignmentOptions.Left);

            var r = lot.report;
            UIFactory.SectionTitle(detail, "Condition report (by the auction house)");
            Line("Grade", $"{r.grade} / 5");
            Line("Mileage", $"{r.claimedMileage:N0} km {(r.mileageVerified ? "" : $"<color={UIFactory.Hex(UIFactory.Warn)}>(unverified)</color>")}");
            Line("Starts / drives", $"{(r.starts ? "Yes" : $"<color={UIFactory.Hex(UIFactory.Bad)}>No</color>")} / {(r.drives ? "Yes" : $"<color={UIFactory.Hex(UIFactory.Bad)}>No</color>")}");
            Line("Keys", r.keys.ToString());
            Line("Logbook (V5C)", r.v5cPresent ? "Present" : $"<color={UIFactory.Hex(UIFactory.Bad)}>Missing</color>");
            Line("Title", venue.GuaranteesTitle ? "Guaranteed (any finance is cleared)" : $"<color={UIFactory.Hex(UIFactory.Warn)}>Not guaranteed - check the history</color>");
            var issues = r.faults.Concat(r.damage).ToList();
            var t = UIFactory.Text(detail, issues.Count == 0 ? "No faults or damage listed." : "Listed: " + string.Join(", ", issues) + (string.IsNullOrEmpty(r.notes) ? "" : $"\n{r.notes}"), 15, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var note = UIFactory.Text(detail, $"Reports can miss things ({venue.ReportReliability * 100f:0}% of faults are listed at this venue). " +
                                              (venue.Physical ? "Inspect the car in the yard (no road test)." : "Run a history check before bidding."), 13, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            note.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(note, h: 36);

            bool open = lot.Status == LotStatus.Upcoming || lot.Status == LotStatus.Live;
            if (!open) return;
            var acc = A.Account(ev.venueId);
            UIFactory.SectionTitle(detail, acc != null ? $"Your bid  (buyer {acc.buyerNo})" : "Your bid");
            var row = UIFactory.HRow(detail, 46, 10, "Bid", false);
            maxInput = UIFactory.MakeInput(row, $"Max bid (at least {UIFactory.Money(AuctionRules.NextBid(lot))})", 44f);
            maxInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            if (lot.playerMax > 0) maxInput.text = lot.playerMax.ToString();
            var input = maxInput;
            var set = UIFactory.Btn(row, ev.online || !(ev.day == CarRecords.Today && AuctionManager.PlayerInHall()) ? "Set max bid" : "Absentee max", UIFactory.Good,
                                    () => { if (int.TryParse(input.text, out int v)) A.SetMaxBid(lot, v); }, 44f, 15f);
            UIFactory.LE(set, h: 44, w: 160, flexW: 0);
            if (lot.playerMax > 0)
            {
                var w = UIFactory.Btn(row, "Withdraw", UIFactory.Neutral, () => A.SetMaxBid(lot, 0), 44f, 14f);
                UIFactory.LE(w, h: 44, w: 110, flexW: 0);
            }
            var row2 = UIFactory.HRow(detail, 44, 10, "Actions", false);
            var watch = UIFactory.Btn(row2, lot.watched ? "Unwatch" : "Watch", UIFactory.CardLight, () => A.ToggleWatch(lot), 42f, 14f);
            UIFactory.LE(watch, h: 42, w: 120, flexW: 0);
            if (car != null)
            {
                var hc = UIFactory.Btn(row2, car.history.checkedHistory ? "History checked" : $"History check {UIFactory.Money(InspectionEngine.HistoryCheckCost)}", UIFactory.Accent,
                                       () => { if (CarSalesServices.Workshop) CarSalesServices.Workshop.RunHistoryCheck(car); Refresh(); }, 42f, 14f);
                UIFactory.LE(hc, h: 42, w: 200, flexW: 0);
                hc.interactable = !car.history.checkedHistory;
                if (car.history.checkedHistory)
                {
                    var h = car.history;
                    var res = UIFactory.Text(detail, (h.writeOff == WriteOff.None ? "No write-off" : h.writeOff == WriteOff.CatN ? "WRITE-OFF Cat N" : "WRITE-OFF Cat S") + "  -  " +
                                                     (h.outstandingFinance && !h.financeSettled ? (venue.GuaranteesTitle ? "finance (cleared by the auction)" : "OUTSTANDING FINANCE") : "no finance") +
                                                     (h.clockingConfirmed ? "  -  MILEAGE ANOMALY" : ""), 15, h.writeOff != WriteOff.None || h.clockingConfirmed ? UIFactory.Bad : UIFactory.Good, TextAlignmentOptions.Left, true);
                    UIFactory.LE(res, h: 24);
                }
                var display = selectedDisplay;
                if (display && venue.Physical)
                {
                    var insp = UIFactory.Btn(row2, "Inspect the car", UIFactory.Warn, () => CarSalesServices.UI.inspection.Show(car, display), 42f, 14f);
                    UIFactory.LE(insp, h: 42, w: 170, flexW: 0);
                }
                int found = InspectionEngine.KnownCount(car.faults);
                if (found > 0)
                {
                    var f = UIFactory.Text(detail, $"Known faults ({found}): " + string.Join(", ", CarRecords.KnownFaults(car).Select(x => x.Def.Name)), 14, UIFactory.Warn, TextAlignmentOptions.TopLeft);
                    f.textWrappingMode = TextWrappingModes.Normal;
                    f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }
            if (acc == null)
            {
                var reg = UIFactory.Btn(detail, $"Register with {venue.Name} ({UIFactory.Money(AuctionRules.Deposit)} refundable deposit)", UIFactory.Accent, () => A.Register(ev.venueId), 42f, 15f);
                UIFactory.LE(reg, h: 42, w: 520, flexW: 0);
            }
            if (!ev.online)
            {
                var hint = UIFactory.Text(detail, $"Live sale: be in the hall at {ev.startHour:00}:00 on day {ev.day} and raise your paddle (E). Your max bid works as an absentee bid if you're not there.",
                                          13, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
                hint.textWrappingMode = TextWrappingModes.Normal;
                UIFactory.LE(hint, h: 36);
            }
        }

        // ------------------------------------------------------------------ BIDS
        void RenderBids()
        {
            var rows = new List<UIFactory.Table.Row>();
            foreach (var ev in A.Events)
                foreach (var lot in A.LotsOf(ev).Where(l => l.playerMax > 0 || l.watched || l.buyer == AuctionRules.PlayerId))
                {
                    var car = A.CarOf(lot);
                    var l = lot;
                    bool leading = lot.leader == AuctionRules.PlayerId;
                    string status = StatusText(lot) + (lot.Status == LotStatus.Live || lot.Status == LotStatus.Upcoming
                        ? (lot.playerMax == 0 ? "" : leading ? $"  <color={UIFactory.Hex(UIFactory.Good)}>leading</color>" : string.IsNullOrEmpty(lot.leader) ? "" : $"  <color={UIFactory.Hex(UIFactory.Bad)}>outbid</color>") : "");
                    rows.Add(new UIFactory.Table.Row
                    {
                        cells = new[] { lot.lotNo.ToString(), car?.Name ?? "-", $"{AuctionRules.TypeName(ev.Type)} day {ev.day}",
                                        string.IsNullOrEmpty(lot.leader) ? "-" : UIFactory.Money(lot.currentBid), lot.playerMax > 0 ? UIFactory.Money(lot.playerMax) : "-", status },
                        keys = new IComparable[] { lot.lotNo, car?.Name ?? "", ev.day * 100 + (int)ev.startHour, lot.currentBid, lot.playerMax, lot.status },
                        onClick = () => ShowLot(l.id),
                    });
                }
            bidsTable.SetRows(rows);
        }

        // ------------------------------------------------------------------ INVOICES
        void RenderInvoices()
        {
            UIFactory.ClearChildren(invoicesList);
            foreach (var inv in A.Invoices.OrderByDescending(i => i.issuedDay))
            {
                var venue = AuctionRules.Venue(inv.venueId);
                var row = UIFactory.Panel(invoicesList, inv.id, UIFactory.CardBg, 1.8f);
                UIFactory.LE(row, h: 96);
                var h = UIFactory.Horizontal(row, 10, 12f);
                h.padding = new RectOffset(16, 12, 8, 8);
                h.childForceExpandHeight = false;
                string state = inv.cancelled ? $"<color={UIFactory.Hex(UIFactory.Bad)}>Cancelled (deposit kept)</color>" :
                               !inv.paid ? (CarRecords.Today > inv.dueDay ? $"<color={UIFactory.Hex(UIFactory.Bad)}>OVERDUE</color>" : $"Due day {inv.dueDay}") :
                               inv.collected ? $"<color={UIFactory.Hex(UIFactory.Good)}>Paid and collected</color>" :
                               $"Paid - waiting at the Auction Center car park (free until day {inv.freeStorageUntil}{(inv.storageFees > 0 ? $", storage so far {UIFactory.Money(inv.storageFees)}" : "")})";
                var t = UIFactory.Text(row.transform,
                    $"<b>{inv.id}</b>  -  {venue.Name} lot {inv.lotNo}  -  {state}\n" +
                    $"<size=85%>Hammer {UIFactory.Money(inv.hammer)}  +  buyer's premium {UIFactory.Money(inv.premium)}" + (inv.internetFee > 0 ? $"  +  internet fee {UIFactory.Money(inv.internetFee)}" : "") +
                    $"  +  VAT on fees {UIFactory.Money(inv.vat)}" + (inv.lateFees > 0 ? $"  +  late fees {UIFactory.Money(inv.lateFees)}" : "") + $"  =  <b>{UIFactory.Money(inv.Total)}</b></size>",
                    16, UIFactory.TextMain, TextAlignmentOptions.Left);
                UIFactory.LE(t, flexW: 1);
                var i2 = inv;
                if (!inv.paid && !inv.cancelled)
                {
                    var pay = UIFactory.Btn(row.transform, $"Pay {UIFactory.Money(inv.Total)}", UIFactory.Good, () => A.PayInvoice(i2), 46f, 16f);
                    UIFactory.LE(pay, h: 46, w: 180, flexW: 0);
                }
                else if (inv.paid && !inv.collected)
                {
                    var gps = UIFactory.Btn(row.transform, "GPS to car park", UIFactory.Accent, () =>
                    {
                        var p = MapPlace.All.FirstOrDefault(x => x && x.placeName.ToLowerInvariant().Contains("auction"));
                        if (p) { GpsSystem.SetTarget(p); CarSalesServices.UI.Success("GPS", "Route to the Auction Center."); }
                    }, 46f, 15f);
                    UIFactory.LE(gps, h: 46, w: 180, flexW: 0);
                }
            }
            if (invoicesList.childCount == 0) UIFactory.EmptyState(invoicesList, "No invoices", "Win a lot and its invoice appears here (payment due in 2 days).");
        }

        // ------------------------------------------------------------------ ACCOUNTS
        void RenderAccounts()
        {
            UIFactory.ClearChildren(accountsList);
            foreach (var v in AuctionRules.Venues)
            {
                var acc = A.Account(v.Id);
                var row = UIFactory.Panel(accountsList, v.Id, UIFactory.CardBg, 1.8f);
                UIFactory.LE(row, h: 80);
                var h = UIFactory.Horizontal(row, 10, 12f);
                h.padding = new RectOffset(16, 12, 8, 8);
                var t = UIFactory.Text(row.transform,
                    $"<b>{v.Name}</b>  {(v.Physical ? "(hall + yard)" : "(online)")}\n<size=85%>" +
                    (acc != null ? $"Buyer number <b>{acc.buyerNo}</b>  -  deposit {UIFactory.Money(acc.deposit)} held" : $"Not registered. Deposit {UIFactory.Money(AuctionRules.Deposit)}, refunded when you close the account.") +
                    $"  -  {(v.GuaranteesTitle ? "title guaranteed" : "no title guarantee")}  -  reports list about {v.ReportReliability * 100f:0}% of faults</size>",
                    16, UIFactory.TextMain, TextAlignmentOptions.Left);
                UIFactory.LE(t, flexW: 1);
                var id = v.Id;
                var b = UIFactory.Btn(row.transform, acc != null ? "Close account" : "Register", acc != null ? UIFactory.Neutral : UIFactory.Accent,
                                      () => { if (A.Registered(id)) A.CloseAccount(id); else A.Register(id); }, 44f, 15f);
                UIFactory.LE(b, h: 44, w: 170, flexW: 0);
            }
            var fees = UIFactory.Text(accountsList,
                $"Buyer's premium: under $1,000: $150  -  $1,000-2,999: $250  -  $3,000-4,999: $350  -  $5,000-9,999: $450  -  $10,000-19,999: $600  -  $20,000+: $750 + 1%.\n" +
                $"Online sales add a {UIFactory.Money(AuctionRules.InternetFee)} internet fee. VAT 20% is charged on fees (not on the car). Pay within {AuctionRules.PaymentDays} days " +
                $"({UIFactory.Money(AuctionRules.LateFeePerDay)}/day late; cancelled after {AuctionRules.CancelAfterDays}). Storage is free for {AuctionRules.FreeStorageDays} days, then {UIFactory.Money(AuctionRules.StoragePerDay)}/day.",
                14, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            fees.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(fees, h: 80);
        }
    }
}
