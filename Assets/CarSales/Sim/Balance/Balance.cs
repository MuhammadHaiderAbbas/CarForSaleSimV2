using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CarSales.Foundation;

namespace CarSales.Sim
{
    public enum Difficulty { Easy = 0, Normal = 1, Hard = 2, Realistic = 3, Custom = 4 }

    /// <summary>
    /// Phase 10 balance knobs (spec §13). A preset per difficulty, or a custom file written by the Simulation Configurator.
    /// The game reads these when a new game starts: starting money, customer budgets, repair (parts and labour) costs and
    /// how often customers come in.
    /// </summary>
    [Serializable]
    public class BalanceConfig
    {
        public string name = "Normal";
        public int startingMoney = 50000;
        public float budgetMultiplier = 1f;      // customers' budgets
        public float repairCostMultiplier = 1f;  // parts and garage labour
        public float footfallMultiplier = 1f;    // customers per day
        public float buyPriceMultiplier = 1f;    // what stock costs to buy (vs market value) - sandbox and auctions
        public int weeklyOverheads = 600;        // sandbox only: wages, utilities, insurance

        public const int NormalStartingMoney = 50000;

        public static BalanceConfig Preset(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:      return new BalanceConfig { name = "Easy", startingMoney = 80000, budgetMultiplier = 1.15f, repairCostMultiplier = 0.8f, footfallMultiplier = 1.15f, buyPriceMultiplier = 0.97f, weeklyOverheads = 500 };
                case Difficulty.Hard:      return new BalanceConfig { name = "Hard", startingMoney = 30000, budgetMultiplier = 0.9f, repairCostMultiplier = 1.2f, footfallMultiplier = 0.85f, buyPriceMultiplier = 1.03f, weeklyOverheads = 700 };
                case Difficulty.Realistic: return new BalanceConfig { name = "Realistic", startingMoney = 20000, budgetMultiplier = 0.85f, repairCostMultiplier = 1.35f, footfallMultiplier = 0.8f, buyPriceMultiplier = 1.05f, weeklyOverheads = 800 };
                default:                   return new BalanceConfig();
            }
        }

        public BalanceConfig Clone() => (BalanceConfig)MemberwiseClone();

        /// <summary>Keeps every knob in a sane range (custom files are hand-editable).</summary>
        public BalanceConfig Clamped()
        {
            var c = Clone();
            c.startingMoney = Math.Max(1000, Math.Min(1000000, c.startingMoney));
            c.budgetMultiplier = Clamp(c.budgetMultiplier, 0.3f, 3f);
            c.repairCostMultiplier = Clamp(c.repairCostMultiplier, 0.3f, 3f);
            c.footfallMultiplier = Clamp(c.footfallMultiplier, 0.2f, 3f);
            c.buyPriceMultiplier = Clamp(c.buyPriceMultiplier, 0.5f, 1.5f);
            c.weeklyOverheads = Math.Max(0, Math.Min(100000, c.weeklyOverheads));
            if (string.IsNullOrEmpty(c.name)) c.name = "Custom";
            return c;
        }

        static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? 1f : v < lo ? lo : v > hi ? hi : v;

        public string ToJson()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"name\": ").Append(MiniJson.Quote(name)).Append(",\n");
            sb.Append("  \"startingMoney\": ").Append(startingMoney.ToString(ci)).Append(",\n");
            sb.Append("  \"budgetMultiplier\": ").Append(budgetMultiplier.ToString("0.###", ci)).Append(",\n");
            sb.Append("  \"repairCostMultiplier\": ").Append(repairCostMultiplier.ToString("0.###", ci)).Append(",\n");
            sb.Append("  \"footfallMultiplier\": ").Append(footfallMultiplier.ToString("0.###", ci)).Append(",\n");
            sb.Append("  \"buyPriceMultiplier\": ").Append(buyPriceMultiplier.ToString("0.###", ci)).Append(",\n");
            sb.Append("  \"weeklyOverheads\": ").Append(weeklyOverheads.ToString(ci)).Append("\n");
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Reads a config; missing fields keep the Normal value. Null if the text is not a JSON object.</summary>
        public static BalanceConfig FromJson(string json)
        {
            if (string.IsNullOrEmpty(json) || !MiniJson.TryParse(json, out var n, out _) || n.Kind != JsonKind.Object) return null;
            var c = new BalanceConfig();
            c.name = n["name"]?.AsString(c.name) ?? c.name;
            c.startingMoney = n["startingMoney"]?.AsInt(c.startingMoney) ?? c.startingMoney;
            c.budgetMultiplier = Num(n["budgetMultiplier"], c.budgetMultiplier);
            c.repairCostMultiplier = Num(n["repairCostMultiplier"], c.repairCostMultiplier);
            c.footfallMultiplier = Num(n["footfallMultiplier"], c.footfallMultiplier);
            c.buyPriceMultiplier = Num(n["buyPriceMultiplier"], c.buyPriceMultiplier);
            c.weeklyOverheads = n["weeklyOverheads"]?.AsInt(c.weeklyOverheads) ?? c.weeklyOverheads;
            return c.Clamped();
        }

        static float Num(JsonNode n, float fallback) => n != null && n.Kind == JsonKind.Number ? (float)n.Num : fallback;
    }

    /// <summary>What a sandbox run produced (one profit and loss account).</summary>
    public sealed class SandboxResult
    {
        public BalanceConfig config;
        public int days, seed;
        public int carsBought, carsSold, customers, walkedAway;
        public long revenue, stockCost, repairCost, overheads;
        public long startCash, endCash, minCash, stockValueAtEnd;
        public readonly List<long> cashByDay = new List<long>();
        public Ledger ledger;

        /// <summary>Sales minus what the sold cars cost (bought + repaired).</summary>
        public long GrossProfit => revenue - (stockCost + repairCost - stockValueAtEnd);
        /// <summary>Profit on the cars sold, after overheads (unsold stock counts at cost, so this = change in net worth).</summary>
        public long NetProfit => GrossProfit - overheads;
        /// <summary>Cash plus stock at cost, minus what you started with.</summary>
        public long NetWorthChange => endCash + stockValueAtEnd - startCash;
        public float SellThrough => carsBought == 0 ? 0f : carsSold / (float)carsBought;
        public float ConversionRate => customers == 0 ? 0f : carsSold / (float)customers;

        public string Summary()
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Format(ci, "{0} - {1} days: bought {2}, sold {3} ({4:0}% of {5} customers). Revenue ${6:N0}, stock ${7:N0}, repairs ${8:N0}, overheads ${9:N0}. Net ${10:N0}. Cash ${11:N0} (lowest ${12:N0}).",
                config.name, days, carsBought, carsSold, ConversionRate * 100f, customers, revenue, stockCost, repairCost, overheads, NetProfit, endCash, minCash);
        }
    }

    /// <summary>
    /// Phase 10 balancing sandbox: plays a simple, sensible dealer for N days with the real buyer model
    /// (<see cref="Buyers"/> personas, scores and price limits), weekend/holiday footfall and the ledger, so the
    /// presets can be compared without running the game. Deterministic for a seed.
    /// </summary>
    public static class Sandbox
    {
        sealed class StockCar
        {
            public int value, cost, asking, ageYears, mileage, daysListed;
            public float condition;
            public MarketCategory category;
            public bool economical, history;
        }

        public const int MaxStock = 6;
        public const float BaseCustomersPerDay = 4f;
        public const float MinScoreToBuy = 0.4f;
        public const float AskingMarkup = 1.12f;

        public static SandboxResult Run(BalanceConfig config, int days, int seed)
        {
            var cfg = (config ?? new BalanceConfig()).Clamped();
            var rng = new Random(seed);
            var r = new SandboxResult { config = cfg, days = days, seed = seed, ledger = new Ledger() };
            r.ledger.Reset(cfg.startingMoney, 1);
            r.startCash = cfg.startingMoney;
            long cash = cfg.startingMoney;
            r.minCash = cash;
            var stock = new List<StockCar>();

            void Post(int amount, TxCategory c, int day, string memo)
            {
                r.ledger.Post(amount, c, day, 12f, memo);
                cash += amount;
            }

            for (int day = 1; day <= days; day++)
            {
                // Buy: keep the lot full while keeping a cash buffer for repairs.
                int attempts = 0;
                while (stock.Count < MaxStock && attempts++ < 10)
                {
                    int value = 3000 + rng.Next(22000);
                    // Auction/trade prices plus fees: roughly 76-88% of what the car is worth once fixed.
                    int price = (int)(value * (0.76 + rng.NextDouble() * 0.12) * cfg.buyPriceMultiplier);
                    int repair = (int)(value * (0.03 + rng.NextDouble() * 0.07) * cfg.repairCostMultiplier);
                    if (cash - price - repair < 2000) break;
                    Post(-price, TxCategory.VehiclePurchase, day, "Stock");
                    Post(-repair, TxCategory.Parts, day, "Repairs");
                    r.carsBought++;
                    r.stockCost += price;
                    r.repairCost += repair;
                    stock.Add(new StockCar
                    {
                        value = value, cost = price + repair, asking = (int)(value * AskingMarkup / 50) * 50,
                        ageYears = 2 + rng.Next(14), mileage = 20000 + rng.Next(160000),
                        condition = 55f + (float)rng.NextDouble() * 40f,
                        category = (MarketCategory)rng.Next(6), economical = rng.NextDouble() < 0.4, history = rng.NextDouble() < 0.6,
                    });
                }

                // Customers.
                double expected = BaseCustomersPerDay * cfg.footfallMultiplier * WorldSim.DayFootfall(day);
                int customers = (int)expected + (rng.NextDouble() < expected - Math.Floor(expected) ? 1 : 0);
                for (int i = 0; i < customers && stock.Count > 0; i++)
                {
                    r.customers++;
                    var p = Buyers.Pick(rng);
                    int budget = (int)((p.BudgetMin + rng.NextDouble() * (p.BudgetMax - p.BudgetMin)) * cfg.budgetMultiplier);
                    StockCar best = null;
                    float bestScore = 0f;
                    foreach (var c in stock)
                    {
                        var f = new CarFacts
                        {
                            price = c.asking, marketValue = c.value, condition = c.condition, cleanliness = 80f, mileage = c.mileage, ageYears = c.ageYears,
                            category = c.category, economical = c.economical, fullHistory = c.history, motValid = true, v5cPresent = true,
                        };
                        float s = Buyers.Score(p, f, budget);
                        if (s > bestScore) { bestScore = s; best = c; }
                    }
                    if (best == null || bestScore < MinScoreToBuy) { r.walkedAway++; continue; }
                    bool finance = rng.NextDouble() < p.FinanceChance;
                    int max = Buyers.MaxPrice(p, best.value, bestScore, budget, finance);
                    // The dealer drops the price a little the longer a car sits, never below cost + 5%.
                    int floor = (int)(best.cost * 1.05);
                    int ask = Math.Max(floor, (int)(best.asking * (1.0 - Math.Min(0.1, best.daysListed * 0.01))));
                    if (max < floor) { r.walkedAway++; continue; }
                    int sale = Math.Min(ask, max);
                    Post(sale, TxCategory.VehicleSale, day, "Sale");
                    r.revenue += sale;
                    r.carsSold++;
                    stock.Remove(best);
                }
                foreach (var c in stock) c.daysListed++;

                if (day % 7 == 0 && cfg.weeklyOverheads > 0)
                {
                    Post(-cfg.weeklyOverheads, TxCategory.Wages, day, "Overheads");
                    r.overheads += cfg.weeklyOverheads;
                }
                if (cash < 0)
                {
                    int interest = BusinessRules.OverdraftInterest((int)Math.Max(int.MinValue, cash));
                    if (interest > 0) { Post(-interest, TxCategory.LoanInterest, day, "Overdraft"); r.overheads += interest; }
                }
                r.cashByDay.Add(cash);
                if (cash < r.minCash) r.minCash = cash;
            }
            r.endCash = cash;
            foreach (var c in stock) r.stockValueAtEnd += c.cost;
            return r;
        }

        /// <summary>Average of several seeds (smooths the luck out of a comparison).</summary>
        public static (double netProfit, double sold, double endCash) Average(BalanceConfig config, int days, int seeds, int firstSeed = 1)
        {
            double net = 0, sold = 0, end = 0;
            for (int s = 0; s < seeds; s++)
            {
                var r = Run(config, days, firstSeed + s);
                net += r.NetProfit; sold += r.carsSold; end += r.endCash;
            }
            return (net / seeds, sold / seeds, end / seeds);
        }
    }
}
