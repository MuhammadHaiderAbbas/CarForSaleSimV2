using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>Inventory page: Owned Vehicles / For Sale tabs, manage or quick-sell each car.</summary>
    public class InventoryUI : ComputerPage
    {
        bool showListedOnly;
        Button ownedTab, listedTab;
        TextMeshProUGUI ownedLabel, listedLabel, footer;
        RectTransform list;
        string confirmSellId;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 12f);
            var tabs = UIFactory.HRow(Area, 48, 0, "Tabs", true);
            ownedTab = UIFactory.Btn(tabs, "Owned Vehicles", UIFactory.Accent, () => { showListedOnly = false; Refresh(); }, out ownedLabel, 48f, 19f);
            listedTab = UIFactory.Btn(tabs, "For Sale", UIFactory.Neutral, () => { showListedOnly = true; Refresh(); }, out listedLabel, 48f, 19f);
            list = UIFactory.ScrollList(Area, 10f);
            footer = UIFactory.Text(Area, "", 17, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(footer, h: 28);
        }

        public override void Refresh()
        {
            var owned = CarSalesServices.Inventory.OwnedCars;
            var listed = owned.Where(c => c.state == CarState.Listed).ToList();
            ownedLabel.text = $"Owned Vehicles ({owned.Count})";
            listedLabel.text = $"For Sale ({listed.Count})";
            UIFactory.SetButtonColor(ownedTab, showListedOnly ? UIFactory.Neutral : UIFactory.Accent);
            UIFactory.SetButtonColor(listedTab, showListedOnly ? UIFactory.Accent : UIFactory.Neutral);

            UIFactory.ClearChildren(list);
            var cars = showListedOnly ? listed : owned.ToList();
            foreach (var car in cars) Row(car);
            if (cars.Count == 0)
            {
                var t = UIFactory.Text(list, showListedOnly ? "No cars listed for sale. Open a car and use Set Price."
                                                          : "You don't own any cars. Buy some in the Marketplace!",
                                       20, UIFactory.TextDim, TextAlignmentOptions.Center);
                UIFactory.LE(t, h: 80);
            }
            int total = owned.Sum(c => CarValueCalculator.MarketValue(c));
            footer.text = $"Total vehicles: {owned.Count}   |   Dealership spots: {DealershipManager.TotalCapacity}   |   Total value: {UIFactory.Money(total)}";
        }

        void Row(CarInstance car)
        {
            var def = CarSalesServices.Database.Get(car.carId);
            var row = UIFactory.Panel(list, "Row", UIFactory.CardBg, 1.4f);
            UIFactory.LE(row, h: 104);
            var h = UIFactory.Horizontal(row, 10, 16f);
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            var pic = UIFactory.CarPicture(row.transform, 84f, 140f);
            UIFactory.SetCarPicture(pic, car);

            var col = UIFactory.VCol(row.transform, 4f);
            UIFactory.Text(col, $"{def.year} {def.brand} {def.displayName}", 21, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Text(col,
                $"Condition: <color={UIFactory.Hex(CarParts.LabelColor(car.condition))}>{CarParts.Label(car.condition)} ({car.condition:0}%)</color>   " +
                $"Clean: {car.cleanliness:0}%   Fuel: {car.fuel:0}%   {car.mileage:N0} km\n<color={UIFactory.Hex(UIFactory.Accent)}>{CarSalesServices.Vehicles.LocationText(car)}</color>",
                15, UIFactory.TextDim, TextAlignmentOptions.Left);

            Color chipColor = car.personal ? UIFactory.Personal : car.state == CarState.Listed ? UIFactory.Good : car.state == CarState.NeedsRepair ? UIFactory.Warn : UIFactory.Neutral;
            string chipText = car.personal ? "Personal car" : car.state == CarState.Listed ? $"For sale {UIFactory.Money(car.listedPrice)}" : car.state == CarState.NeedsRepair ? "Needs repair" : "Ready";
            var chipRow = UIFactory.HRow(col, 26, 6, "Chips", false);
            UIFactory.Chip(chipRow, chipText, chipColor, out _);
            UIFactory.Spacer(chipRow, 0f, 1f);

            var value = UIFactory.Text(row.transform, $"<size=70%><color={UIFactory.Hex(UIFactory.TextDim)}>Value</color></size>\n{UIFactory.Money(CarValueCalculator.MarketValue(car))}",
                                       22, UIFactory.Gold, TextAlignmentOptions.Right, true);
            UIFactory.LE(value, w: 150, flexW: 0);

            var file = UIFactory.Btn(row.transform, "File", UIFactory.Neutral, () => CarSalesServices.UI.vehicleDetails.ShowOwned(car), 44f, 17f);
            UIFactory.LE(file, h: 44, w: 80, flexW: 0);
            var manage = UIFactory.Btn(row.transform, "Manage", UIFactory.Accent, () => Manage(car), 44f, 17f);
            UIFactory.LE(manage, h: 44, w: 120, flexW: 0);
            bool confirming = confirmSellId == car.instanceId;
            var sell = UIFactory.Btn(row.transform, confirming ? $"Confirm\n{UIFactory.Money(CarValueCalculator.QuickSellPrice(car))}" : "Quick Sell",
                                     confirming ? UIFactory.Bad : UIFactory.Warn, () => QuickSell(car), 44f, 15f);
            UIFactory.LE(sell, h: 44, w: 130, flexW: 0);
            sell.interactable = !car.personal;
        }

        void Manage(CarInstance car)
        {
            var display = CarSalesServices.Vehicles.GetDisplay(car.instanceId);
            if (display) CarSalesServices.UI.carMenu.Show(display);
            else CarSalesServices.UI.Error("That car isn't on the lot right now.");
        }

        void QuickSell(CarInstance car)
        {
            if (confirmSellId != car.instanceId)
            {
                confirmSellId = car.instanceId;
                CarSalesServices.UI.Notify($"Wholesale offer: {UIFactory.Money(CarValueCalculator.QuickSellPrice(car))} (75% of value). Click again to confirm.");
                Refresh();
                return;
            }
            confirmSellId = null;
            var display = CarSalesServices.Vehicles.GetDisplay(car.instanceId);
            if (display) CarSalesServices.Vehicles.QuickSell(display);
            Refresh();
        }

        void OnDisable() => confirmSellId = null;
    }
}
