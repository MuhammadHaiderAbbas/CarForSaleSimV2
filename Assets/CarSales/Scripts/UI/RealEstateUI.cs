using System.Linq;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>Computer page: buy homes, extra dealerships and income properties.</summary>
    public class RealEstateUI : ComputerPage
    {
        RectTransform list;
        TextMeshProUGUI summary;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 12f);
            summary = UIFactory.Text(Area, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.LE(summary, h: 52);
            list = UIFactory.ScrollList(Area, 10f);
        }

        public override void Refresh()
        {
            var props = CarSalesServices.Properties;
            UIFactory.ClearChildren(list);
            if (!props) return;
            summary.text = $"Property income: <color={UIFactory.Hex(UIFactory.Good)}><b>{UIFactory.Money(props.DailyIncome)}/night</b></color>   |   " +
                           $"Rent & upkeep: <color={UIFactory.Hex(UIFactory.Bad)}><b>{UIFactory.Money(props.DailyCosts)}/night</b></color>   |   " +
                           $"Portfolio value: <b>{UIFactory.Money(props.PortfolioValue)}</b>\n" +
                           $"<size=85%><color={UIFactory.Hex(UIFactory.TextDim)}>Income and rent are paid when you sleep. Better homes wake you earlier and boost reputation.</color></size>";

            foreach (var type in new[] { PropertyType.Home, PropertyType.Dealership, PropertyType.Investment })
            {
                string header = type == PropertyType.Home ? "HOMES" : type == PropertyType.Dealership ? "DEALERSHIPS" : "INVESTMENTS";
                UIFactory.LE(UIFactory.Text(list, header, 16, UIFactory.Accent, TextAlignmentOptions.Left, true), h: 26);
                foreach (var p in props.Properties.Where(x => x.type == type)) Row(p);
            }
        }

        void Row(PropertyInfo p)
        {
            var props = CarSalesServices.Properties;
            bool owned = props.Owns(p.id);
            var card = UIFactory.Panel(list, p.id, owned ? UIFactory.CardLight : UIFactory.CardBg, 1.4f);
            UIFactory.LE(card, h: 108);
            var h = UIFactory.Horizontal(card, 14, 16f);
            h.childForceExpandHeight = false;
            string glyph = p.type == PropertyType.Home ? "H" : p.type == PropertyType.Dealership ? "D" : "$";
            Color c = p.type == PropertyType.Home ? UIFactory.Home : p.type == PropertyType.Dealership ? UIFactory.Accent : UIFactory.Gold;
            UIFactory.Icon(card.transform, c, glyph, 54f);
            var col = UIFactory.VCol(card.transform, 3f);
            UIFactory.Text(col, p.displayName + (props.ActiveHome == p.id ? $"  <color={UIFactory.Hex(UIFactory.Good)}>(your home)</color>" : ""), 22, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Text(col, p.description, 15, UIFactory.TextDim, TextAlignmentOptions.Left);
            string money = p.dailyIncome > 0 ? $"+{UIFactory.Money(p.dailyIncome)}/day" : p.dailyCost > 0 ? $"-{UIFactory.Money(p.dailyCost)}/day" : "";
            var info = UIFactory.Text(card.transform, (p.price > 0 ? UIFactory.Money(p.price) : "Free") + (money != "" ? $"\n<size=75%>{money}</size>" : ""),
                                      22, owned ? UIFactory.TextDim : UIFactory.Gold, TextAlignmentOptions.Right, true);
            UIFactory.LE(info, w: 150, flexW: 0);

            var place = MapPlace.All.FirstOrDefault(m => m && m.placeName == p.displayName);
            if (place)
            {
                var gps = UIFactory.Btn(card.transform, "GPS", UIFactory.Neutral, () => GpsSystem.SetTarget(place), 44f, 15f);
                UIFactory.LE(gps, h: 44, w: 70, flexW: 0);
            }

            if (!owned)
            {
                var buy = UIFactory.Btn(card.transform, "Buy", UIFactory.Good, () =>
                {
                    if (props.Buy(p.id, out string msg)) CarSalesServices.UI.Success("Property purchased", msg);
                    else CarSalesServices.UI.Error(msg);
                    Refresh();
                }, 50f, 19f);
                buy.interactable = CarSalesServices.Economy.CanAfford(p.price);
                UIFactory.LE(buy, h: 50, w: 120, flexW: 0);
            }
            else if (p.type == PropertyType.Home && props.ActiveHome != p.id)
            {
                var move = UIFactory.Btn(card.transform, "Move in", UIFactory.Accent, () => { props.MoveInto(p.id); Refresh(); }, 50f, 18f);
                UIFactory.LE(move, h: 50, w: 120, flexW: 0);
            }
            else
            {
                var chip = UIFactory.Text(card.transform, "OWNED", 18, UIFactory.Good, TextAlignmentOptions.Center, true);
                UIFactory.LE(chip, w: 120, flexW: 0);
            }
        }
    }
}
