using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class MarketTests
    {
        [Test]
        public void Index_StaysInBounds_AndRevertsToOne()
        {
            var s = new MarketState();
            var rng = new Random(1);
            for (int d = 1; d <= 400; d++) MarketModel.Step(s, new[] { "a", "b" }, d, rng);
            foreach (var m in s.models) Assert.That(m.index, Is.InRange(MarketModel.MinIndex, MarketModel.MaxIndex));
            Assert.That(s.history.Average(), Is.InRange(0.95f, 1.05f));
            Assert.AreEqual(60, s.history.Count);
        }

        [Test]
        public void Seasons_And_Demand()
        {
            Assert.Greater(MarketModel.Season(MarketCategory.Sports, 3), MarketModel.Season(MarketCategory.Sports, 1));
            Assert.Greater(MarketModel.Season(MarketCategory.SUV, 1), MarketModel.Season(MarketCategory.SUV, 3));
            Assert.AreEqual(1f, MarketModel.Season(MarketCategory.Sedan, 2));
            var s = new MarketState();
            MarketModel.RegisterSale(s, MarketCategory.SUV);
            Assert.Less(s.demand[(int)MarketCategory.SUV], 1f);
            MarketModel.Step(s, new string[0], 2, new Random(1));
            Assert.Greater(s.demand[(int)MarketCategory.SUV], 0.98f);
            Assert.Greater(MarketModel.Supply(0), MarketModel.Supply(8));
        }

        [Test]
        public void Competitors_PriceAboveMarket_AndBatchesAreDiscounted()
        {
            var r = new Random(2);
            var lux = MarketModel.Competitors.First(c => c.Name == "Luxury Lane");
            Assert.Greater(MarketModel.CompetitorPrice(lux, 20000, r), 21000);
            Assert.AreEqual(0.15f, MarketModel.BatchDiscount(0), 1e-5);
            Assert.AreEqual(0.3f, MarketModel.BatchDiscount(20), 1e-5);
        }
    }

    public class BuyerTests
    {
        static CarFacts Car(int price = 10000, int value = 10000, float cond = 75f, MarketCategory cat = MarketCategory.Sedan) => new CarFacts
        { price = price, marketValue = value, condition = cond, cleanliness = 80f, mileage = 60000, ageYears = 6, category = cat, fullHistory = true, motValid = true, v5cPresent = true };

        [Test]
        public void NinePersonas_WeightsSensible()
        {
            Assert.AreEqual(9, Buyers.All.Length);
            foreach (var p in Buyers.All)
            {
                float w = p.WPrice + p.WCondition + p.WMileage + p.WAge + p.WHistory + p.WEconomy + p.WDocs;
                Assert.That(w, Is.InRange(0.95f, 1.05f), p.Name);
                Assert.AreEqual(6, p.CategoryLike.Length, p.Name);
            }
        }

        [Test]
        public void Score_PrefersCheaper_BetterCars_AndRespectsBudget()
        {
            var p = Buyers.Get(PersonaId.FamilyBuyer);
            Assert.Greater(Buyers.Score(p, Car(9000), 20000), Buyers.Score(p, Car(12000), 20000));
            Assert.Greater(Buyers.Score(p, Car(cond: 90f), 20000), Buyers.Score(p, Car(cond: 45f), 20000));
            Assert.Greater(Buyers.Score(p, Car(cat: MarketCategory.SUV), 20000), Buyers.Score(p, Car(cat: MarketCategory.Sports), 20000));
            Assert.AreEqual(0f, Buyers.Score(p, Car(30000), 20000));
            var f = Car(); f.knownFaults = 3;
            Assert.Less(Buyers.Score(p, f, 20000), Buyers.Score(p, Car(), 20000));
        }

        [Test]
        public void Hagglers_PayLess_FinanceStretchesBudget()
        {
            int haggler = Buyers.MaxPrice(Buyers.Get(PersonaId.SavvyHaggler), 10000, 0.6f, 50000, false);
            int first = Buyers.MaxPrice(Buyers.Get(PersonaId.FirstTimeBuyer), 10000, 0.6f, 50000, false);
            Assert.Less(haggler, first);
            Assert.AreEqual(10500, Buyers.MaxPrice(Buyers.Get(PersonaId.FirstTimeBuyer), 30000, 0.8f, 10000, false));
            Assert.AreEqual(13000, Buyers.MaxPrice(Buyers.Get(PersonaId.FirstTimeBuyer), 30000, 0.8f, 10000, true));
            Assert.Less(Buyers.OpeningOffer(Buyers.Get(PersonaId.Trader), 10000), Buyers.OpeningOffer(Buyers.Get(PersonaId.Impatient), 10000));
        }

        [Test]
        public void Finance_And_Commission()
        {
            Assert.Greater(Buyers.FinanceApproval(800, 0.2f), Buyers.FinanceApproval(500, 0.2f));
            Assert.Greater(Buyers.FinanceApproval(600, 0.4f), Buyers.FinanceApproval(600, 0f));
            Assert.AreEqual(300, Buyers.FinanceCommission(10000));
            Assert.AreEqual(1500, Buyers.FinanceCommission(100000));
        }

        [Test]
        public void Reviews_PunishHiddenFaults()
        {
            var p = Buyers.Get(PersonaId.FamilyBuyer);
            int honest = Buyers.ReviewStars(p, 80f, 1f, 1, 0, 2);
            int hidden = Buyers.ReviewStars(p, 80f, 1f, 0, 2, 2);
            Assert.Greater(honest, hidden);
            Assert.That(Buyers.ReviewStars(p, 0f, 3f, 0, 9, 20), Is.EqualTo(1));
            Assert.Greater(Buyers.ReputationFromStars(5), 0f);
            Assert.Less(Buyers.ReputationFromStars(1), 0f);
        }
    }

    public class StaffTests
    {
        [Test]
        public void Wages_RiseWithSkill_AndCandidatesVary()
        {
            Assert.Greater(StaffRules.Wage(StaffRole.Mechanic, 80), StaffRules.Wage(StaffRole.Mechanic, 30));
            var c = StaffRules.Candidates(3, new Random(4));
            Assert.That(c.Count, Is.InRange(3, 6));
            foreach (var e in c) { Assert.AreEqual(StaffRules.Wage(e.Role, e.skill), e.wage); Assert.That(e.skill, Is.InRange(25, 90)); }
        }

        [Test]
        public void Salesperson_NeverSellsBelowTheFloor()
        {
            Assert.AreEqual(0, StaffRules.SalespersonDeal(8000, 9000, 9500, 90));
            int good = StaffRules.SalespersonDeal(8000, 10000, 8500, 90);
            int poor = StaffRules.SalespersonDeal(8000, 10000, 8500, 10);
            Assert.Greater(good, poor);
            Assert.GreaterOrEqual(poor, 8500);
            Assert.LessOrEqual(good, 10000);
        }

        [Test]
        public void Experience_RaisesSkill_MoraleFollowsPay()
        {
            var e = new Employee { role = (int)StaffRole.Detailer, skill = 50, wage = StaffRules.Wage(StaffRole.Detailer, 50), morale = 0.5f };
            StaffRules.GainExperience(e, 2.5f);
            Assert.AreEqual(52, e.skill);
            for (int i = 0; i < 20; i++) StaffRules.UpdateMorale(e);
            Assert.AreEqual(0.7f, e.morale, 0.02f);
            e.wage *= 2;
            for (int i = 0; i < 20; i++) StaffRules.UpdateMorale(e);
            Assert.Greater(e.morale, 0.9f);
            Assert.Greater(StaffRules.SickChance(new Employee { morale = 0.2f }), StaffRules.SickChance(new Employee { morale = 0.9f }));
        }
    }
}

namespace CarSales.Tests
{
    using System;
    using System.Linq;
    using CarSales.Sim;
    using NUnit.Framework;

    public class SourcingTests
    {
        [Test]
        public void PrivateSellers_AskUnderMarket_AndKeepAFloor()
        {
            var r = new Random(1);
            for (int i = 0; i < 50; i++)
            {
                int ask = PrivateSellers.Asking(10000, r);
                Assert.That(ask, Is.InRange(7500, 9000));
                int min = PrivateSellers.MinPrice(ask, r);
                Assert.Less(min, ask);
                var (ok, counter) = PrivateSellers.Haggle(min - 500, ask, min, r);
                Assert.IsFalse(ok);
                Assert.GreaterOrEqual(counter, min);
            }
            Assert.IsTrue(PrivateSellers.Haggle(9000, 9000, 8000, r).accepted);
        }

        [Test]
        public void HonestSellers_TellTheTruth_LiarsDont()
        {
            int lies = 0;
            for (int i = 0; i < 100; i++) if (PrivateSellers.Answer(SellerQuestion.AnyProblems, true, "clutch", 0f, new Random(i)).lie) lies++;
            Assert.AreEqual(100, lies);
            Assert.IsFalse(PrivateSellers.Answer(SellerQuestion.AnyProblems, true, "clutch", 1f, new Random(3)).lie);
            Assert.IsFalse(PrivateSellers.Answer(SellerQuestion.AnyProblems, false, "", 0f, new Random(3)).lie, "no bad news, no lie");
        }

        [Test]
        public void ListingQuality_RewardsPhotosAndFairPrices()
        {
            float bare = Listings.Quality(0, 0, false, 1.2f, 0f);
            float good = Listings.Quality(6, 3, true, 0.95f, 4.5f);
            Assert.Less(bare, 0.2f);
            Assert.Greater(good, 0.7f);
            Assert.Greater(Listings.LeadsPerDay(good, true), Listings.LeadsPerDay(good, false));
            Assert.AreEqual("2015 Focus - 80k km - FSH - 1 owner", Listings.Headline("Focus", 2015, 80500, new[] { 0, 3 }));
        }

        [Test]
        public void Questions_LyingDefersThePain()
        {
            Assert.IsTrue(BuyerQuestions.Effect(false, true).lie);
            Assert.Less(BuyerQuestions.Effect(true, true).priceFactor, 1f);
            Assert.Greater(BuyerQuestions.Effect(true, false).patience, BuyerQuestions.Effect(false, false).patience);
        }

        [Test]
        public void Warranty_And_Paperwork()
        {
            Assert.AreEqual(150, AfterSale.WarrantyPrice(2000));
            Assert.AreEqual(400, AfterSale.WarrantyPrice(10000));
            Assert.AreEqual(900, AfterSale.WarrantyPrice(80000));
            Assert.AreEqual(0f, AfterSale.ClaimChancePerDay(new float[0]));
            Assert.Greater(AfterSale.ClaimChancePerDay(new[] { 0.8f, 0.5f }), 0.02f);
            Assert.AreEqual(1f, AfterSale.Paperwork(true, true, 2, true), 1e-5);
            Assert.Greater(AfterSale.ReviewBonus(1f), 0f);
            Assert.Less(AfterSale.ReviewBonus(0.2f), 0f);
        }

        [Test]
        public void NewRoles_HaveWages()
        {
            Assert.Greater(StaffRules.Wage(StaffRole.Manager, 50), StaffRules.Wage(StaffRole.Receptionist, 50));
            Assert.AreEqual("Receptionist", StaffRules.RoleName(StaffRole.Receptionist));
            var e = new Employee { role = (int)StaffRole.Detailer, skill = 50, wage = StaffRules.Wage(StaffRole.Detailer, 50), morale = 0.7f };
            for (int i = 0; i < 20; i++) StaffRules.UpdateMorale(e, manager: true);
            Assert.AreEqual(0.8f, e.morale, 0.02f);
        }
    }
}
