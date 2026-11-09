using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// The vehicle file (spec §6.14.2): photo and headline on the left, tabs on the right —
    /// Overview, Condition, Documents, History, Costs, Listing. Opens for marketplace listings, cars standing at
    /// the Used Car Market (physical) and your own cars (<see cref="ShowOwned"/>).
    /// </summary>
    public class VehicleDetailsUI : UIPanel
    {
        enum Mode { Listing, Physical, Owned }
        static readonly string[] TabNames = { "Overview", "Condition", "Documents", "History", "Costs", "Listing" };

        CarInstance car;
        CarDisplay physical;   // set when viewing a car at the Used Car Market (or an owned car on a spot)
        Mode mode;
        RawImage photo;
        TextMeshProUGUI titleText, nameText, priceText, priceSub, buyLabel, backLabel, inspectLabel, conditionChipText, dealChipText;
        Image conditionChip, dealChip;
        Button inspectButton, buyButton;
        UIFactory.TabBar tabs;
        RectTransform tabArea;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Vehicle file", new Vector2(1420f, 820f), CloseAll, out titleText, out _);
            UIFactory.Horizontal(body, 0, 28f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            var left = UIFactory.VCol(body, 10f, "Left", 520f);
            photo = UIFactory.CarPicture(left, 320f);
            nameText = UIFactory.Text(left, "", 30, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(nameText, h: 76);
            priceText = UIFactory.Text(left, "", 42, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(priceText, h: 52);
            priceSub = UIFactory.Text(left, "", 16, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(priceSub, h: 24);
            var chips = UIFactory.HRow(left, 30, 8, "Chips", false);
            conditionChip = UIFactory.Chip(chips, "", UIFactory.Neutral, out conditionChipText, 14f);
            dealChip = UIFactory.Chip(chips, "", UIFactory.Neutral, out dealChipText, 14f);
            UIFactory.Spacer(chips, 0f, 1f);
            UIFactory.Spacer(left);
            var row = UIFactory.HRow(left, 56, 12);
            inspectButton = UIFactory.Btn(row, "Inspect", UIFactory.Warn, Inspect, out inspectLabel, 54f, 17f);
            buyButton = UIFactory.Btn(row, "Buy Now", UIFactory.Good, Primary, out buyLabel, 54f, 18f);
            UIFactory.Btn(left, "Back", UIFactory.Neutral, Back, out backLabel, 46f, 17f);

            var right = UIFactory.VCol(body, 12f, "Right");
            tabs = UIFactory.Tabs(right, TabNames, _ => RenderTab(), 44f, 0f, 16f);
            var area = UIFactory.Panel(right, "TabArea", UIFactory.CardBg, 1.4f);
            UIFactory.LE(area, flexH: 1, flexW: 1);
            UIFactory.Vertical(area, 18, 8f);
            tabArea = UIFactory.ScrollList(area.transform, 8f, "TabContent");
            tabs.Select(0, false);
        }

        // ------------------------------------------------------------------ OPENING
        /// <summary>A car standing at the Used Car Market (free inspection, drive it away).</summary>
        public void ShowPhysical(CarDisplay display)
        {
            physical = display;
            mode = Mode.Physical;
            ShowInternal(display.Data);
        }

        public void Show(CarInstance listing)
        {
            physical = null;
            mode = Mode.Listing;
            ShowInternal(listing);
        }

        /// <summary>The file of a car you own (from My Cars or the car menu).</summary>
        public void ShowOwned(CarInstance owned)
        {
            physical = CarSalesServices.Vehicles ? CarSalesServices.Vehicles.GetDisplay(owned.instanceId) : null;
            mode = Mode.Owned;
            ShowInternal(owned);
        }

        /// <summary>Returns to the same car after the inspection screen.</summary>
        public void Reopen()
        {
            if (mode == Mode.Owned) ShowOwned(car);
            else if (physical) ShowPhysical(physical);
            else Show(car);
        }

        void Back()
        {
            if (CarSalesServices.UI.CanGoBack) { GoBack(); return; }
            if (mode == Mode.Listing) CarSalesServices.UI.marketplace.Show();
            else CloseAll();
        }

        void Primary()
        {
            switch (mode)
            {
                case Mode.Listing: CarSalesServices.UI.marketplace.Buy(car); break;
                case Mode.Physical:
                    int price = car.purchasePrice;
                    if (CarSalesServices.Marketplace.TryBuyFromLot(physical, out string msg))
                        CarSalesServices.UI.purchaseConfirm.Show(car, price, -1, msg);
                    else CarSalesServices.UI.Error(msg);
                    break;
                case Mode.Owned:
                    if (physical) CarSalesServices.UI.listingEditor.Show(physical);
                    else CarSalesServices.UI.Error("Park the car on one of your dealership spots to sell it.");
                    break;
            }
        }

        void ShowInternal(CarInstance target)
        {
            car = target;
            if (mode == Mode.Physical) car.inspected = true;   // you can look the car over yourself on the lot
            OpenSelf();
            var def = CarSalesServices.Database.Get(car.carId);
            UIFactory.SetCarPicture(photo, car);
            bool known = Known;
            float cond = known ? car.condition : car.reportedCondition;
            int estimate = CarValueCalculator.MarketValue(car, useReported: !known);

            titleText.text = mode == Mode.Owned ? "Vehicle file  -  your car" : mode == Mode.Physical ? "Vehicle file  -  Used Car Market" : "Vehicle file  -  marketplace listing";
            nameText.text = def ? $"{def.year} {def.brand} {def.displayName}\n<size=55%><color={UIFactory.Hex(UIFactory.TextDim)}>{def.category}  -  {car.mileage:N0} km</color></size>" : car.carId;

            if (mode == Mode.Owned)
            {
                priceText.text = UIFactory.Money(estimate);
                priceSub.text = car.state == CarState.Listed ? $"Market value  -  for sale at {UIFactory.Money(car.listedPrice)}" : "Market value";
                var r = car.state == CarState.Listed ? PriceRating.Rate(car.listedPrice, estimate) : DealRating.Unknown;
                SetChip(dealChip, dealChipText, car.state == CarState.Listed ? (r == DealRating.Overpriced ? "Priced high" : r == DealRating.GreatDeal ? "Priced low" : "Priced fairly") : car.personal ? "Personal car" : "Not for sale",
                        car.state == CarState.Listed ? (r == DealRating.Overpriced ? UIFactory.Warn : UIFactory.Good) : UIFactory.Neutral);
            }
            else
            {
                priceText.text = UIFactory.Money(car.purchasePrice);
                priceSub.text = $"Estimated value {UIFactory.Money(estimate)}{(known ? "" : " (seller's claim)")}";
                var r = PriceRating.Rate(car.purchasePrice, estimate);
                SetChip(dealChip, dealChipText, PriceRating.Label(r), DealColor(r));
            }
            SetChip(conditionChip, conditionChipText, known ? $"Verified {cond:0}%" : $"Claimed {cond:0}% - not inspected", known ? UIFactory.Good : UIFactory.Warn);

            int found = InspectionEngine.KnownCount(car.faults);
            inspectLabel.text = found > 0 ? $"Inspection ({found} found)" : "Inspect";
            int delivery = CarSalesServices.Marketplace.DeliveryFee;
            buyLabel.text = mode == Mode.Physical ? "Buy - drive it away" : mode == Mode.Listing ? $"Buy now\n<size=75%>+{UIFactory.Money(delivery)} delivery</size>" : car.state == CarState.Listed ? "Change price" : "Set price";
            UIFactory.SetButtonColor(buyButton, mode == Mode.Owned ? UIFactory.Accent : UIFactory.Good);
            buyButton.interactable = !(mode == Mode.Owned && car.personal);
            backLabel.text = CarSalesServices.UI.CanGoBack ? "Back" : mode == Mode.Listing ? "Back to Marketplace" : "Close";

            tabs.SetLabel(3, "History");
            RenderTab();
        }

        bool Known => mode == Mode.Owned || CarValueCalculator.IsConditionKnown(car);

        static void SetChip(Image chip, TextMeshProUGUI label, string text, Color c)
        {
            chip.color = c;
            label.text = text;
            label.color = UIFactory.ContrastText(c);
        }

        static Color DealColor(DealRating r) =>
            r == DealRating.GreatDeal ? UIFactory.Good : r == DealRating.GoodPrice ? UIFactory.Info : r == DealRating.Fair ? UIFactory.Neutral : r == DealRating.Overpriced ? UIFactory.Bad : UIFactory.Neutral;

        // ------------------------------------------------------------------ TABS
        void RenderTab()
        {
            if (car == null || !tabArea) return;
            UIFactory.ClearChildren(tabArea);
            switch (tabs.Index)
            {
                case 1: Condition(); break;
                case 2: Documents(); break;
                case 3: History(); break;
                case 4: Costs(); break;
                case 5: Listing(); break;
                default: Overview(); break;
            }
        }

        TextMeshProUGUI Spec(string label, string value)
        {
            var v = UIFactory.SpecRow(tabArea, label, 34f);
            v.text = value;
            return v;
        }

        void Paragraph(string text, float height = 0f)
        {
            var t = UIFactory.Text(tabArea, text, 17, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            if (height > 0f) UIFactory.LE(t, h: height);
            else t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void Overview()
        {
            var def = CarSalesServices.Database.Get(car.carId);
            bool known = Known;
            float cond = known ? car.condition : car.reportedCondition;
            UIFactory.SectionTitle(tabArea, "Specification");
            Spec("Year", def ? def.year.ToString() : "-");
            Spec("Body", def ? def.category.ToString() : "-");
            Spec("Mileage", $"{car.mileage:N0} km");
            Spec("Fuel type", def && !string.IsNullOrEmpty(def.fuelType) ? def.fuelType : "Petrol");
            Spec("Transmission", def && !string.IsNullOrEmpty(def.transmission) ? def.transmission : "Automatic");
            Spec("Condition", $"<color={UIFactory.Hex(CarParts.LabelColor(cond))}>{(known ? "" : "~")}{cond:0}% {CarParts.Label(cond)}</color>");
            Spec("Cleanliness", $"{car.cleanliness:0}%");
            Spec("Fuel in tank", $"{car.fuel:0}%");
            if (mode == Mode.Owned && CarSalesServices.Vehicles) Spec("Location", CarSalesServices.Vehicles.LocationText(car));

            string flavour = cond >= 80 ? "Well maintained and ready to sell." : cond >= 60 ? "Decent car, might need a little work."
                           : cond >= 40 ? "Needs some repairs before resale." : "Project car - expect major repairs.";
            UIFactory.SectionTitle(tabArea, "Description");
            Paragraph((def && !string.IsNullOrEmpty(def.description) ? def.description + "\n\n" : "") + flavour);
        }

        void Condition()
        {
            if (!Known)
            {
                UIFactory.EmptyState(tabArea, "Not inspected yet",
                    $"The seller claims {car.reportedCondition:0}%. Inspect the car to see each part's real condition.");
                return;
            }
            UIFactory.SectionTitle(tabArea, "Condition by area");
            foreach (var part in CarParts.All)
            {
                float v = CarParts.Get(car, part);
                var row = UIFactory.HRow(tabArea, 34, 12, part.ToString(), false);
                var l = UIFactory.Text(row, CarParts.DisplayName(part), 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
                UIFactory.LE(l, w: 200, flexW: 0);
                var holder = UIFactory.VCol(row, 0f, "Bar");
                holder.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                var bar = UIFactory.ProgressBar(holder, CarParts.LabelColor(v), 12f);
                bar.Set(v / 100f);
                var t = UIFactory.Text(row, $"{v:0}%  {CarParts.Label(v)}", 16, CarParts.LabelColor(v), TextAlignmentOptions.Right, true);
                UIFactory.LE(t, w: 150, flexW: 0);
            }
            var known = CarRecords.KnownFaults(car).ToList();
            UIFactory.SectionTitle(tabArea, known.Count == 0 ? "No faults found yet" : $"Faults found ({known.Count})");
            foreach (var f in known)
            {
                var def = f.Def;
                string mot = def.FailsMot(f.severity) ? "  MOT FAIL" : def.AdvisesMot(f.severity) ? "  MOT advisory" : "";
                Spec(def.Name, $"<color={UIFactory.Hex(f.severity >= 0.6f ? UIFactory.Bad : UIFactory.Warn)}>~{UIFactory.Money(CarRecords.RepairEstimate(car, f))}{mot}</color>");
            }
            int repair = CarValueCalculator.RepairCost(car);
            UIFactory.Divider(tabArea);
            Spec("Estimated repair cost (all)", repair > 0 ? UIFactory.Money(repair) : "Nothing to repair");
            Spec("Full service at the workshop", repair > 0 ? UIFactory.Money(CarValueCalculator.FullServiceCost(car)) : "-");
        }

        void Documents()
        {
            CarParts.EnsureParts(car);
            var d = car.documents;
            var id = car.identity;
            UIFactory.SectionTitle(tabArea, "Identity");
            Spec("Registration", string.IsNullOrEmpty(id.plate) ? "-" : id.plate);
            Spec("VIN", string.IsNullOrEmpty(id.vin) ? "-" : id.vin);
            Spec("First registered", id.firstRegYear > 0 ? id.firstRegYear.ToString() : "-");
            UIFactory.SectionTitle(tabArea, "Paperwork");
            Spec("Logbook (V5C)", d.v5cPresent ? $"Present  -  {d.keepers} previous keeper{(d.keepers == 1 ? "" : "s")}" :
                                  d.v5cArrivesDay > 0 ? $"<color={UIFactory.Hex(UIFactory.Warn)}>Replacement arrives day {d.v5cArrivesDay}</color>" :
                                  $"<color={UIFactory.Hex(UIFactory.Bad)}>MISSING (-25% value)</color>");
            bool motOk = CarRecords.MotValid(car);
            Spec("MOT", $"<color={UIFactory.Hex(motOk ? UIFactory.Good : UIFactory.Bad)}>{CarRecords.MotText(car)}</color>");
            Spec("Service history", CarRecords.ServiceText(d) + (d.lastServiceKm > 0 ? $"  -  last at {d.lastServiceKm:N0} km" : ""));
            Spec("Keys", d.keys >= 2 ? "2 keys" : d.keys == 1 ? $"<color={UIFactory.Hex(UIFactory.Warn)}>1 key</color>" : $"<color={UIFactory.Hex(UIFactory.Bad)}>No key - can't be driven</color>");

            UIFactory.SectionTitle(tabArea, "MOT history");
            if (d.motHistory.Count == 0) { Paragraph("No MOT tests yet.", 30f); return; }
            foreach (var m in Enumerable.Reverse(d.motHistory).Take(8))
            {
                string extra = (m.fails.Count > 0 ? $"  <color={UIFactory.Hex(UIFactory.Bad)}>{string.Join(", ", m.fails)}</color>" : "") +
                               (m.advisories.Count > 0 ? $"  <color={UIFactory.Hex(UIFactory.Warn)}>Advisory: {string.Join(", ", m.advisories)}</color>" : "");
                var t = UIFactory.Text(tabArea, $"<b>{m.year}</b>   {(m.passed ? "PASS" : "FAIL")}   {m.mileage:N0} km{extra}", 15, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
                t.textWrappingMode = TextWrappingModes.Normal;
                t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        void History()
        {
            CarParts.EnsureParts(car);
            var h = car.history;
            UIFactory.SectionTitle(tabArea, "History check");
            if (!h.checkedHistory)
            {
                Paragraph($"Write-offs, outstanding finance and mileage records are only known after a history check ({UIFactory.Money(InspectionEngine.HistoryCheckCost)})." +
                          (h.clockingSuspected ? $"\n<color={UIFactory.Hex(UIFactory.Warn)}>You suspect the mileage is wrong.</color>" : ""), 60f);
                var b = UIFactory.Btn(tabArea, $"Run a history check ({UIFactory.Money(InspectionEngine.HistoryCheckCost)})", UIFactory.Accent, () =>
                {
                    if (CarSalesServices.Workshop && CarSalesServices.Workshop.RunHistoryCheck(car) != null) ShowInternal(car);
                }, 44f, 16f);
                UIFactory.LE(b, h: 44, w: 360, flexW: 0);
            }
            else
            {
                foreach (var line in InspectionEngine.HistoryCheck(car.documents, h, car.mileage, h.checkedDay, null))
                {
                    bool bad = line.StartsWith("WRITE-OFF") || line.StartsWith("OUTSTANDING") || line.StartsWith("MILEAGE");
                    var t = UIFactory.Text(tabArea, (bad ? "!  " : "-  ") + line, 16, bad ? UIFactory.Bad : UIFactory.TextMain, TextAlignmentOptions.Left, bad);
                    t.textWrappingMode = TextWrappingModes.Normal;
                    t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
                Paragraph($"Checked on day {h.checkedDay}.", 24f);
            }

            UIFactory.SectionTitle(tabArea, "Your records for this car");
            var ledger = CarSalesServices.Economy ? CarSalesServices.Economy.Ledger : null;
            var entries = ledger != null ? ledger.ForRef(car.instanceId) : new List<Transaction>();
            if (entries.Count == 0) { Paragraph(mode == Mode.Owned ? "No records kept (older ones are summarised in Accounts)." : "Nothing yet - buy the car to start its file.", 30f); return; }
            foreach (var e in entries)
            {
                var row = UIFactory.HRow(tabArea, 30, 12, "Entry", false);
                var when = UIFactory.Text(row, $"Day {e.day}  {Mathf.FloorToInt(e.hour):00}:{Mathf.FloorToInt((e.hour % 1f) * 60f):00}", 15, UIFactory.TextDim, TextAlignmentOptions.Left);
                UIFactory.LE(when, w: 130, flexW: 0);
                var what = UIFactory.Text(row, string.IsNullOrEmpty(e.memo) ? TxCategories.DisplayName(e.category) : e.memo, 15, UIFactory.TextMain, TextAlignmentOptions.Left);
                UIFactory.LE(what, flexW: 1);
                var amt = UIFactory.Text(row, (e.amount > 0 ? "+" : "") + UIFactory.Money(e.amount), 15, e.amount >= 0 ? UIFactory.Good : UIFactory.Bad, TextAlignmentOptions.Right, true);
                UIFactory.LE(amt, w: 130, flexW: 0);
            }
        }

        void Costs()
        {
            int estimate = CarValueCalculator.MarketValue(car, useReported: !Known);
            if (mode != Mode.Owned)
            {
                int fee = car.inspected ? 0 : CarValueCalculator.InspectionCost();
                int delivery = mode == Mode.Listing ? CarSalesServices.Marketplace.DeliveryFee : 0;
                int repair = Known ? CarValueCalculator.RepairCost(car) : 0;
                int total = car.purchasePrice + delivery + fee;
                UIFactory.SectionTitle(tabArea, "If you buy it");
                Spec("Asking price", UIFactory.Money(car.purchasePrice));
                if (delivery > 0) Spec("Delivery", UIFactory.Money(delivery));
                if (fee > 0) Spec("Inspection (optional)", UIFactory.Money(fee));
                Spec("Repairs to full condition", Known ? (repair > 0 ? UIFactory.Money(repair) : "None") : "Unknown - inspect first");
                UIFactory.Divider(tabArea);
                Spec("Total to get it on your lot", UIFactory.Money(total));
                Spec("Estimated value now", UIFactory.Money(estimate));
                int margin = estimate - total;
                Spec("Margin at today's value", $"<color={UIFactory.Hex(margin >= 0 ? UIFactory.Good : UIFactory.Bad)}>{UIFactory.Money(margin)}</color>");
                return;
            }

            var ledger = CarSalesServices.Economy.Ledger;
            int bought = 0, other = 0;
            foreach (var e in ledger.ForRef(car.instanceId))
            {
                if (e.category == TxCategory.VehiclePurchase) bought += -e.amount;
                else if (e.amount < 0 && e.category != TxCategory.Repair) other += -e.amount;
            }
            if (bought == 0) bought = car.purchasePrice;
            int be = PriceRating.BreakEven(bought, car.repairSpent, other);
            UIFactory.SectionTitle(tabArea, "Money in this car");
            Spec("Bought for", UIFactory.Money(bought));
            Spec("Repairs", UIFactory.Money(car.repairSpent));
            Spec("Other costs (fuel, wash, delivery...)", UIFactory.Money(other));
            UIFactory.Divider(tabArea);
            Spec("Break-even price", UIFactory.Money(be));
            Spec("Market value now", UIFactory.Money(estimate));
            int profit = estimate - be;
            Spec("Profit if sold at market value", $"<color={UIFactory.Hex(profit >= 0 ? UIFactory.Good : UIFactory.Bad)}>{UIFactory.Money(profit)}</color>");
            Spec("Quick sale (wholesale) offer", UIFactory.Money(CarValueCalculator.QuickSellPrice(car)));
        }

        void Listing()
        {
            int estimate = CarValueCalculator.MarketValue(car, useReported: !Known);
            if (mode == Mode.Owned)
            {
                UIFactory.SectionTitle(tabArea, "Sale listing");
                if (car.personal) { UIFactory.EmptyState(tabArea, "This is your personal car", "Personal cars are never offered to customers."); return; }
                Spec("Status", car.state == CarState.Listed ? "For sale" : car.state == CarState.NeedsRepair ? "Needs repair" : "Not listed");
                Spec("Asking price", car.state == CarState.Listed ? UIFactory.Money(car.listedPrice) : "-");
                Spec("Market value", UIFactory.Money(estimate));
                if (car.state == CarState.Listed)
                {
                    var r = PriceRating.Rate(car.listedPrice, estimate);
                    Spec("Buyers will see it as", PriceRating.Label(r));
                }
                Paragraph("Customers pay close to market value; a lower price sells faster, a higher one needs good haggling. " +
                          "Photos, descriptions and competitor prices come with the listing editor (phase 7).", 80f);
                return;
            }
            UIFactory.SectionTitle(tabArea, "This listing");
            var rating = PriceRating.Rate(car.purchasePrice, estimate);
            Spec("Asking price", UIFactory.Money(car.purchasePrice));
            Spec("Estimated value", UIFactory.Money(estimate) + (Known ? "" : " (seller's claim)"));
            Spec("Price vs value", $"{(float)car.purchasePrice / Mathf.Max(1, estimate) * 100f:0}%  -  {PriceRating.Label(rating)}");
            Spec("Seller", mode == Mode.Physical ? "Used Car Market" : "Private / trade seller (online)");
            Paragraph(Known ? "The condition has been checked, so the estimate is reliable."
                            : "Sellers may exaggerate. Inspect before you buy, or upgrade to a mechanic for true condition.", 60f);
        }

        void Inspect() => CarSalesServices.UI.inspection.Show(car, physical);
    }
}
