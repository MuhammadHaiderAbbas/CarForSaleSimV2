using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>"My Vehicle": photo, stats, location, fuel and every action (drive, repair, wash, refuel, tow, sell).</summary>
    public class CarMenuUI : UIPanel
    {
        CarDisplay car;
        RawImage photo;
        TextMeshProUGUI nameText, valueText, condLabel, cleanLabel, fuelLabel, locationText, mileage, fuel, bought, spent, invested, profit, listed;
        TextMeshProUGUI repairLabel, washLabel, refuelLabel, priceLabel, quickSellLabel;
        Image stateChip;
        TextMeshProUGUI stateChipText;
        UIFactory.Bar condBar, cleanBar, fuelBar;
        Button driveButton, repairButton, washButton, refuelButton, priceButton, quickSellButton;
        bool confirmQuickSell;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "My Vehicle", new Vector2(1300f, 780f), CloseAll);
            UIFactory.Horizontal(body, 0, 28f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            var left = UIFactory.VCol(body, 8f, "Left", 600f);
            photo = UIFactory.CarPicture(left, 300f);
            var chips = UIFactory.HRow(left, 30, 8, "Chips", false);
            stateChip = UIFactory.Chip(chips, "", UIFactory.Neutral, out stateChipText, 15f);
            UIFactory.Spacer(chips, 0f, 1f);
            locationText = UIFactory.Text(left, "", 17, UIFactory.Accent, TextAlignmentOptions.Left, true);
            condLabel = UIFactory.Text(left, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            condBar = UIFactory.ProgressBar(left, UIFactory.Good, 12f);
            cleanLabel = UIFactory.Text(left, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            cleanBar = UIFactory.ProgressBar(left, UIFactory.Accent, 12f);
            fuelLabel = UIFactory.Text(left, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            fuelBar = UIFactory.ProgressBar(left, UIFactory.Gold, 12f);
            UIFactory.Spacer(left);

            var right = UIFactory.VCol(body, 5f, "Right");
            nameText = UIFactory.Text(right, "", 30, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(nameText, h: 40);
            valueText = UIFactory.Text(right, "", 28, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(valueText, h: 38);
            UIFactory.Divider(right);
            mileage = UIFactory.SpecRow(right, "Mileage", 26);
            fuel = UIFactory.SpecRow(right, "Fuel / Transmission", 26);
            bought = UIFactory.SpecRow(right, "Bought for", 26);
            spent = UIFactory.SpecRow(right, "Repairs & cleaning", 26);
            invested = UIFactory.SpecRow(right, "Total invested", 26);
            profit = UIFactory.SpecRow(right, "Potential profit", 26);
            listed = UIFactory.SpecRow(right, "Listed price", 26);
            UIFactory.Spacer(right);

            var gridRt = UIFactory.Rect("Actions", right);
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(196f, 54f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            UIFactory.LE(gridRt, h: 190);
            driveButton = UIFactory.Btn(gridRt, "Drive (F)", UIFactory.Special, () => CarSalesServices.TestDrive.StartDrive(car), 54f, 17f);
            UIFactory.Btn(gridRt, "Inspect", UIFactory.Neutral, () => CarSalesServices.UI.inspection.Show(car.Data, car), 54f, 17f);
            priceButton = UIFactory.Btn(gridRt, "Sell / listing", UIFactory.Good, () => CarSalesServices.UI.listingEditor.Show(car), out priceLabel, 54f, 16f);
            repairButton = UIFactory.Btn(gridRt, "Repair", UIFactory.Warn, () => Service(ServiceType.Workshop), out repairLabel, 54f, 15f);
            washButton = UIFactory.Btn(gridRt, "Wash", UIFactory.Accent, () => Service(ServiceType.CarWash), out washLabel, 54f, 15f);
            refuelButton = UIFactory.Btn(gridRt, "Refuel", UIFactory.Gold, () => Service(ServiceType.FuelStation), out refuelLabel, 54f, 15f);
            UIFactory.Btn(gridRt, "Tow somewhere...", UIFactory.AccentDark, () => CarSalesServices.UI.phone.ShowTow(car), 54f, 16f);
            quickSellButton = UIFactory.Btn(gridRt, "Quick Sell", UIFactory.Bad, QuickSell, out quickSellLabel, 54f, 15f);
            UIFactory.Btn(gridRt, "Vehicle file", UIFactory.Neutral, () => CarSalesServices.UI.vehicleDetails.ShowOwned(car.Data), 54f, 17f);
        }

        public void Show(CarDisplay target)
        {
            car = target;
            confirmQuickSell = false;
            OpenSelf();
            Refresh();
        }

        bool IsAt(ServiceType type)
        {
            var zone = ServiceZone.Find(type);
            return zone && zone.Contains(car.transform.position);
        }

        string TowLabel(string verb, ServiceType type)
        {
            var dest = CarSalesServices.Tow ? CarSalesServices.Tow.DestinationFor(type) : null;
            if (dest == null) return verb + "\n(unavailable)";
            return $"Tow to {verb}\n{UIFactory.Money(CarSalesServices.Tow.TowCost(car.transform.position, dest.Position))}";
        }

        void Refresh()
        {
            var d = car.Data;
            var def = car.Definition;
            int value = CarValueCalculator.MarketValue(d);
            int inv = d.purchasePrice + d.repairSpent;
            UIFactory.SetCarPicture(photo, d);
            nameText.text = $"{def.year} {def.brand} {def.displayName}";
            valueText.text = $"<size=60%><color={UIFactory.Hex(UIFactory.TextDim)}>Market value </color></size>{UIFactory.Money(value)}";
            locationText.text = "Location: " + CarSalesServices.Vehicles.LocationText(d);
            condLabel.text = $"Condition: <color={UIFactory.Hex(CarParts.LabelColor(d.condition))}>{CarParts.Label(d.condition)} ({d.condition:0}%)</color>";
            condBar.Set(d.condition / 100f);
            condBar.SetColor(CarParts.LabelColor(d.condition));
            cleanLabel.text = $"Cleanliness: {d.cleanliness:0}%";
            cleanBar.Set(d.cleanliness / 100f);
            fuelLabel.text = $"Fuel: {d.fuel:0}%";
            fuelBar.Set(d.fuel / 100f);
            fuelBar.SetColor(d.fuel < 15f ? UIFactory.Bad : UIFactory.Gold);
            mileage.text = $"{d.mileage:N0} km";
            fuel.text = $"{(string.IsNullOrEmpty(def.fuelType) ? "Petrol" : def.fuelType)} / {(string.IsNullOrEmpty(def.transmission) ? "Automatic" : def.transmission)}";
            bought.text = d.personal ? "Your own car" : UIFactory.Money(d.purchasePrice);
            spent.text = UIFactory.Money(d.repairSpent);
            invested.text = UIFactory.Money(inv);
            profit.text = d.personal ? "-" : $"<color={UIFactory.Hex(value - inv >= 0 ? UIFactory.Good : UIFactory.Bad)}>{UIFactory.Money(value - inv)}</color>";
            listed.text = d.state == CarState.Listed ? UIFactory.Money(d.listedPrice) : "Not listed";

            stateChip.color = d.personal ? UIFactory.Personal : d.state == CarState.Listed ? UIFactory.Good : d.state == CarState.NeedsRepair ? UIFactory.Warn : UIFactory.Neutral;
            stateChipText.text = d.personal ? "PERSONAL CAR" : d.state == CarState.Listed ? "FOR SALE" : d.state == CarState.NeedsRepair ? "NEEDS REPAIR" : "READY";

            driveButton.interactable = CarSalesServices.TestDrive && !CarSalesServices.TestDrive.IsDriving && d.fuel > 0.5f;
            bool atSpot = d.location == CarLocation.Spot;
            priceButton.interactable = !d.personal && atSpot;
            priceLabel.text = d.personal ? "Personal car" : atSpot ? (d.state == CarState.Listed ? "Edit listing" : "Sell / listing") : "Park at your\ndealership to sell";
            repairLabel.text = IsAt(ServiceType.Workshop) ? "Repair\n(at workshop)" : TowLabel("Workshop", ServiceType.Workshop);
            washLabel.text = IsAt(ServiceType.CarWash) ? "Wash\n(at car wash)" : TowLabel("Car Wash", ServiceType.CarWash);
            refuelLabel.text = IsAt(ServiceType.FuelStation) ? $"Refuel\n{UIFactory.Money(CarSalesServices.Vehicles.RefuelCost(d))}" : TowLabel("Fuel Station", ServiceType.FuelStation);
            quickSellButton.interactable = !d.personal;
            quickSellLabel.text = confirmQuickSell ? $"Confirm {UIFactory.Money(CarValueCalculator.QuickSellPrice(d))}" : "Quick Sell";
        }

        /// <summary>Uses the service if the car is already there, otherwise tows it there first.</summary>
        void Service(ServiceType type)
        {
            if (!IsAt(type))
            {
                var dest = CarSalesServices.Tow.DestinationFor(type);
                if (dest == null) { CarSalesServices.UI.Error("That place doesn't exist in this scene."); return; }
                if (!CarSalesServices.Tow.Tow(car, dest, out string msg)) { CarSalesServices.UI.Error(msg); return; }
                CarSalesServices.UI.Success("Towed", msg);
            }
            switch (type)
            {
                case ServiceType.Workshop: CarSalesServices.UI.garage.Show(car); break;
                case ServiceType.CarWash: CarSalesServices.UI.carWash.Show(car); break;
                case ServiceType.FuelStation: CarSalesServices.Vehicles.Refuel(car); Refresh(); break;
            }
        }

        void QuickSell()
        {
            if (!confirmQuickSell)
            {
                confirmQuickSell = true;
                CarSalesServices.UI.Notify($"A wholesaler offers {UIFactory.Money(CarValueCalculator.QuickSellPrice(car.Data))} (75% of value). Click again to confirm.");
                Refresh();
                return;
            }
            CarSalesServices.UI.Close();
            CarSalesServices.Vehicles.QuickSell(car);
        }
    }
}
