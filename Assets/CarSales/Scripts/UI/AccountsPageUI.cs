using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Accounts app: profit &amp; loss for a period (today / 7 / 30 days / all kept), daily-profit chart,
    /// totals per category and a sortable transaction table, all read from the ledger.
    /// </summary>
    public class AccountsPageUI : ComputerPage
    {
        static readonly int[] PeriodDays = { 1, 7, 30, 0 };
        UIFactory.TabBar periods, views;
        UIFactory.StatTileRefs income, expenses, net, cash;
        UIFactory.MiniChart chart;
        UIFactory.Table categories, transactions;
        RectTransform categoryView, transactionView, cashView, carsView, stockView, vatView, cashList, vatList;
        UIFactory.Table perCar, stock;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 12f);
            periods = UIFactory.Tabs(Area, new[] { "Today", "7 days", "30 days", "All kept" }, _ => Refresh(), 40f, 130f, 16f);

            var tiles = UIFactory.HRow(Area, 104, 12, "Tiles");
            income = UIFactory.StatTile(tiles, "Income", 104f);
            expenses = UIFactory.StatTile(tiles, "Expenses", 104f);
            net = UIFactory.StatTile(tiles, "Profit", 104f);
            cash = UIFactory.StatTile(tiles, "Cash now", 104f);

            chart = UIFactory.Chart(Area, "Profit per day", 90f);

            views = UIFactory.Tabs(Area, new[] { "By category", "Transactions", "Cash flow", "Per car", "Stock", "VAT" }, _ => { ShowView(); Refresh(); }, 40f, 160f, 15f);

            var body = UIFactory.Rect("Views", Area);
            UIFactory.LE(body, flexH: 1, flexW: 1);
            categoryView = UIFactory.Rect("Categories", body);
            UIFactory.Stretch(categoryView);
            UIFactory.Vertical(categoryView, 0, 4f);
            categories = UIFactory.MakeTable(categoryView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Category"),
                new UIFactory.TableColumn("Money in", 200f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Money out", 200f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Net", 200f, TextAlignmentOptions.Right),
            }, 38f);
            categories.SetSort(3, false);

            transactionView = UIFactory.Rect("Transactions", body);
            UIFactory.Stretch(transactionView);
            UIFactory.Vertical(transactionView, 0, 4f);
            transactions = UIFactory.MakeTable(transactionView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("When", 150f),
                new UIFactory.TableColumn("Category", 220f),
                new UIFactory.TableColumn("Details"),
                new UIFactory.TableColumn("Amount", 170f, TextAlignmentOptions.Right),
            }, 36f);
            transactions.SetSort(0, true);
            transactions.SetEmptyText("No transactions in this period.");

            // Phase 9: cash flow, profit per car, stock and ageing, VAT
            cashView = View(body); cashList = UIFactory.ScrollList(cashView, 6f);
            carsView = View(body);
            perCar = UIFactory.MakeTable(carsView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Car"), new UIFactory.TableColumn("Sold", 90f), new UIFactory.TableColumn("Price", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Cost", 130f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Profit", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Margin", 100f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Days in stock", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Sold by", 140f),
            }, 34f);
            perCar.SetSort(1, true);
            perCar.SetEmptyText("No cars sold in this period.");
            stockView = View(body);
            stock = UIFactory.MakeTable(stockView, new List<UIFactory.TableColumn>
            {
                new UIFactory.TableColumn("Car"), new UIFactory.TableColumn("Days in stock", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("Cost", 130f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Value now", 130f, TextAlignmentOptions.Right),
                new UIFactory.TableColumn("If sold now", 140f, TextAlignmentOptions.Right), new UIFactory.TableColumn("Status", 150f),
            }, 34f);
            stock.SetSort(1, true);
            stock.SetEmptyText("No cars in stock.");
            vatView = View(body); vatList = UIFactory.ScrollList(vatView, 8f);

            periods.Select(1, false);
            views.Select(0, false);
            ShowView();
        }

        static RectTransform View(Transform host)
        {
            var v = UIFactory.Rect("View", host);
            UIFactory.Stretch(v);
            UIFactory.Vertical(v, 0, 4f);
            return v;
        }

        void ShowView()
        {
            categoryView.gameObject.SetActive(views.Index == 0);
            transactionView.gameObject.SetActive(views.Index == 1);
            cashView.gameObject.SetActive(views.Index == 2);
            carsView.gameObject.SetActive(views.Index == 3);
            stockView.gameObject.SetActive(views.Index == 4);
            vatView.gameObject.SetActive(views.Index == 5);
        }

        void Line(Transform parent, string label, string value, bool bold = false)
        {
            var v = UIFactory.SpecRow(parent, label, 32f);
            v.text = bold ? $"<b>{value}</b>" : value;
        }

        void RenderExtras(int from, int today, Foundation.LedgerSummary s)
        {
            var eco = CarSalesServices.Economy;
            var ledger = eco.Ledger;
            var biz = CarSalesServices.Business;
            if (views.Index == 2)
            {
                UIFactory.ClearChildren(cashList);
                long before = ledger.OpeningBalance + ledger.Entries.Where(e => e.day < from).Sum(e => (long)e.amount);
                long after = before + ledger.Entries.Where(e => e.day >= from && e.day <= today).Sum(e => (long)e.amount);
                UIFactory.SectionTitle(cashList, $"Cash flow, days {from}-{today}");
                Line(cashList, "Cash at the start", UIFactory.Money((int)before));
                Line(cashList, "Money in from trading", Coloured(s.income));
                Line(cashList, "Money out on trading", Coloured(-s.expenses));
                Line(cashList, "Operating cash flow", Coloured(s.Net), true);
                Line(cashList, "Loans taken", Coloured(s.financingIn));
                Line(cashList, "Loans repaid", Coloured(-s.financingOut));
                Line(cashList, "Cash at the end", UIFactory.Money((int)after), true);
                UIFactory.SectionTitle(cashList, "Check");
                bool ok = after == eco.Money || today < CarRecords.Today;
                long fromLedger = ledger.OpeningBalance + ledger.Entries.Sum(e => (long)e.amount);
                Line(cashList, "Cash = opening balance + every transaction", fromLedger == eco.Money
                    ? $"<color={UIFactory.Hex(UIFactory.Good)}>Reconciled: {UIFactory.Money((int)fromLedger)}</color>"
                    : $"<color={UIFactory.Hex(UIFactory.Bad)}>Off by {UIFactory.Money((int)(eco.Money - fromLedger))}</color>");
                if (biz)
                {
                    Line(cashList, "Credit rating", $"{biz.CreditRating}/100");
                    Line(cashList, "Overdraft available", $"{UIFactory.Money(biz.OverdraftLimit)} (0.06% a day interest when you're below zero)");
                }
            }
            else if (views.Index == 3 && biz)
            {
                perCar.SetRows(biz.State.sales.Where(x => x.day >= from && x.day <= today).Select(x => new UIFactory.Table.Row
                {
                    cells = new[] { x.carName, $"day {x.day}", UIFactory.Money(x.price), UIFactory.Money(x.cost), Coloured(x.Profit),
                                    x.cost > 0 ? $"{(x.price - x.cost) / (float)x.cost * 100f:0}%" : "-", x.daysInStock.ToString(), string.IsNullOrEmpty(x.soldBy) ? (x.persona < 0 ? "wholesale" : "you") : x.soldBy },
                    keys = new IComparable[] { x.carName, x.day, x.price, x.cost, x.Profit, x.cost > 0 ? (x.price - x.cost) / (float)x.cost : 0f, x.daysInStock, x.soldBy ?? "" },
                }).ToList());
            }
            else if (views.Index == 4)
            {
                var rows = new List<UIFactory.Table.Row>();
                int totalValue = 0, totalCost = 0;
                foreach (var c in CarSalesServices.Inventory.OwnedCars.Where(x => !x.personal))
                {
                    var buy = ledger.ForRef(c.instanceId).FirstOrDefault(e => e.category == TxCategory.VehiclePurchase);
                    int days = buy != null ? today - buy.day : 0;
                    int cost = c.purchasePrice + c.repairSpent;
                    int value = CarValueCalculator.MarketValue(c);
                    totalValue += value; totalCost += cost;
                    rows.Add(new UIFactory.Table.Row
                    {
                        cells = new[] { c.Name, days >= 30 ? $"<color={UIFactory.Hex(UIFactory.Bad)}>{days}</color>" : days >= 14 ? $"<color={UIFactory.Hex(UIFactory.Warn)}>{days}</color>" : days.ToString(),
                                        UIFactory.Money(cost), UIFactory.Money(value), Coloured(value - cost), c.state.ToString() },
                        keys = new IComparable[] { c.Name, days, cost, value, value - cost, c.state.ToString() },
                    });
                }
                stock.SetRows(rows);
                if (stock.footer) stock.footer.text = $"{rows.Count} cars  -  stock cost {UIFactory.Money(totalCost)}  -  stock value {UIFactory.Money(totalValue)}  -  cars over 30 days old cost you money every day";
            }
            else if (views.Index == 5 && biz)
            {
                UIFactory.ClearChildren(vatList);
                int q = GameCalendar.QuarterOf(CarRecords.Today);
                var est = biz.Estimate(q);
                UIFactory.SectionTitle(vatList, $"This quarter (Q{q}) so far - margin scheme");
                Line(vatList, "VAT on your margins (1/6 of each car's profit)", UIFactory.Money(est.outputMargin));
                Line(vatList, "VAT inside warranty sales", UIFactory.Money(est.outputStandard));
                Line(vatList, "VAT you paid on parts, labour, auction fees", "-" + UIFactory.Money(est.input));
                Line(vatList, "Due at the end of the quarter (estimate)", UIFactory.Money(est.Due), true);
                UIFactory.SectionTitle(vatList, "Returns");
                foreach (var r in biz.State.vatReturns.OrderByDescending(x => x.quarter))
                {
                    var row = UIFactory.HRow(vatList, 44, 10, "Return", false);
                    var t = UIFactory.Text(row, $"Q{r.quarter}: {UIFactory.Money(r.Due)}" + (r.fine > 0 ? $" + {UIFactory.Money(r.fine)} fine" : "") +
                                                $"  -  {(r.paid ? "paid" : CarRecords.Today > r.dueDay ? "OVERDUE" : $"due day {r.dueDay}")}", 16, r.paid ? UIFactory.TextMain : UIFactory.Warn, TextAlignmentOptions.Left);
                    UIFactory.LE(t, flexW: 1);
                    if (!r.paid)
                    {
                        var rr = r;
                        var b = UIFactory.Btn(row, $"Pay {UIFactory.Money(r.Due + r.fine)}", UIFactory.Good, () => { biz.PayVat(rr); Refresh(); }, 40f, 14f);
                        UIFactory.LE(b, h: 40, w: 160, flexW: 0);
                    }
                }
                if (biz.State.vatReturns.Count == 0) UIFactory.EmptyState(vatList, "No returns yet", "A return is made at the end of every quarter (13 weeks).");
            }
        }

        public override void Refresh()
        {
            var eco = CarSalesServices.Economy;
            var day = CarSalesServices.DayCycle;
            if (!eco || !day) return;
            var ledger = eco.Ledger;
            int today = day.CurrentDay;
            int span = PeriodDays[Mathf.Clamp(periods.Index, 0, PeriodDays.Length - 1)];
            int from = span > 0 ? Mathf.Max(1, today - span + 1) : 1;
            var s = ledger.Summarize(from, today);

            income.value.text = UIFactory.Money(s.income);
            income.value.color = UIFactory.Good;
            expenses.value.text = UIFactory.Money(s.expenses);
            expenses.value.color = UIFactory.Bad;
            net.value.text = UIFactory.Money(s.Net);
            net.value.color = s.Net >= 0 ? UIFactory.Good : UIFactory.Bad;
            net.sub.text = s.Net >= 0 ? "profit" : "loss";
            cash.value.text = UIFactory.Money(eco.Money);
            int loan = CarSalesServices.Bank ? CarSalesServices.Bank.Loan : 0;
            cash.sub.text = loan > 0 ? $"loan {UIFactory.Money(loan)}" : "no loan";
            income.sub.text = span == 1 ? $"day {today}" : $"days {from}-{today}";
            expenses.sub.text = s.financingIn + s.financingOut > 0 ? $"loans: +{UIFactory.Money(s.financingIn)} / -{UIFactory.Money(s.financingOut)}" : "loans excluded";

            int chartFrom = Mathf.Max(1, today - 13);
            var daily = ledger.DailyNet(chartFrom, today);
            var values = new List<float>();
            foreach (var v in daily) values.Add(v);
            chart.Set(values);
            chart.caption.text = $"PROFIT PER DAY  (days {chartFrom}-{today})";

            // Category totals: split in / out for the period.
            var inBy = new int[TxCategories.Count];
            var outBy = new int[TxCategories.Count];
            var rows = new List<UIFactory.Table.Row>();
            foreach (var e in ledger.Entries)
            {
                if (e.day < from || e.day > today) continue;
                int c = (int)e.category;
                if (c < 0 || c >= inBy.Length) continue;
                if (e.amount > 0) inBy[c] += e.amount; else outBy[c] -= e.amount;

                rows.Add(new UIFactory.Table.Row
                {
                    cells = new[]
                    {
                        $"Day {e.day}  {Mathf.FloorToInt(e.hour):00}:{Mathf.FloorToInt((e.hour % 1f) * 60f):00}",
                        TxCategories.DisplayName(e.category),
                        string.IsNullOrEmpty(e.memo) ? "-" : e.memo,
                        Coloured(e.amount),
                    },
                    keys = new IComparable[] { e.id, TxCategories.DisplayName(e.category), e.memo ?? "", e.amount },
                });
            }
            transactions.SetRows(rows);

            var catRows = new List<UIFactory.Table.Row>();
            for (int c = 0; c < inBy.Length; c++)
            {
                if (inBy[c] == 0 && outBy[c] == 0) continue;
                var cat = (TxCategory)c;
                int n = inBy[c] - outBy[c];
                catRows.Add(new UIFactory.Table.Row
                {
                    cells = new[]
                    {
                        TxCategories.DisplayName(cat) + (TxCategories.IsFinancing(cat) ? $"  <color={UIFactory.Hex(UIFactory.TextDim)}>(financing)</color>" : ""),
                        inBy[c] > 0 ? UIFactory.Money(inBy[c]) : "-",
                        outBy[c] > 0 ? UIFactory.Money(outBy[c]) : "-",
                        Coloured(n),
                    },
                    keys = new IComparable[] { TxCategories.DisplayName(cat), inBy[c], outBy[c], n },
                });
            }
            categories.SetEmptyText("No money moved in this period.");
            categories.SetRows(catRows);
            RenderExtras(from, today, s);
        }

        static string Coloured(int amount) =>
            $"<color={UIFactory.Hex(amount >= 0 ? UIFactory.Good : UIFactory.Bad)}>{(amount > 0 ? "+" : "")}{UIFactory.Money(amount)}</color>";
    }
}
