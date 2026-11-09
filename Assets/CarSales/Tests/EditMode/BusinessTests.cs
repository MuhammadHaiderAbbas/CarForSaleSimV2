using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class BusinessTests
    {
        [SetUp] public void SetUp() => EventBus.Clear();

        [Test]
        public void MarginVat_IsOneSixthOfProfit_NeverNegative()
        {
            Assert.AreEqual(500, BusinessRules.MarginVat(9000, 6000));
            Assert.AreEqual(0, BusinessRules.MarginVat(5000, 6000));
            Assert.AreEqual(100, BusinessRules.VatInside(600));
            var r = BusinessRules.Return(1, new[] { new SaleRecord { price = 9000, cost = 6000 }, new SaleRecord { price = 3000, cost = 4000 } }, 600, 150, 121);
            Assert.AreEqual(500, r.outputMargin);
            Assert.AreEqual(100, r.outputStandard);
            Assert.AreEqual(450, r.Due);
            Assert.AreEqual(200 + 22, BusinessRules.VatFine(450));   // 5% of 450 = 22.5, rounded to even
        }

        [Test]
        public void Overdraft_FollowsCreditRating()
        {
            int good = BusinessRules.CreditRating(80f, 0, 200000);
            int bad = BusinessRules.CreditRating(30f, 20, 0);
            Assert.Greater(good, bad);
            Assert.LessOrEqual(BusinessRules.OverdraftLimit(100), 10000);
            Assert.AreEqual(0, BusinessRules.OverdraftInterest(500));
            Assert.AreEqual(3, BusinessRules.OverdraftInterest(-5000));
        }

        [Test]
        public void Insurance_PremiumsExcessAndPayouts()
        {
            Assert.AreEqual(0, BusinessRules.WeeklyPremium(InsuranceTier.None, 100000, 0, false));
            int basic = BusinessRules.WeeklyPremium(InsuranceTier.Basic, 100000, 0, false);
            int comp = BusinessRules.WeeklyPremium(InsuranceTier.Comprehensive, 100000, 0, false);
            Assert.Greater(comp, basic);
            Assert.Greater(BusinessRules.WeeklyPremium(InsuranceTier.Basic, 100000, 3, false), basic);
            Assert.Less(BusinessRules.WeeklyPremium(InsuranceTier.Basic, 100000, 0, true), basic);
            Assert.AreEqual(600, BusinessRules.Payout(InsuranceTier.Basic, 1000, false));
            Assert.AreEqual(0, BusinessRules.Payout(InsuranceTier.Basic, 1000, true), "basic doesn't cover forecourt damage");
            Assert.AreEqual(850, BusinessRules.Payout(InsuranceTier.Comprehensive, 1000, true));
            Assert.AreEqual(0, BusinessRules.Payout(InsuranceTier.None, 1000, false));
        }

        [Test]
        public void Marketing_HasDiminishingReturns()
        {
            Assert.AreEqual(0.25f, BusinessRules.Combined(new[] { 0.25f }), 1e-5);
            float two = BusinessRules.Combined(new[] { 0.25f, 0.25f });
            Assert.Less(two, 0.5f);
            Assert.Greater(two, 0.25f);
            Assert.AreEqual(1f, BusinessRules.StarsFootfall(5f, 1), "too few reviews to matter");
            Assert.AreEqual(1.2f, BusinessRules.StarsFootfall(5f, 10), 1e-5);
            Assert.AreEqual(0.8f, BusinessRules.StarsFootfall(1f, 10), 1e-5);
        }

        [Test]
        public void Levels_UnlockInOrder()
        {
            var i = new BusinessRules.LevelInputs { reputation = 50f };
            Assert.AreEqual(1, BusinessRules.Level(i).level);
            i.carsSold = 35; i.reputation = 62f; i.staff = 1;
            Assert.AreEqual(3, BusinessRules.Level(i).level);
            i.carsSold = 130; i.workshopJobs = 8; i.reputation = 80f; i.netWorth = 300000;
            Assert.AreEqual(5, BusinessRules.Level(i).level);
            i.dealerships = 2;
            Assert.AreEqual(6, BusinessRules.Level(i).level);
            Assert.Greater(BusinessRules.MaxStaff(6), BusinessRules.MaxStaff(1));
            Assert.IsFalse(BusinessRules.LuxuryBuyers(3, false));
            Assert.IsTrue(BusinessRules.LuxuryBuyers(3, true));
        }

        [Test]
        public void Analytics_AndDevelopment()
        {
            var sales = new List<SaleRecord>
            {
                new SaleRecord { price = 11000, cost = 10000, daysInStock = 4, persona = 2, category = 1, viaLead = true },
                new SaleRecord { price = 6000, cost = 5000, daysInStock = 10, persona = 2, category = 0 },
            };
            var a = BusinessRules.Analyse(sales, 4);
            Assert.AreEqual(2000, a.profit);
            Assert.AreEqual(15f, a.avgMarginPct, 1e-3);
            Assert.AreEqual(7f, a.avgDaysToSell, 1e-3);
            Assert.AreEqual(0.25f, a.leadConversion, 1e-5);
            Assert.AreEqual(2, a.byPersona[2]);
            Assert.AreEqual(24000, BusinessRules.DevelopmentCost(60000, 0));
            Assert.AreEqual(1.6f, BusinessRules.IncomeFactor(2), 1e-5);
        }

        /// <summary>
        /// Spec Phase 9 "done when": a 30-day sandbox run with buying, repairs, fees, VAT, wages, insurance, overdraft
        /// interest and loans reconciles exactly: cash = opening + Σ ledger, every day, and after compaction.
        /// </summary>
        [Test]
        public void Sandbox30Days_Reconciles()
        {
            var rng = new Random(42);
            var ledger = new Ledger();
            int opening = 20000;
            ledger.Reset(opening, 1);
            long cash = opening;
            var sales = new List<SaleRecord>();
            int inputVat = 0, warranty = 0;
            void Post(int amount, TxCategory c, int day) { ledger.Post(amount, c, day, 12f, c.ToString()); cash += amount; }
            for (int day = 1; day <= 30; day++)
            {
                int buy = 3000 + rng.Next(8000);
                Post(-buy, TxCategory.VehiclePurchase, day);
                int fees = 250; int vat = BusinessRules.Vat(fees);
                Post(-fees, TxCategory.Auction, day); Post(-vat, TxCategory.Vat, day); inputVat += vat;
                int repairs = rng.Next(800);
                Post(-repairs, TxCategory.Parts, day); inputVat += BusinessRules.VatInside(repairs);
                int sale = (int)(buy * (1.05 + rng.NextDouble() * 0.3));
                Post(sale, TxCategory.VehicleSale, day);
                sales.Add(new SaleRecord { day = day, price = sale, cost = buy + repairs });
                if (day % 3 == 0) { Post(300, TxCategory.Warranty, day); warranty += 300; }
                if (day % 7 == 0) { Post(-1200, TxCategory.Wages, day); Post(-60, TxCategory.Insurance, day); }
                if (cash < 0) Post(-BusinessRules.OverdraftInterest((int)cash), TxCategory.LoanInterest, day);
                if (day == 10) Post(5000, TxCategory.LoanDrawdown, day);
                if (day == 25) Post(-5000, TxCategory.LoanRepayment, day);
                Assert.AreEqual(cash, ledger.Balance, $"day {day}");
            }
            var ret = BusinessRules.Return(1, sales, warranty, inputVat, 60);
            Post(-ret.Due, TxCategory.Vat, 30);
            Assert.AreEqual(cash, ledger.Balance);
            Assert.AreEqual(cash, opening + ledger.Entries.Sum(e => (long)e.amount));
            var s = ledger.Summarize(1, 30);
            Assert.AreEqual(cash - opening, s.income - s.expenses + s.financingIn - s.financingOut);
            ledger.Compact(30, 7);
            Assert.AreEqual(cash, ledger.Balance, "compaction keeps the balance");
            Assert.AreEqual(0, ledger.Reconcile((int)cash, 30, 23f), "nothing to correct");
        }
    }
}
