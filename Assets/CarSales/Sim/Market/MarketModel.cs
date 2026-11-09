using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    /// <summary>Body types (same order as the runtime's CarCategory).</summary>
    public enum MarketCategory { Hatchback = 0, Sedan = 1, SUV = 2, Sports = 3, Pickup = 4, Luxury = 5 }

    [Serializable] public class ModelIndex { public string carId; public float index = 1f; }

    /// <summary>Saved market data (spec §6.8): price index per model and per body type, short-term demand.</summary>
    [Serializable]
    public class MarketState
    {
        public int lastDay;
        public List<ModelIndex> models = new List<ModelIndex>();
        public float[] category = { 1f, 1f, 1f, 1f, 1f, 1f };
        public float[] demand = { 1f, 1f, 1f, 1f, 1f, 1f };    // falls after sales, recovers
        public List<float> history = new List<float>();       // average index per day (last 60)
        public List<CompetitorListing> competitorListings = new List<CompetitorListing>();
        public List<TradeBatch> tradeBatches = new List<TradeBatch>();
    }

    [Serializable]
    public class CompetitorListing
    {
        public string dealer;
        public string carId;
        public int category;
        public int price;
        public float condition;
        public int mileage;
    }

    /// <summary>A weekly wholesale offer from another dealer: take the whole batch at a discount, or leave it.</summary>
    [Serializable]
    public class TradeBatch
    {
        public string id;
        public string dealer;
        public int day, expiresDay;
        public int price;
        public bool taken;
        public List<string> carIds = new List<string>();   // CarDefinition ids; cars are generated when taken
        public List<float> conditions = new List<float>();
        public List<int> mileages = new List<int>();
    }

    public sealed class CompetitorDef
    {
        public string Name;
        public float Margin;      // over market value
        public float Quality;     // typical condition they sell
        public int Size;          // cars listed
    }

    public static class MarketModel
    {
        public const float DailyNoise = 0.005f, MeanReversion = 0.05f, MinIndex = 0.8f, MaxIndex = 1.25f;
        public const float SaleDemandDrop = 0.02f, DemandRecovery = 0.2f;

        public static readonly CompetitorDef[] Competitors =
        {
            new CompetitorDef { Name = "Downtown Motors", Margin = 0.10f, Quality = 70f, Size = 8 },
            new CompetitorDef { Name = "Luxury Lane", Margin = 0.18f, Quality = 82f, Size = 6 },
            new CompetitorDef { Name = "Cheap Wheels", Margin = 0.02f, Quality = 52f, Size = 10 },
        };

        static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        static float Gauss(Random rng) => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Sin(2.0 * Math.PI * rng.NextDouble()));

        /// <summary>One day of drift: a mean-reverting random walk (±0.5%/day) and demand recovering towards 1.</summary>
        public static void Step(MarketState s, IEnumerable<string> carIds, int day, Random rng)
        {
            foreach (var id in carIds)
                if (!s.models.Exists(m => m.carId == id)) s.models.Add(new ModelIndex { carId = id });
            foreach (var m in s.models)
                m.index = Clamp(m.index + (1f - m.index) * MeanReversion + Gauss(rng) * DailyNoise, MinIndex, MaxIndex);
            for (int i = 0; i < s.category.Length; i++)
            {
                s.category[i] = Clamp(s.category[i] + (1f - s.category[i]) * MeanReversion + Gauss(rng) * DailyNoise, MinIndex, MaxIndex);
                s.demand[i] = s.demand[i] + (1f - s.demand[i]) * DemandRecovery;
            }
            float avg = 0f;
            foreach (var m in s.models) avg += m.index;
            s.history.Add(s.models.Count > 0 ? avg / s.models.Count : 1f);
            while (s.history.Count > 60) s.history.RemoveAt(0);
            s.lastDay = day;
        }

        /// <summary>
        /// Seasons from the game calendar (quarter 1 = Jan-Mar ...): sports cars +8% in summer (Q2/Q3 peak),
        /// SUVs and pickups +5% in winter, everything else flat.
        /// </summary>
        public static float Season(MarketCategory c, int quarterInYear)
        {
            switch (c)
            {
                case MarketCategory.Sports: return quarterInYear == 2 ? 1.04f : quarterInYear == 3 ? 1.08f : quarterInYear == 4 ? 0.97f : 0.96f;
                case MarketCategory.SUV: case MarketCategory.Pickup: return quarterInYear == 1 || quarterInYear == 4 ? 1.05f : 0.98f;
                default: return 1f;
            }
        }

        /// <summary>Few cars of a type on the market push its price up a little; many push it down.</summary>
        public static float Supply(int listedOfCategory, int typical = 3) => Clamp(1f + (typical - listedOfCategory) * 0.01f, 0.95f, 1.05f);

        public static float Multiplier(MarketState s, string carId, MarketCategory c, int quarterInYear, int listedOfCategory)
        {
            var m = s.models.Find(x => x.carId == carId);
            int i = (int)c;
            float cat = i >= 0 && i < s.category.Length ? s.category[i] : 1f;
            float dem = i >= 0 && i < s.demand.Length ? s.demand[i] : 1f;
            return (m != null ? m.index : 1f) * cat * dem * Season(c, quarterInYear) * Supply(listedOfCategory);
        }

        public static void RegisterSale(MarketState s, MarketCategory c)
        {
            int i = (int)c;
            if (i >= 0 && i < s.demand.Length) s.demand[i] = Clamp(s.demand[i] - SaleDemandDrop, 0.85f, 1.1f);
        }

        /// <summary>A competitor's asking price for a car worth <paramref name="marketValue"/>.</summary>
        public static int CompetitorPrice(CompetitorDef d, int marketValue, Random rng) =>
            (int)(Math.Round(marketValue * (1f + d.Margin + (float)(rng.NextDouble() - 0.5) * 0.08f) / 100f) * 100);

        /// <summary>Wholesale batches are 15-30% under market value, cheaper per car for bigger batches.</summary>
        public static float BatchDiscount(int cars) => Clamp(0.15f + cars * 0.02f, 0.15f, 0.3f);
    }
}
