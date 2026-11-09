using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>Customers app: everyone who visited (buyer type, visits, purchases, satisfaction) and their reviews.</summary>
    public class CrmPageUI : ComputerPage
    {
        UIFactory.TabBar views;
        RectTransform customersView, reviewsView, reviewList;
        UIFactory.Table table;
        TextMeshProUGUI summary;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 10f);
            summary = UIFactory.Text(Area, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(summary, h: 28);
            var top = UIFactory.HRow(Area, 44, 10, "Top", false);
            views = UIFactory.Tabs(top, new[] { "Customers", "Reviews" }, _ => Refresh(), 42f, 200f, 16f);
            var od = UIFactory.Btn(top, $"Book an open day tomorrow ({UIFactory.Money(CarSales.Sim.WorldSim.OpenDayCost)}, +60% customers)", UIFactory.Gold, () => { if (CarSalesServices.World) CarSalesServices.World.BookOpenDay(); }, 42f, 14f);
            UIFactory.LE(od, h: 42, w: 420, flexW: 0);
            var host = UIFactory.Rect("Views", Area);
            UIFactory.LE(host, flexH: 1, flexW: 1);
            customersView = UIFactory.Rect("Customers", host); UIFactory.Stretch(customersView);
            UIFactory.Vertical(customersView, 0, 4f);
            table = UIFactory.MakeTable(customersView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Name", 180f), new UIFactory.TableColumn("Type", 200f), new UIFactory.TableColumn("Visits", 90f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Bought", 90f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Spent", 140f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Satisfaction", 140f), new UIFactory.TableColumn("Last car"), new UIFactory.TableColumn("Last visit", 110f),
            }, 36f);
            table.SetEmptyText("No customers yet. List cars at your dealership and open the shop.");
            table.SetSort(7, true);
            reviewsView = UIFactory.Rect("Reviews", host); UIFactory.Stretch(reviewsView);
            UIFactory.Vertical(reviewsView, 0, 6f);
            reviewList = UIFactory.ScrollList(reviewsView, 8f);
            views.Select(0, false);
        }

        static string Stars(float s) => s < 0f ? "-" : new string('*', Mathf.RoundToInt(s)) + $"  {s:0.0}";

        public override void Refresh()
        {
            var crm = CarSalesServices.Crm;
            customersView.gameObject.SetActive(views.Index == 0);
            reviewsView.gameObject.SetActive(views.Index == 1);
            if (!crm) return;
            var st = crm.State;
            summary.text = $"{st.customers.Count} customers  -  {st.customers.Sum(c => c.purchases)} sales  -  average review " +
                           (st.reviews.Count > 0 ? $"<color={UIFactory.Hex(crm.AverageStars >= 3.5f ? UIFactory.Good : UIFactory.Warn)}>{crm.AverageStars:0.0} / 5</color> ({st.reviews.Count})" : "none yet") +
                           (st.complaints.Count > 0 ? $"  -  <color={UIFactory.Hex(UIFactory.Bad)}>{st.complaints.Count} complaint(s) brewing</color>" : "");
            if (views.Index == 0)
            {
                table.SetRows(st.customers.Select(c => new UIFactory.Table.Row
                {
                    cells = new[] { c.name, Buyers.Get((PersonaId)Mathf.Clamp(c.persona, 0, 8)).Name, c.visits.ToString(), c.purchases.ToString(), UIFactory.Money(c.spent),
                                    Stars(c.satisfaction), string.IsNullOrEmpty(c.lastCar) ? "-" : c.lastCar, $"day {c.lastDay}" },
                    keys = new IComparable[] { c.name, c.persona, c.visits, c.purchases, c.spent, c.satisfaction, c.lastCar ?? "", c.lastDay },
                }).ToList());
            }
            else
            {
                UIFactory.ClearChildren(reviewList);
                foreach (var r in st.reviews)
                {
                    var row = UIFactory.Panel(reviewList, "Review", UIFactory.CardBg, 1.8f);
                    UIFactory.LE(row, h: 70);
                    var v = UIFactory.Vertical(row, 12, 2f);
                    v.padding = new RectOffset(16, 16, 8, 8);
                    var c = r.stars >= 4 ? UIFactory.Good : r.stars == 3 ? UIFactory.Gold : UIFactory.Bad;
                    UIFactory.Text(row.transform, $"<color={UIFactory.Hex(c)}>{new string('*', r.stars)}</color>  <b>{r.name}</b>  -  {r.car}  -  day {r.day}", 16, UIFactory.TextMain, TextAlignmentOptions.Left);
                    UIFactory.Text(row.transform, r.text, 15, UIFactory.TextDim, TextAlignmentOptions.Left);
                }
                if (reviewList.childCount == 0) UIFactory.EmptyState(reviewList, "No reviews yet", "Every sale earns a review. Honest, fair deals earn good ones.");
            }
        }
    }
}
