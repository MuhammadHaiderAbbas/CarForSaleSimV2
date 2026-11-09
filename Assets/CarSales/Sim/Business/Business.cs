using System;
using System.Collections.Generic;
using System.Linq;

namespace CarSales.Sim
{
    /// <summary>One car sold (for accounts, VAT and analytics).</summary>
    [Serializable]
    public class SaleRecord
    {
        public int day;
        public string carName;
        public int category;           // MarketCategory
        public int persona = -1;       // PersonaId, -1 = wholesale / unknown
        public int price, cost;        // cost = purchase + repairs
        public int daysInStock;
        public bool viaLead;
        public string soldBy = "";
        public int Profit => price - cost;
    }

    [Serializable]
    public class VatReturn
    {
        public int quarter;
        public int outputMargin, outputStandard, input;
        public int dueDay;
        public bool paid;
        public int fine;
        public int Due => outputMargin + outputStandard - input;
    }

    public enum InsuranceTier { None = 0, Basic = 1, Comprehensive = 2 }

    public sealed class Campaign
    {
        public string Id, Name;
        public int CostPerDay;
        public float Footfall;     // +share of customers
        public float Leads;        // +share of online enquiries
        public PersonaId? Attracts;
        public string Description;
    }

    /// <summary>Business rules (spec §6.12). Pure; tests in BusinessTests.</summary>
    public static class BusinessRules
    {
        // ------------------------------------------------------------------ VAT (UK margin scheme)
        public const float VatRate = 0.2f;

        /// <summary>Margin scheme: VAT is 1/6 of the profit on a used car (never negative).</summary>
        public static int MarginVat(int salePrice, int purchasePrice) => (int)Math.Round(Math.Max(0, salePrice - purchasePrice) / 6.0);

        /// <summary>VAT inside a VAT-inclusive amount (1/6 at 20%).</summary>
        public static int VatInside(int gross) => (int)Math.Round(gross / 6.0);

        /// <summary>
        /// A quarter's return: margin VAT on every car sold + VAT inside warranty sales, minus the VAT you paid on
        /// parts, labour and auction fees (input VAT). Negative = HMRC pays you back.
        /// </summary>
        public static VatReturn Return(int quarter, IEnumerable<SaleRecord> sales, int warrantySales, int inputVatPaid, int dueDay)
        {
            var r = new VatReturn { quarter = quarter, dueDay = dueDay, input = inputVatPaid, outputStandard = VatInside(warrantySales) };
            foreach (var s in sales) r.outputMargin += MarginVat(s.price, s.cost);
            return r;
        }

        public static int Vat(int net) => (int)Math.Round(net * VatRate);
        public const int VatPaymentDays = 30, VatLateFine = 200;
        public static int VatFine(int due) => VatLateFine + (int)Math.Round(Math.Max(0, due) * 0.05);

        // ------------------------------------------------------------------ CREDIT & OVERDRAFT
        /// <summary>Credit rating 0-100 from reputation, days spent overdrawn recently and net worth.</summary>
        public static int CreditRating(float reputation, int daysOverdrawnLast30, int netWorth) =>
            Math.Max(0, Math.Min(100, (int)(reputation * 0.6f + Math.Min(30f, netWorth / 10000f) - daysOverdrawnLast30 * 1.5f + 10f)));

        /// <summary>Overdraft you may use: $100 per rating point (max $10,000).</summary>
        public static int OverdraftLimit(int rating) => Math.Min(10000, rating * 100);

        public const float OverdraftDailyRate = 0.0006f;   // about 22% a year
        public static int OverdraftInterest(int balance) => balance >= 0 ? 0 : (int)Math.Ceiling(-(long)balance * 6 / 10000.0);   // 0.06% a day, exact

        // ------------------------------------------------------------------ INSURANCE
        public static int WeeklyPremium(InsuranceTier t, int stockValue, int claimsLastYear, bool security)
        {
            if (t == InsuranceTier.None) return 0;
            float rate = t == InsuranceTier.Basic ? 0.0012f : 0.0022f;
            float p = 40f + stockValue * rate;
            p *= 1f + Math.Min(5, claimsLastYear) * 0.15f;
            if (security) p *= 0.85f;
            return (int)Math.Round(p / 5f) * 5;
        }

        public static int Excess(InsuranceTier t) => t == InsuranceTier.Comprehensive ? 150 : t == InsuranceTier.Basic ? 400 : int.MaxValue;

        /// <summary>What the insurer pays: basic covers theft/vandalism of stock, comprehensive also forecourt damage.</summary>
        public static int Payout(InsuranceTier t, int loss, bool forecourtDamage)
        {
            if (t == InsuranceTier.None || (forecourtDamage && t != InsuranceTier.Comprehensive)) return 0;
            return Math.Max(0, loss - Excess(t));
        }

        /// <summary>Security (CCTV + alarm) cuts the chance of theft and vandalism by 60%.</summary>
        public static float TheftFactor(bool security) => security ? 0.4f : 1f;
        public const int SecurityWeekly = 90;

        // ------------------------------------------------------------------ RUNNING COSTS
        /// <summary>Utilities per day for a site with this many parking spots (electricity, water, heating).</summary>
        public static int Utilities(int spots) => 25 + spots * 3;

        /// <summary>Business rates per week by site size.</summary>
        public static int BusinessRates(int spots) => 60 + spots * 10;

        // ------------------------------------------------------------------ MARKETING
        public static readonly Campaign[] Campaigns =
        {
            new Campaign { Id = "paper", Name = "Local paper", CostPerDay = 80, Footfall = 0.15f, Leads = 0f, Attracts = PersonaId.FamilyBuyer, Description = "Older and family buyers read it." },
            new Campaign { Id = "online", Name = "Online ads", CostPerDay = 120, Footfall = 0.1f, Leads = 0.5f, Attracts = PersonaId.Commuter, Description = "More enquiries for your online listings." },
            new Campaign { Id = "social", Name = "Social media", CostPerDay = 60, Footfall = 0.12f, Leads = 0.2f, Attracts = PersonaId.FirstTimeBuyer, Description = "Younger buyers, first cars." },
            new Campaign { Id = "signs", Name = "Signs & flags", CostPerDay = 30, Footfall = 0.08f, Leads = 0f, Attracts = null, Description = "Passers-by notice you." },
            new Campaign { Id = "radio", Name = "Local radio", CostPerDay = 220, Footfall = 0.25f, Leads = 0.1f, Attracts = PersonaId.LuxuryBuyer, Description = "Reaches buyers with money." },
        };

        public static Campaign CampaignById(string id) => Array.Find(Campaigns, c => c.Id == id);

        /// <summary>Running campaigns add up with diminishing returns: 1 - Π(1 - boost).</summary>
        public static float Combined(IEnumerable<float> boosts)
        {
            float keep = 1f;
            foreach (var b in boosts) keep *= 1f - Math.Max(0f, Math.Min(0.9f, b));
            return 1f - keep;
        }

        /// <summary>Footfall from review stars: 3 stars = normal, 5 stars +20%, 1 star -30%.</summary>
        public static float StarsFootfall(float avgStars, int reviews) => reviews < 3 ? 1f : 1f + Math.Max(-0.3f, Math.Min(0.2f, (avgStars - 3f) * 0.1f));

        // ------------------------------------------------------------------ DEALERSHIP LEVELS
        public struct LevelInputs
        {
            public int carsSold, staff, dealerships, workshopJobs, netWorth;
            public float reputation;
        }

        public static readonly string[] LevelNames = { "", "Small forecourt", "Small showroom", "Medium dealership", "Large with workshop", "Premium dealer", "Dealer group" };

        /// <summary>The level reached (1-6) and what the next one needs.</summary>
        public static (int level, string next) Level(LevelInputs i)
        {
            if (!(i.carsSold >= 10 && i.reputation >= 55f)) return (1, "Sell 10 cars and reach reputation 55");
            if (!(i.carsSold >= 30 && i.reputation >= 60f && i.staff >= 1)) return (2, "Sell 30 cars, reputation 60, hire someone");
            if (!(i.carsSold >= 60 && i.workshopJobs >= 5)) return (3, "Sell 60 cars and finish 5 workshop jobs");
            if (!(i.carsSold >= 120 && i.reputation >= 75f && i.netWorth >= 250000)) return (4, "Sell 120 cars, reputation 75, net worth $250,000");
            if (!(i.dealerships >= 2)) return (5, "Own a second dealership");
            return (6, "Top level reached");
        }

        public static int MaxStaff(int level) => level <= 1 ? 2 : level == 2 ? 4 : level == 3 ? 6 : level == 4 ? 9 : level == 5 ? 12 : 20;

        /// <summary>Luxury buyers only visit premium dealers (level 5+) or the Luxury Lane site.</summary>
        public static bool LuxuryBuyers(int level, bool premiumSite) => level >= 5 || premiumSite;

        // ------------------------------------------------------------------ PROPERTY DEVELOPMENT
        public const int MaxDevelopment = 3;
        /// <summary>Developing an investment property (refurbish, extend): 40% of its price per level, +30% income per level.</summary>
        public static int DevelopmentCost(int price, int currentLevel) => (int)Math.Round(price * 0.4 * (currentLevel + 1) / 100.0) * 100;
        public static float IncomeFactor(int level) => 1f + 0.3f * Math.Max(0, Math.Min(MaxDevelopment, level));

        // ------------------------------------------------------------------ ANALYTICS
        public sealed class Analytics
        {
            public int sales, revenue, profit;
            public float avgMarginPct, avgDaysToSell, leadConversion;
            public Dictionary<int, int> byPersona = new Dictionary<int, int>();
            public Dictionary<int, int> byCategory = new Dictionary<int, int>();
        }

        public static Analytics Analyse(IList<SaleRecord> sales, int leads)
        {
            var a = new Analytics { sales = sales.Count };
            if (sales.Count == 0) return a;
            a.revenue = sales.Sum(s => s.price);
            a.profit = sales.Sum(s => s.Profit);
            a.avgMarginPct = (float)sales.Average(s => s.cost > 0 ? (s.price - s.cost) / (float)s.cost * 100f : 0f);
            a.avgDaysToSell = (float)sales.Average(s => s.daysInStock);
            a.leadConversion = leads > 0 ? Math.Min(1f, sales.Count(s => s.viaLead) / (float)leads) : 0f;
            foreach (var s in sales)
            {
                a.byPersona[s.persona] = a.byPersona.TryGetValue(s.persona, out var p) ? p + 1 : 1;
                a.byCategory[s.category] = a.byCategory.TryGetValue(s.category, out var c) ? c + 1 : 1;
            }
            return a;
        }
    }
}
