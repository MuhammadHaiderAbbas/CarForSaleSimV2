using System.Linq;
using CarSales.Foundation;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>Shown when you wake up: yesterday's results.</summary>
    public class DailyReportUI : UIPanel
    {
        TextMeshProUGUI title, subtitle, sold, bought, revenue, expenses, rent, interest, income, profit, balance, rep, worth, warning, breakdown;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Good Morning!", new Vector2(760f, 900f), null);
            UIFactory.Vertical(body, 0, 3f);
            title = UIFactory.Text(body, "", 34, UIFactory.TextMain, TextAlignmentOptions.Center, true);
            UIFactory.LE(title, h: 48);
            subtitle = UIFactory.Text(body, "", 17, UIFactory.TextDim, TextAlignmentOptions.Center);
            UIFactory.LE(subtitle, h: 44);
            sold = UIFactory.SpecRow(body, "Cars sold");
            bought = UIFactory.SpecRow(body, "Cars bought");
            revenue = UIFactory.SpecRow(body, "Revenue");
            expenses = UIFactory.SpecRow(body, "Expenses");
            rent = UIFactory.SpecRow(body, "  incl. rent (dealerships + cars)");
            interest = UIFactory.SpecRow(body, "  incl. loan interest");
            income = UIFactory.SpecRow(body, "Property income");
            profit = UIFactory.SpecRow(body, "Profit on sold cars");
            breakdown = UIFactory.Text(body, "", 15, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            UIFactory.LE(breakdown, h: 72);
            UIFactory.Divider(body);
            balance = UIFactory.SpecRow(body, "Cash balance", 40);
            rep = UIFactory.SpecRow(body, "Reputation");
            worth = UIFactory.SpecRow(body, "Net worth");
            warning = UIFactory.Text(body, "", 17, UIFactory.Bad, TextAlignmentOptions.Center, true);
            UIFactory.Spacer(body);
            UIFactory.Btn(body, "Start the Day", UIFactory.Accent, CloseAll, 60f, 22f);
        }

        public void Show(DailyReport r)
        {
            OpenSelf();
            var eco = CarSalesServices.Economy;
            var p = CarSalesServices.Progress;
            var day = CarSalesServices.DayCycle;
            int h = Mathf.FloorToInt(day.HourOfDay);
            title.text = $"DAY {r.day} REPORT";
            subtitle.text = (r.passedOut ? $"<color={UIFactory.Hex(UIFactory.Warn)}>You passed out last night! Taxi + clinic fee {UIFactory.Money(r.passOutFee)}. You feel groggy.</color>\n"
                                         : "You slept well. ") +
                            $"It's Day {day.CurrentDay}, {h:00}:00. Dealerships open at {day.OpenHour:00}:00.";
            sold.text = r.carsSold.ToString();
            bought.text = r.carsBought.ToString();
            revenue.text = $"<color={UIFactory.Hex(UIFactory.Good)}>{UIFactory.Money(r.revenue)}</color>";
            expenses.text = $"<color={UIFactory.Hex(UIFactory.Bad)}>{UIFactory.Money(r.expenses)}</color>";
            rent.text = UIFactory.Money(r.rent);
            interest.text = UIFactory.Money(r.interest);
            income.text = $"<color={UIFactory.Hex(UIFactory.Good)}>{UIFactory.Money(r.propertyIncome)}</color>";
            profit.text = UIFactory.Money(r.profit);
            balance.text = $"<size=130%><color={UIFactory.Hex(eco.Money < 0 ? UIFactory.Bad : UIFactory.Gold)}>{UIFactory.Money(eco.Money)}</color></size>";
            rep.text = $"{eco.Reputation:0}/100";
            var lines = eco.Ledger.Summarize(r.day, r.day).Lines();
            string Join(bool income) => string.Join("  ·  ", lines.Where(l => (l.Value > 0) == income).Take(5)
                                                               .Select(l => $"{TxCategories.DisplayName(l.Key)} {UIFactory.Money(System.Math.Abs(l.Value))}"));
            string inc = Join(true), outg = Join(false);
            breakdown.text = (inc.Length > 0 ? $"<color={UIFactory.Hex(UIFactory.Good)}>In:</color> {inc}\n" : "") +
                             (outg.Length > 0 ? $"<color={UIFactory.Hex(UIFactory.Bad)}>Out:</color> {outg}" : "") +
                             (inc.Length + outg.Length == 0 ? "No money moved yesterday." : "") +
                             "\n<size=85%>Full details: computer > Bank > Account statement</size>";
            worth.text = p ? UIFactory.Money(p.NetWorth) : "-";
            warning.text = p && p.DaysInDebt > 0
                ? $"IN DEBT: day {p.DaysInDebt}/{p.BankruptcyDays}. Get above $0 or you go bankrupt! (Sell cars, Quick Sell, or take a loan.)"
                : "";
        }
    }
}
