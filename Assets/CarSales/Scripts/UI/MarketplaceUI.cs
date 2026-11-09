using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Marketplace app: filter sidebar (search, body type, max price, min condition, verified / deals only),
    /// sort, and a grid of cards or a sortable list. Cards carry a deal badge (price vs. estimated value).
    /// </summary>
    public class MarketplaceUI : ComputerPage
    {
        enum Sort { Newest, PriceLow, PriceHigh, Condition, BestDeal }

        RectTransform gridHost, listHost, grid;
        UIFactory.Table table;
        TMP_InputField search;
        TextMeshProUGUI info, maxPriceLabel, minCondLabel;
        Slider maxPrice, minCondition;
        Button verifiedBtn, dealsBtn;
        CarCategory? category;
        Sort sort = Sort.Newest;
        bool verifiedOnly, dealsOnly, listView;
        UIFactory.TabBar sortTabs, viewTabs;
        readonly List<KeyValuePair<CarCategory?, Button>> categoryButtons = new List<KeyValuePair<CarCategory?, Button>>();

        public override void Build()
        {
            UIFactory.Horizontal(Area, 0, 20f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            // ---- Filters
            var side = UIFactory.Panel(Area, "Filters", UIFactory.CardBg, 1.4f);
            UIFactory.LE(side, w: 270, flexW: 0);
            var sv = UIFactory.Vertical(side, 16, 6f);
            sv.padding = new RectOffset(16, 16, 16, 16);
            UIFactory.SectionTitle(side.transform, "Search");
            search = UIFactory.MakeInput(side.transform, "Name or brand...", 42f);
            search.onValueChanged.AddListener(_ => Refresh());

            UIFactory.SectionTitle(side.transform, "Body type");
            CategoryButton(side.transform, "All types", null);
            foreach (CarCategory c in Enum.GetValues(typeof(CarCategory))) CategoryButton(side.transform, c.ToString(), c);

            UIFactory.SectionTitle(side.transform, "Max price");
            maxPriceLabel = UIFactory.Text(side.transform, "", 15, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(maxPriceLabel, h: 20);
            maxPrice = UIFactory.MakeSlider(side.transform, UIFactory.Accent, 26f);
            maxPrice.value = 1f;
            maxPrice.onValueChanged.AddListener(_ => Refresh());

            UIFactory.SectionTitle(side.transform, "Min condition");
            minCondLabel = UIFactory.Text(side.transform, "", 15, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(minCondLabel, h: 20);
            minCondition = UIFactory.MakeSlider(side.transform, UIFactory.Good, 26f);
            minCondition.value = 0f;
            minCondition.onValueChanged.AddListener(_ => Refresh());

            verifiedBtn = UIFactory.Btn(side.transform, "Verified condition only", UIFactory.CardLight, () => { verifiedOnly = !verifiedOnly; Refresh(); }, 36f, 15f);
            dealsBtn = UIFactory.Btn(side.transform, "Good deals only", UIFactory.CardLight, () => { dealsOnly = !dealsOnly; Refresh(); }, 36f, 15f);
            UIFactory.Spacer(side.transform);
            UIFactory.Btn(side.transform, "Reset filters", UIFactory.Neutral, ResetFilters, 38f, 15f);

            // ---- Results
            var right = UIFactory.VCol(Area, 10f, "Results");
            var bar = UIFactory.HRow(right, 42, 10, "Bar", false);
            sortTabs = UIFactory.Tabs(bar, new[] { "Newest", "Price low", "Price high", "Condition", "Best deal" }, i => { sort = (Sort)i; Refresh(); }, 40f, 120f, 15f);
            viewTabs = UIFactory.Tabs(bar, new[] { "Grid", "List" }, i => { listView = i == 1; Refresh(); }, 40f, 80f, 15f);
            info = UIFactory.Text(right, "", 15, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(info, h: 22);

            var hosts = UIFactory.Rect("Hosts", right);
            UIFactory.LE(hosts, flexH: 1, flexW: 1);
            gridHost = UIFactory.Rect("GridHost", hosts);
            UIFactory.Stretch(gridHost);
            UIFactory.Vertical(gridHost, 0, 0f);
            grid = UIFactory.ScrollGrid(gridHost, new Vector2(282f, 350f), 4, 14f);
            listHost = UIFactory.Rect("ListHost", hosts);
            UIFactory.Stretch(listHost);
            UIFactory.Vertical(listHost, 0, 4f);
            table = UIFactory.MakeTable(listHost, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Car"),
                new UIFactory.TableColumn("Year", 80f),
                new UIFactory.TableColumn("Mileage", 120f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Condition", 170f),
                new UIFactory.TableColumn("Price", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Deal", 130f),
            }, 40f);
            table.SetEmptyText("No cars match your filters.");

            sortTabs.Select(0, false);
            viewTabs.Select(0, false);
        }

        void CategoryButton(Transform parent, string label, CarCategory? c)
        {
            var b = UIFactory.Btn(parent, label, UIFactory.Clear, () => { category = c; Refresh(); }, out var l, 30f, 15f);
            l.alignment = TextAlignmentOptions.Left;
            l.margin = new Vector4(10f, 0f, 0f, 0f);
            categoryButtons.Add(new KeyValuePair<CarCategory?, Button>(c, b));
        }

        void ResetFilters()
        {
            category = null; verifiedOnly = dealsOnly = false;
            search.text = "";
            maxPrice.value = 1f;
            minCondition.value = 0f;
            Refresh();
        }

        static float ShownCondition(CarInstance c) => CarValueCalculator.IsConditionKnown(c) ? c.condition : c.reportedCondition;

        static int Estimate(CarInstance c) => CarValueCalculator.MarketValue(c, useReported: !CarValueCalculator.IsConditionKnown(c));

        public override void Refresh()
        {
            foreach (var pair in categoryButtons)
            {
                bool on = pair.Key.HasValue ? category.HasValue && category.Value == pair.Key.Value : !category.HasValue;
                UIFactory.SetButtonColor(pair.Value, on ? UIFactory.Accent : UIFactory.Clear);
                pair.Value.GetComponentInChildren<TextMeshProUGUI>().color = on ? UIFactory.ContrastText(UIFactory.Accent) : UIFactory.TextMain;
            }
            UIFactory.SetButtonColor(verifiedBtn, verifiedOnly ? UIFactory.Good : UIFactory.CardLight);
            UIFactory.SetButtonColor(dealsBtn, dealsOnly ? UIFactory.Good : UIFactory.CardLight);

            var db = CarSalesServices.Database;
            var all = CarSalesServices.Marketplace.Listings;
            int highest = all.Count > 0 ? all.Max(c => c.purchasePrice) : 0;
            int priceCap = maxPrice.value >= 0.999f ? int.MaxValue : Mathf.RoundToInt(Mathf.Lerp(1000f, Mathf.Max(1000, highest), maxPrice.value) / 500f) * 500;
            float condMin = Mathf.Round(minCondition.value * 90f);
            maxPriceLabel.text = priceCap == int.MaxValue ? "Any price" : "Up to " + UIFactory.Money(priceCap);
            minCondLabel.text = condMin <= 0f ? "Any condition" : $"{condMin:0}% or better";

            IEnumerable<CarInstance> list = all;
            string q = search ? search.text.Trim().ToLowerInvariant() : "";
            if (q.Length > 0)
                list = list.Where(c => { var d = db.Get(c.carId); return d && (d.displayName + " " + d.brand + " " + d.category).ToLowerInvariant().Contains(q); });
            if (category.HasValue) list = list.Where(c => { var d = db.Get(c.carId); return d && d.category == category.Value; });
            list = list.Where(c => c.purchasePrice <= priceCap && ShownCondition(c) >= condMin);
            if (verifiedOnly) list = list.Where(CarValueCalculator.IsConditionKnown);
            if (dealsOnly) list = list.Where(c => { var r = PriceRating.Rate(c.purchasePrice, Estimate(c)); return r == DealRating.GreatDeal || r == DealRating.GoodPrice; });
            switch (sort)
            {
                case Sort.PriceLow: list = list.OrderBy(c => c.purchasePrice); break;
                case Sort.PriceHigh: list = list.OrderByDescending(c => c.purchasePrice); break;
                case Sort.Condition: list = list.OrderByDescending(ShownCondition); break;
                case Sort.BestDeal: list = list.OrderBy(c => (float)c.purchasePrice / Mathf.Max(1, Estimate(c))); break;
            }
            var shown = list.ToList();

            gridHost.gameObject.SetActive(!listView);
            listHost.gameObject.SetActive(listView);
            if (listView) FillTable(shown);
            else
            {
                UIFactory.ClearChildren(grid);
                foreach (var car in shown) MarketListingItemUI.Create(grid, car, this);
                if (shown.Count == 0) UIFactory.EmptyState(grid, "No cars match your filters", "Try Reset filters, or come back tomorrow morning for new cars.");
            }

            bool mech = CarSalesServices.Upgrades && CarSalesServices.Upgrades.AccurateInspections;
            info.text = $"{shown.Count} of {all.Count} cars  -  new cars arrive every morning  -  " +
                        (mech ? $"<color={UIFactory.Hex(UIFactory.Good)}>Mechanic: true condition shown</color>" : $"Inspections cost ${CarValueCalculator.InspectionFee}. Sellers may exaggerate!");
        }

        void FillTable(List<CarInstance> cars)
        {
            var db = CarSalesServices.Database;
            var rows = new List<UIFactory.Table.Row>();
            foreach (var car in cars)
            {
                var d = db.Get(car.carId);
                if (!d) continue;
                bool known = CarValueCalculator.IsConditionKnown(car);
                float cond = ShownCondition(car);
                int est = Estimate(car);
                var r = PriceRating.Rate(car.purchasePrice, est);
                var c = car;
                rows.Add(new UIFactory.Table.Row
                {
                    cells = new[]
                    {
                        $"{d.brand} {d.displayName}",
                        d.year.ToString(),
                        $"{car.mileage:N0} km",
                        $"<color={UIFactory.Hex(CarParts.LabelColor(cond))}>{(known ? "" : "~")}{cond:0}% {CarParts.Label(cond)}</color>",
                        $"<color={UIFactory.Hex(UIFactory.Gold)}>{UIFactory.Money(car.purchasePrice)}</color>",
                        $"<color={UIFactory.Hex(DealColor(r))}>{PriceRating.Label(r)}</color>",
                    },
                    keys = new IComparable[] { $"{d.brand} {d.displayName}", d.year, car.mileage, cond, car.purchasePrice, (float)car.purchasePrice / Mathf.Max(1, est) },
                    onClick = () => OpenDetails(c),
                });
            }
            table.SetRows(rows);
        }

        public static Color DealColor(DealRating r) =>
            r == DealRating.GreatDeal ? UIFactory.Good : r == DealRating.GoodPrice ? UIFactory.Info : r == DealRating.Overpriced ? UIFactory.Bad : UIFactory.TextDim;

        public void OpenDetails(CarInstance car) => CarSalesServices.UI.vehicleDetails.Show(car);

        public void Buy(CarInstance car)
        {
            int price = car.purchasePrice;
            if (CarSalesServices.Marketplace.TryBuy(car, out string msg))
            {
                var spot = car.parkingSpotIndex;
                CarSalesServices.UI.purchaseConfirm.Show(car, price, spot, msg);
            }
            else CarSalesServices.UI.Error(msg);
        }
    }
}
