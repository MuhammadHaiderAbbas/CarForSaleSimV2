using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    public class PriceUI : UIPanel
    {
        CarDisplay car;
        int marketValue = 1;
        Slider slider;
        RawImage photo;
        TextMeshProUGUI nameText, priceText, markupText, adviceText;
        Button unlistButton;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Set Selling Price", new Vector2(900f, 640f), () => CarSalesServices.UI.carMenu.Show(car));
            UIFactory.Vertical(body, 0, 12f);
            var top = UIFactory.HRow(body, 170, 20, "Top", false);
            photo = UIFactory.CarPicture(top, 170f, 270f);
            var col = UIFactory.VCol(top, 4f);
            nameText = UIFactory.Text(col, "", 26, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            priceText = UIFactory.Text(col, "$0", 56, UIFactory.Gold, TextAlignmentOptions.Left, true);
            markupText = UIFactory.Text(col, "", 20, UIFactory.TextMain, TextAlignmentOptions.Left, true);

            slider = UIFactory.MakeSlider(body, UIFactory.Gold, 34f);
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(UpdateLabels);

            var quick = UIFactory.HRow(body, 44, 10, "Presets");
            Preset(quick, "90%", 0.9f);
            Preset(quick, "100%", 1f);
            Preset(quick, "110%  (recommended)", 1.1f);
            Preset(quick, "125%", 1.25f);

            adviceText = UIFactory.Text(body, "", 17, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.Spacer(body);
            var row = UIFactory.HRow(body, 56, 12);
            UIFactory.Btn(row, "Back", UIFactory.Neutral, () => CarSalesServices.UI.carMenu.Show(car), 54f);
            unlistButton = UIFactory.Btn(row, "Remove Listing", UIFactory.Bad, Unlist, 54f);
            UIFactory.Btn(row, "List for Sale", UIFactory.Good, Confirm, 54f, 21f);
        }

        void Preset(Transform parent, string label, float ratio) =>
            UIFactory.Btn(parent, label, UIFactory.CardLight, () => slider.value = Mathf.Round(marketValue * ratio / 100f) * 100f, 42f, 16f);

        public void Show(CarDisplay target)
        {
            car = target;
            OpenSelf();
            marketValue = Mathf.Max(1, CarValueCalculator.MarketValue(car.Data));
            UIFactory.SetCarPicture(photo, car.Data);
            nameText.text = $"{car.Definition.year} {car.Definition.brand} {car.Definition.displayName}";
            slider.minValue = 0;
            slider.maxValue = Mathf.Round(marketValue * 1.5f);
            slider.minValue = Mathf.Round(marketValue * 0.7f);
            slider.SetValueWithoutNotify(car.Data.listedPrice > 0 ? car.Data.listedPrice : Mathf.Round(marketValue * 1.1f / 100f) * 100f);
            unlistButton.gameObject.SetActive(car.Data.state == CarState.Listed);
            UpdateLabels(slider.value);
        }

        void UpdateLabels(float value)
        {
            float ratio = value / marketValue;
            Color c = ratio < 1f ? UIFactory.Good : ratio <= 1.2f ? UIFactory.Gold : UIFactory.Bad;
            priceText.text = UIFactory.Money(Mathf.RoundToInt(value));
            markupText.text = $"<color={UIFactory.Hex(c)}>{ratio * 100f:0}% of market value</color>  <color={UIFactory.Hex(UIFactory.TextDim)}>({UIFactory.Money(marketValue)})</color>";
            int profit = Mathf.RoundToInt(value) - car.Data.purchasePrice - car.Data.repairSpent;
            adviceText.text = (ratio < 1f ? "Cheap: sells fast, but you leave money on the table."
                             : ratio <= 1.2f ? "Fair price: most customers will consider it."
                             : "Expensive: few customers will be interested.") +
                              $"\nProfit at this price: <color={UIFactory.Hex(profit >= 0 ? UIFactory.Good : UIFactory.Bad)}><b>{UIFactory.Money(profit)}</b></color>" +
                              (car.Data.cleanliness < 40 ? $"\n<color={UIFactory.Hex(UIFactory.Warn)}>This car is dirty - customers will be less interested.</color>" : "");
        }

        void Confirm()
        {
            if (car.Data.state != CarState.Listed && CarSalesServices.Progress) CarSalesServices.Progress.Stats.carsListed++;
            car.Data.listedPrice = Mathf.RoundToInt(slider.value);
            car.Data.state = CarState.Listed;
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CarSalesServices.UI.Success("Listed for sale", $"{car.Definition.displayName} is on sale for {UIFactory.Money(car.Data.listedPrice)}.");
            CarSalesServices.UI.Close();
        }

        void Unlist()
        {
            car.Data.state = CarValueCalculator.DefaultState(car.Data);
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CarSalesServices.UI.carMenu.Show(car);
        }
    }
}
