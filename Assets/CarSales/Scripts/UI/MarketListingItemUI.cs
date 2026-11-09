using CarSales.Foundation;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>One car card in the marketplace grid.</summary>
    public class MarketListingItemUI : MonoBehaviour
    {
        public static MarketListingItemUI Create(Transform parent, CarInstance car, MarketplaceUI owner)
        {
            var def = CarSalesServices.Database.Get(car.carId);
            var card = UIFactory.Panel(parent, "Card_" + car.carId, UIFactory.CardBg, 1.3f);
            var v = UIFactory.Vertical(card, 10, 5f);
            v.padding = new RectOffset(10, 10, 10, 12);

            var pic = UIFactory.CarPicture(card.transform, 150f);
            UIFactory.SetCarPicture(pic, car);
            bool verified = CarValueCalculator.IsConditionKnown(car);
            var rating = PriceRating.Rate(car.purchasePrice, CarValueCalculator.MarketValue(car, useReported: !verified));
            var badges = UIFactory.HRow(pic.transform.parent, 24, 6, "Badges", false);
            badges.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            UIFactory.Place(badges, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -8f), new Vector2(240f, 24f));
            if (rating != DealRating.Fair && rating != DealRating.Unknown) UIFactory.Badge(badges, PriceRating.Label(rating), MarketplaceUI.DealColor(rating));
            if (verified) UIFactory.Badge(badges, "Verified", UIFactory.Good);
            if (car.documents != null && car.documents.generated && !car.documents.v5cPresent) UIFactory.Badge(badges, "No logbook", UIFactory.Bad);
            else if (!CarRecords.MotValid(car)) UIFactory.Badge(badges, "MOT expired", UIFactory.Warn);
            UIFactory.Spacer(badges, 0f, 1f);

            var name = UIFactory.Text(card.transform, $"{def.brand} {def.displayName}", 20, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(name, h: 26);
            var price = UIFactory.Text(card.transform, UIFactory.Money(car.purchasePrice), 24, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(price, h: 30);
            bool known = CarValueCalculator.IsConditionKnown(car);
            float cond = known ? car.condition : car.reportedCondition;
            var details = UIFactory.Text(card.transform,
                $"{def.year}  |  {car.mileage:N0} km\n<color={UIFactory.Hex(CarParts.LabelColor(cond))}>{(known ? "" : "~")}{CarParts.Label(cond)} {cond:0}%</color>" +
                (known ? $"  <color={UIFactory.Hex(UIFactory.Good)}>(verified)</color>" : $"  <color={UIFactory.Hex(UIFactory.TextDim)}>(seller's claim)</color>"),
                15, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(details, h: 40);
            UIFactory.Btn(card.transform, "View Details", UIFactory.Accent, () => owner.OpenDetails(car), 38f, 17f);
            return card.gameObject.AddComponent<MarketListingItemUI>();
        }
    }
}
