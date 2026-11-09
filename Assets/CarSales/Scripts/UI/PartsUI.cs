using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>Every individual component of a car, grouped like the inspection report (opened from the inspection).</summary>
    public class PartsUI : UIPanel
    {
        CarInstance car;
        CarDisplay owner;
        TextMeshProUGUI title, summary;
        RectTransform list;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "All Parts", new Vector2(980f, 820f), Back);
            UIFactory.Vertical(body, 0, 10f);
            title = UIFactory.Text(body, "", 26, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(title, h: 34);
            summary = UIFactory.Text(body, "", 16, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(summary, h: 44);
            var box = UIFactory.Panel(body, "List", UIFactory.CardBg, 1.4f);
            UIFactory.LE(box, flexH: 1);
            UIFactory.Vertical(box, 12, 0f);
            list = UIFactory.ScrollList(box.transform, 3f, "PartsScroll");
            UIFactory.Btn(body, "Back to inspection", UIFactory.Neutral, Back, 50f);
        }

        public void Show(CarInstance target, CarDisplay ownerCar)
        {
            car = target;
            owner = ownerCar;
            OpenSelf();
            CarParts.EnsureParts(car);
            var def = CarSalesServices.Database.Get(car.carId);
            var pt = CarParts.PowertrainOf(car);
            title.text = def ? $"{def.year} {def.brand} {def.displayName}" : car.carId;
            var e = CarParts.Effects(car);
            summary.text = $"{pt} - {car.components.Count} parts - overall {car.condition:0}%.   " +
                           $"Driving: power {e.Power:0%}, brakes {e.Brakes:0%}, grip {(e.FrontGrip + e.RearGrip) * 0.5f:0%}, shocks {e.Damping:0%}" +
                           (e.CanStart ? "" : $"   <color={UIFactory.Hex(UIFactory.Bad)}><b>WON'T START</b></color>");

            UIFactory.ClearChildren(list);
            foreach (var part in CarParts.All)
            {
                var group = (ComponentGroup)(int)part;
                float g = CarParts.Get(car, part);
                var head = UIFactory.Text(list, $"<b>{CarParts.DisplayName(part).ToUpper()}</b>   <color={UIFactory.Hex(CarParts.LabelColor(g))}>{g:0}%</color>",
                                          18, UIFactory.TextMain);
                UIFactory.LE(head, h: 34);
                foreach (var def2 in ComponentCatalog.InGroup(group, pt).OrderBy(d => ComponentSet.Get(car.components, d.Id)))
                {
                    float v = ComponentSet.Get(car.components, def2.Id);
                    var c = CarParts.LabelColor(v);
                    var row = UIFactory.HRow(list, 26, 12, "Part");
                    UIFactory.LE(UIFactory.Text(row, "   " + def2.Name, 16, UIFactory.TextMain), w: 300);
                    var holder = UIFactory.VCol(row, 0f, "Bar");
                    holder.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    var bar = UIFactory.ProgressBar(holder, c, 10f);
                    bar.Set(v / 100f);
                    UIFactory.LE(UIFactory.Text(row, $"<color={UIFactory.Hex(c)}>{CarParts.Label(v)}</color> {v:0}%", 16, UIFactory.TextMain,
                                                TextAlignmentOptions.Right, true), w: 150);
                }
            }
        }

        void Back() => CarSalesServices.UI.inspection.Show(car, owner);
    }
}
