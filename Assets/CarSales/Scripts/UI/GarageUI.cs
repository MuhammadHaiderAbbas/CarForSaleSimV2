using CarSales.Foundation;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>Garage: pick repairs (per part or full service) and watch the mechanics work.</summary>
    public class GarageUI : UIPanel
    {
        CarDisplay car;
        RawImage photo;
        TextMeshProUGUI condText, discountText, totalText, statusText, startLabel;
        UIFactory.Bar condBar, progress;
        RectTransform options;
        Button startButton, backButton;
        readonly HashSet<CarPart> selected = new HashSet<CarPart>();
        bool fullService, working;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Garage", new Vector2(1220f, 700f), Back);
            UIFactory.Horizontal(body, 0, 28f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            var left = UIFactory.VCol(body, 10f, "Left", 520f);
            photo = UIFactory.CarPicture(left, 300f);
            condText = UIFactory.Text(left, "", 20, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            condBar = UIFactory.ProgressBar(left, UIFactory.Good, 14f);
            discountText = UIFactory.Text(left, "", 16, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.Spacer(left);
            statusText = UIFactory.Text(left, "", 18, UIFactory.Warn, TextAlignmentOptions.Left, true);
            progress = UIFactory.ProgressBar(left, UIFactory.Warn, 16f);

            var right = UIFactory.VCol(body, 10f, "Right");
            UIFactory.LE(UIFactory.Text(right, "Repair Options", 24, UIFactory.TextMain, TextAlignmentOptions.Left, true), h: 34);
            options = UIFactory.VCol(right, 8f, "Options");
            UIFactory.Spacer(right);
            totalText = UIFactory.Text(right, "", 22, UIFactory.Gold, TextAlignmentOptions.Right, true);
            UIFactory.LE(totalText, h: 32);
            var row = UIFactory.HRow(right, 56, 12);
            backButton = UIFactory.Btn(row, "Back", UIFactory.Neutral, Back, 54f);
            startButton = UIFactory.Btn(row, "Start Repair", UIFactory.Accent, StartRepair, out startLabel, 54f, 20f);
        }

        public void Show(CarDisplay target)
        {
            car = target;
            selected.Clear();
            fullService = false;
            OpenSelf();
            Refresh();
        }

        void Refresh()
        {
            var d = car.Data;
            UIFactory.SetCarPicture(photo, d);
            condText.text = $"Condition: <color={UIFactory.Hex(CarParts.LabelColor(d.condition))}>{CarParts.Label(d.condition)} ({d.condition:0}%)</color>";
            condBar.Set(d.condition / 100f);
            condBar.SetColor(CarParts.LabelColor(d.condition));
            int mech = CarSalesServices.Upgrades ? CarSalesServices.Upgrades.Level(UpgradeType.Mechanic) : 0;
            discountText.text = mech > 0 ? $"Mechanic Workshop level {mech}: repairs {mech * 15}% cheaper." : "Tip: the Mechanic Workshop upgrade makes repairs cheaper.";
            statusText.text = "";
            progress.background.gameObject.SetActive(false);

            UIFactory.ClearChildren(options);
            bool any = false;
            foreach (var part in CarParts.All)
            {
                float v = CarParts.Get(d, part);
                if (v >= 99.5f) continue;
                any = true;
                var p = part;
                OptionRow(CarParts.RepairName(part), $"{CarParts.DisplayName(part)} at {v:0}%", CarParts.Glyph(part),
                          CarParts.LabelColor(v), CarValueCalculator.PartRepairCost(d, part), !fullService && selected.Contains(part),
                          () => { fullService = false; if (!selected.Remove(p)) selected.Add(p); Refresh(); });
            }
            if (any)
                OptionRow("Full Service", "Every part to 100%  -  10% discount", "F", UIFactory.Accent,
                          CarValueCalculator.FullServiceCost(d), fullService,
                          () => { fullService = !fullService; selected.Clear(); Refresh(); });
            else
            {
                var t = UIFactory.Text(options, "This car is in perfect condition. Nothing to repair!", 20, UIFactory.Good, TextAlignmentOptions.Left, true);
                UIFactory.LE(t, h: 60);
            }

            int total = Total();
            totalText.text = total > 0 ? $"Total: {UIFactory.Money(total)}" : "";
            startButton.interactable = total > 0 && !working;
            startLabel.text = total > 0 ? $"Start Repair ({UIFactory.Money(total)})" : "Select repairs";
        }

        void OptionRow(string title, string subtitle, string glyph, Color color, int cost, bool isSelected, UnityEngine.Events.UnityAction onClick)
        {
            var row = UIFactory.Panel(options, title, isSelected ? UIFactory.AccentDark : UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: 58);
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row;
            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(() => CarSalesServices.PlaySfx(Sfx.Click, 0.5f));
            var h = UIFactory.Horizontal(row, 8, 14f);
            h.padding = new RectOffset(12, 16, 8, 8);
            h.childForceExpandHeight = false;
            UIFactory.Icon(row.transform, color, glyph, 38f);
            var col = UIFactory.VCol(row.transform, 0f);
            UIFactory.Text(col, (isSelected ? $"<color={UIFactory.Hex(UIFactory.Info)}>[x]</color> " : "[  ] ") + title, 19, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Text(col, subtitle, 14, UIFactory.TextDim, TextAlignmentOptions.Left);
            var price = UIFactory.Text(row.transform, UIFactory.Money(cost), 20, UIFactory.Gold, TextAlignmentOptions.Right, true);
            UIFactory.LE(price, w: 120, flexW: 0);
        }

        int Total()
        {
            if (fullService) return CarValueCalculator.FullServiceCost(car.Data);
            int sum = 0;
            foreach (var p in selected) sum += CarValueCalculator.PartRepairCost(car.Data, p);
            return sum;
        }

        void StartRepair()
        {
            if (working) return;
            int total = Total();
            if (total <= 0) return;
            if (!CarSalesServices.Economy.CanAfford(total)) { CarSalesServices.UI.Error("Not enough money for these repairs!"); return; }
            StartCoroutine(RepairRoutine());
        }

        IEnumerator RepairRoutine()
        {
            working = true;
            startButton.interactable = backButton.interactable = false;
            progress.background.gameObject.SetActive(true);
            string[] steps = { "Lifting the car...", "Mechanics at work...", "Testing...", "Done!" };
            float duration = 2f, t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                progress.Set(t / duration);
                statusText.text = steps[Mathf.Min(steps.Length - 1, (int)(t / duration * (steps.Length - 1)))];
                yield return null;
            }
            Apply();
        }

        void Apply()
        {
            if (!working) return;
            working = false;
            backButton.interactable = true;
            int total = Total();
            if (!CarSalesServices.Economy.TrySpend(total, TxCategory.Repair, (fullService ? "Full service - " : "Repairs - ") + car.Data.Name, car.Data.instanceId)) { CarSalesServices.UI.Error("Not enough money!"); Refresh(); return; }
            var d = car.Data;
            if (fullService) CarParts.RepairAll(d);
            else foreach (var p in selected) CarParts.Set(d, p, 100f);
            d.repairSpent += total;
            if (d.state == CarState.NeedsRepair && d.condition >= CarValueCalculator.RepairThreshold) d.state = CarState.Ready;
            CarSalesServices.DayCycle.RegisterExpense(total);
            if (CarSalesServices.Progress) CarSalesServices.Progress.Stats.carsServiced++;
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            CarSalesServices.UI.Success("Repair complete", $"Condition is now {d.condition:0}% ({CarParts.Label(d.condition)}).");
            selected.Clear();
            fullService = false;
            Refresh();
        }

        void OnDisable()
        {
            if (working) { StopAllCoroutines(); Apply(); }   // closing mid-repair still finishes it
        }

        void Back() { if (!working) CarSalesServices.UI.carMenu.Show(car); }
    }
}
