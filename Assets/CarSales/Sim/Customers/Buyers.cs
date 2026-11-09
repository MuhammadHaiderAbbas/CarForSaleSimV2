using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum PersonaId { BudgetHunter = 0, FirstTimeBuyer = 1, FamilyBuyer = 2, Commuter = 3, Enthusiast = 4, LuxuryBuyer = 5, SavvyHaggler = 6, Impatient = 7, Trader = 8 }

    /// <summary>A kind of customer (spec §6.10). Weights add up to about 1.</summary>
    public sealed class BuyerPersona
    {
        public PersonaId Id;
        public string Name;
        public int BudgetMin, BudgetMax;
        public float[] CategoryLike;   // per MarketCategory, 0..1
        public float WPrice, WCondition, WMileage, WAge, WHistory, WEconomy, WDocs;
        public float Knowledge;        // chance to spot a fault you didn't mention
        public float Haggling;         // 0 = pays the asking price, 1 = grinds you down
        public int Patience;           // counter-offers tolerated
        public float TradeInChance, FinanceChance;
        public float Harshness;        // reviews
        public float Weight;           // how common
    }

    /// <summary>What a customer can see about a car.</summary>
    public struct CarFacts
    {
        public int price, marketValue;
        public float condition, cleanliness;
        public int mileage, ageYears;
        public MarketCategory category;
        public bool economical;        // EV / hybrid / small engine
        public bool fullHistory, motValid, v5cPresent;
        public int knownFaults;
    }

    public static class Buyers
    {
        static float[] Cats(float hatch, float sedan, float suv, float sports, float pickup, float lux) => new[] { hatch, sedan, suv, sports, pickup, lux };

        public static readonly BuyerPersona[] All =
        {
            new BuyerPersona { Id = PersonaId.BudgetHunter, Name = "Budget hunter", BudgetMin = 3000, BudgetMax = 12000, CategoryLike = Cats(1f, .8f, .5f, .2f, .5f, .1f),
                WPrice = .45f, WCondition = .15f, WMileage = .1f, WAge = .05f, WHistory = .05f, WEconomy = .15f, WDocs = .05f, Knowledge = .3f, Haggling = .8f, Patience = 4, TradeInChance = .2f, FinanceChance = .2f, Harshness = .5f, Weight = 1.4f },
            new BuyerPersona { Id = PersonaId.FirstTimeBuyer, Name = "First-time buyer", BudgetMin = 4000, BudgetMax = 15000, CategoryLike = Cats(1f, .7f, .4f, .5f, .2f, .2f),
                WPrice = .3f, WCondition = .2f, WMileage = .1f, WAge = .1f, WHistory = .1f, WEconomy = .1f, WDocs = .1f, Knowledge = .1f, Haggling = .3f, Patience = 3, TradeInChance = 0f, FinanceChance = .6f, Harshness = .4f, Weight = 1.1f },
            new BuyerPersona { Id = PersonaId.FamilyBuyer, Name = "Family buyer", BudgetMin = 8000, BudgetMax = 30000, CategoryLike = Cats(.4f, .8f, 1f, .1f, .6f, .4f),
                WPrice = .2f, WCondition = .25f, WMileage = .1f, WAge = .1f, WHistory = .15f, WEconomy = .1f, WDocs = .1f, Knowledge = .3f, Haggling = .5f, Patience = 3, TradeInChance = .5f, FinanceChance = .4f, Harshness = .6f, Weight = 1.2f },
            new BuyerPersona { Id = PersonaId.Commuter, Name = "Commuter", BudgetMin = 6000, BudgetMax = 25000, CategoryLike = Cats(1f, 1f, .4f, .2f, .2f, .3f),
                WPrice = .25f, WCondition = .15f, WMileage = .2f, WAge = .1f, WHistory = .05f, WEconomy = .2f, WDocs = .05f, Knowledge = .3f, Haggling = .4f, Patience = 3, TradeInChance = .4f, FinanceChance = .4f, Harshness = .5f, Weight = 1.1f },
            new BuyerPersona { Id = PersonaId.Enthusiast, Name = "Enthusiast", BudgetMin = 10000, BudgetMax = 60000, CategoryLike = Cats(.3f, .3f, .2f, 1f, .3f, .6f),
                WPrice = .15f, WCondition = .3f, WMileage = .1f, WAge = .05f, WHistory = .2f, WEconomy = 0f, WDocs = .2f, Knowledge = .8f, Haggling = .5f, Patience = 4, TradeInChance = .4f, FinanceChance = .2f, Harshness = .7f, Weight = .7f },
            new BuyerPersona { Id = PersonaId.LuxuryBuyer, Name = "Luxury buyer", BudgetMin = 25000, BudgetMax = 120000, CategoryLike = Cats(.1f, .5f, .7f, .7f, .2f, 1f),
                WPrice = .1f, WCondition = .35f, WMileage = .15f, WAge = .15f, WHistory = .15f, WEconomy = 0f, WDocs = .1f, Knowledge = .4f, Haggling = .3f, Patience = 2, TradeInChance = .6f, FinanceChance = .3f, Harshness = .8f, Weight = .5f },
            new BuyerPersona { Id = PersonaId.SavvyHaggler, Name = "Savvy haggler", BudgetMin = 5000, BudgetMax = 35000, CategoryLike = Cats(.6f, .7f, .6f, .5f, .6f, .4f),
                WPrice = .4f, WCondition = .25f, WMileage = .1f, WAge = .05f, WHistory = .1f, WEconomy = .05f, WDocs = .05f, Knowledge = .9f, Haggling = .95f, Patience = 5, TradeInChance = .3f, FinanceChance = .1f, Harshness = .6f, Weight = .8f },
            new BuyerPersona { Id = PersonaId.Impatient, Name = "Impatient buyer", BudgetMin = 6000, BudgetMax = 40000, CategoryLike = Cats(.6f, .7f, .7f, .6f, .5f, .5f),
                WPrice = .25f, WCondition = .25f, WMileage = .1f, WAge = .1f, WHistory = .1f, WEconomy = .1f, WDocs = .1f, Knowledge = .2f, Haggling = .2f, Patience = 1, TradeInChance = .3f, FinanceChance = .4f, Harshness = .7f, Weight = .8f },
            new BuyerPersona { Id = PersonaId.Trader, Name = "Trader", BudgetMin = 3000, BudgetMax = 40000, CategoryLike = Cats(.7f, .7f, .7f, .7f, .7f, .7f),
                WPrice = .6f, WCondition = .2f, WMileage = .1f, WAge = 0f, WHistory = .05f, WEconomy = 0f, WDocs = .05f, Knowledge = .7f, Haggling = 1f, Patience = 3, TradeInChance = 0f, FinanceChance = 0f, Harshness = .3f, Weight = .5f },
        };

        public static BuyerPersona Get(PersonaId id) => All[(int)id];

        public static BuyerPersona Pick(Random rng, float budgetBias = 1f)
        {
            float total = 0f;
            foreach (var p in All) total += p.Weight * (p.Id == PersonaId.LuxuryBuyer ? budgetBias : 1f);
            double r = rng.NextDouble() * total;
            foreach (var p in All)
            {
                r -= p.Weight * (p.Id == PersonaId.LuxuryBuyer ? budgetBias : 1f);
                if (r <= 0) return p;
            }
            return All[0];
        }

        static float C01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>
        /// §8.4 car score 0-1: weighted price (vs. market value and budget), condition, mileage, age, history,
        /// economy, paperwork; multiplied by how much they like the body type. 0 if over budget by more than 15%.
        /// </summary>
        public static float Score(BuyerPersona p, CarFacts f, int budget)
        {
            if (f.price > budget * 1.15f) return 0f;
            float ratio = f.marketValue > 0 ? f.price / (float)f.marketValue : 1.2f;
            float price = C01(1.4f - ratio) * (f.price <= budget ? 1f : 0.6f);           // 1.0 at 40% under value, 0 at 40% over
            float cond = C01((f.condition - 30f) / 65f) * (f.cleanliness < 40f ? 0.8f : 1f);
            float miles = C01(1f - f.mileage / 220000f);
            float age = C01(1f - f.ageYears / 18f);
            float hist = f.fullHistory ? 1f : 0.5f;
            float econ = f.economical ? 1f : 0.4f;
            float docs = (f.v5cPresent ? 0.6f : 0f) + (f.motValid ? 0.4f : 0f);
            float s = p.WPrice * price + p.WCondition * cond + p.WMileage * miles + p.WAge * age + p.WHistory * hist + p.WEconomy * econ + p.WDocs * docs;
            s -= f.knownFaults * 0.04f;
            int c = (int)f.category;
            float like = c >= 0 && c < p.CategoryLike.Length ? p.CategoryLike[c] : 0.5f;
            return C01(s * (0.4f + 0.6f * like));
        }

        /// <summary>
        /// The most a customer will pay: around market value, more if they love the car (score), less for hagglers,
        /// capped by budget (+ finance stretches the budget by 30%).
        /// </summary>
        public static int MaxPrice(BuyerPersona p, int marketValue, float score, int budget, bool finance)
        {
            float f = 0.92f + score * 0.25f - p.Haggling * 0.1f;
            int cap = (int)(budget * (finance ? 1.3f : 1.05f));
            return Math.Min(cap, (int)(marketValue * f));
        }

        public static int OpeningOffer(BuyerPersona p, int maxPrice) => (int)(maxPrice * (0.92f - p.Haggling * 0.17f) / 50f) * 50;

        // ------------------------------------------------------------------ TRADE-IN & FINANCE
        /// <summary>Customers think their own car is worth more than it is.</summary>
        public static int TradeInExpectation(int tradeInMarketValue, Random rng) => (int)(tradeInMarketValue * (1.05f + rng.NextDouble() * 0.2f) / 50f) * 50;

        /// <summary>Finance approval chance from credit score (300-850) and deposit share.</summary>
        public static float FinanceApproval(int creditScore, float depositShare) =>
            C01((creditScore - 450f) / 350f * 0.8f + depositShare * 0.6f);

        public const float FinanceCommissionRate = 0.03f;
        public static int FinanceCommission(int financed) => (int)Math.Min(1500, Math.Round(financed * FinanceCommissionRate));

        // ------------------------------------------------------------------ REVIEWS
        /// <summary>
        /// Stars 1-5 after a sale: condition, price vs value, honesty (faults you knew and kept quiet about cost a lot),
        /// how hard the haggle was. Harsh customers mark lower.
        /// </summary>
        public static int ReviewStars(BuyerPersona p, float condition, float priceVsValue, int disclosedFaults, int hiddenKnownFaults, int roundsOfHaggling)
        {
            float s = 3.6f;
            s += (condition - 65f) / 25f;
            s += (1f - priceVsValue) * 3f;
            s += disclosedFaults > 0 ? 0.3f : 0f;
            s -= hiddenKnownFaults * 1.2f;
            s -= Math.Max(0, roundsOfHaggling - p.Patience) * 0.3f;
            s -= p.Harshness * 0.6f;
            return Math.Max(1, Math.Min(5, (int)Math.Round(s)));
        }

        /// <summary>Reputation change from a review (3 stars = neutral).</summary>
        public static float ReputationFromStars(int stars) => (stars - 3) * 0.8f;

        /// <summary>Chance a customer notices a fault you didn't mention (during the viewing / test drive).</summary>
        public static float SpotChance(BuyerPersona p, float severity) => C01(p.Knowledge * (0.3f + severity));
    }
}
