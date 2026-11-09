using CarSales.Foundation;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    public class CarWashUI : UIPanel
    {
        CarDisplay car;
        RawImage photo;
        TextMeshProUGUI cleanText, costText, statusText, startLabel;
        UIFactory.Bar cleanBar, progress;
        Button startButton, backButton;
        bool working;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Car Wash", new Vector2(820f, 700f), Back);
            UIFactory.Vertical(body, 0, 12f);
            photo = UIFactory.CarPicture(body, 330f);
            cleanText = UIFactory.Text(body, "", 22, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            cleanBar = UIFactory.ProgressBar(body, UIFactory.Accent, 16f);
            costText = UIFactory.Text(body, "", 26, UIFactory.Gold, TextAlignmentOptions.Left, true);
            statusText = UIFactory.Text(body, "", 18, UIFactory.Accent, TextAlignmentOptions.Left, true);
            progress = UIFactory.ProgressBar(body, UIFactory.Info, 14f);
            UIFactory.Spacer(body);
            var row = UIFactory.HRow(body, 56, 12);
            backButton = UIFactory.Btn(row, "Back", UIFactory.Neutral, Back, 54f);
            startButton = UIFactory.Btn(row, "Start Wash", UIFactory.Accent, StartWash, out startLabel, 54f, 20f);
        }

        public void Show(CarDisplay target)
        {
            car = target;
            OpenSelf();
            Refresh();
        }

        void Refresh()
        {
            var d = car.Data;
            UIFactory.SetCarPicture(photo, d);
            cleanText.text = $"Cleanliness: {d.cleanliness:0}%";
            cleanBar.Set(d.cleanliness / 100f);
            int cost = CarValueCalculator.CleanCost();
            costText.text = $"Wash Cost: {UIFactory.Money(cost)}";
            statusText.text = d.cleanliness >= 99.5f ? "Already spotless!" : "Clean cars sell faster and for more money.";
            progress.background.gameObject.SetActive(false);
            startButton.interactable = d.cleanliness < 99.5f && !working;
            startLabel.text = $"Start Wash ({UIFactory.Money(cost)})";
        }

        void StartWash()
        {
            if (working) return;
            if (!CarSalesServices.Economy.CanAfford(CarValueCalculator.CleanCost())) { CarSalesServices.UI.Error("Not enough money!"); return; }
            StartCoroutine(WashRoutine());
        }

        IEnumerator WashRoutine()
        {
            working = true;
            startButton.interactable = backButton.interactable = false;
            progress.background.gameObject.SetActive(true);
            float start = car.Data.cleanliness, duration = 2.2f, t = 0f;
            string[] steps = { "Pre-rinse...", "Foam & brushes...", "Rinse...", "Drying..." };
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = t / duration;
                progress.Set(k);
                cleanBar.Set(Mathf.Lerp(start, 100f, k) / 100f);
                statusText.text = steps[Mathf.Min(steps.Length - 1, (int)(k * steps.Length))];
                yield return null;
            }
            Apply();
        }

        void Apply()
        {
            if (!working) return;
            working = false;
            backButton.interactable = true;
            int cost = CarValueCalculator.CleanCost();
            if (!CarSalesServices.Economy.TrySpend(cost, TxCategory.Cleaning, $"Car wash - {car.Data.Name}", car.Data.instanceId)) { CarSalesServices.UI.Error("Not enough money!"); Refresh(); return; }
            car.Data.cleanliness = 100f;
            car.Data.repairSpent += cost;
            CarSalesServices.DayCycle.RegisterExpense(cost);
            if (CarSalesServices.Progress) CarSalesServices.Progress.Stats.carsServiced++;
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            CarSalesServices.UI.Success("Sparkling clean!", $"{car.Definition.displayName} is now 100% clean.");
            Refresh();
        }

        void OnDisable()
        {
            if (working) { StopAllCoroutines(); Apply(); }
        }

        void Back() { if (!working) CarSalesServices.UI.carMenu.Show(car); }
    }
}
