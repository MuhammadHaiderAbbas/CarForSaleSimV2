using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CarSales.Foundation;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    /// <summary>Phase 10: strings table, balancing sandbox, save migration matrix.</summary>
    public class Phase10Tests
    {
        // ------------------------------------------------------------------ STRINGS
        [Test]
        public void Strings_LoadsTable_FallsBack()
        {
            var s = new Strings();
            Assert.IsTrue(s.LoadJson("{\"a\": \"Apple\", \"n\": 3, \"fmt\": \"{0} cars\"}", "en"));
            Assert.AreEqual(2, s.Count, "non-string values are skipped");
            Assert.AreEqual("Apple", s.T("a", "x"));
            Assert.AreEqual("Fallback", s.T("missing", "Fallback"));
            Assert.AreEqual("missing", s.T("missing"));
            Assert.AreEqual("4 cars", s.Format("fmt", "", 4));
            Assert.AreEqual("{0 broken", new Strings().Format("x", "{0 broken", 1), "bad format strings don't throw");
            Assert.IsFalse(s.LoadJson("[1,2]"));
            Assert.AreEqual(0, s.Count);
            Assert.IsFalse(s.LoadJson("not json"));
        }

        [Test]
        public void Strings_FindsDuplicateKeys()
        {
            CollectionAssert.AreEqual(new[] { "a" }, Strings.DuplicateKeys("{\"a\": \"1\", \"b\": \"2\", \"a\": \"3\"}"));
            Assert.IsEmpty(Strings.DuplicateKeys("{\"a\": \"1\"}"));
        }

        static string CarSalesFolder()
        {
            var starts = new List<string>();
            string env = Environment.GetEnvironmentVariable("CARSALES_ROOT");
            if (!string.IsNullOrEmpty(env)) starts.Add(env);
            starts.Add(Directory.GetCurrentDirectory());
            starts.Add(TestContext.CurrentContext.TestDirectory);
            foreach (var s in starts)
            {
                var dir = new DirectoryInfo(s);
                while (dir != null)
                {
                    string cs = Path.Combine(dir.FullName, "Assets", "CarSales");
                    if (Directory.Exists(Path.Combine(cs, "Scripts"))) return cs;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        [Test]
        public void EnglishTable_HasEveryKeyTheGameUses_NoDuplicates()
        {
            string root = CarSalesFolder();
            if (root == null) Assert.Ignore("Source folder not found (set CARSALES_ROOT).");
            string file = Path.Combine(root, "Resources", "Localization", "en.json");
            Assert.IsTrue(File.Exists(file), "Resources/Localization/en.json is missing");
            string json = File.ReadAllText(file);
            Assert.IsEmpty(Strings.DuplicateKeys(json), "duplicate keys in en.json");
            var table = new Strings();
            Assert.IsTrue(table.LoadJson(json));

            var re = new Regex("Loc\\.[TF]\\(\"([^\"]+)\"\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            var missing = new List<string>();
            var fallbacks = new Dictionary<string, string>();
            var conflicts = new List<string>();
            foreach (var cs in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in re.Matches(File.ReadAllText(cs)))
                {
                    string key = m.Groups[1].Value, fb = Regex.Unescape(m.Groups[2].Value);
                    if (!table.Has(key)) missing.Add($"{Path.GetFileName(cs)}: {key}");
                    if (fallbacks.TryGetValue(key, out var other) && other != fb) conflicts.Add($"{key}: \"{other}\" vs \"{fb}\"");
                    fallbacks[key] = fb;
                }
            Assert.Greater(fallbacks.Count, 40, "expected the menus, desktop and settings to use the strings table");
            Assert.IsEmpty(missing, "keys used in code but not in en.json:\n" + string.Join("\n", missing));
            Assert.IsEmpty(conflicts, "one key with two different English texts:\n" + string.Join("\n", conflicts));
        }

        // ------------------------------------------------------------------ BALANCE
        [Test]
        public void Presets_GetHarderInOrder()
        {
            var e = BalanceConfig.Preset(Difficulty.Easy);
            var n = BalanceConfig.Preset(Difficulty.Normal);
            var h = BalanceConfig.Preset(Difficulty.Hard);
            var r = BalanceConfig.Preset(Difficulty.Realistic);
            Assert.AreEqual(BalanceConfig.NormalStartingMoney, n.startingMoney);
            Assert.That(e.startingMoney > n.startingMoney && n.startingMoney > h.startingMoney && h.startingMoney > r.startingMoney);
            Assert.That(e.budgetMultiplier > n.budgetMultiplier && n.budgetMultiplier > h.budgetMultiplier && h.budgetMultiplier > r.budgetMultiplier);
            Assert.That(e.repairCostMultiplier < n.repairCostMultiplier && n.repairCostMultiplier < h.repairCostMultiplier && h.repairCostMultiplier < r.repairCostMultiplier);
            Assert.That(e.footfallMultiplier > n.footfallMultiplier && n.footfallMultiplier > h.footfallMultiplier && h.footfallMultiplier > r.footfallMultiplier);
        }

        [Test]
        public void BalanceConfig_JsonRoundTrip_AndClamps()
        {
            var c = new BalanceConfig { name = "Mine \"x\"", startingMoney = 12345, budgetMultiplier = 1.25f, repairCostMultiplier = 0.75f, footfallMultiplier = 1.5f, buyPriceMultiplier = 0.9f, weeklyOverheads = 300 };
            var back = BalanceConfig.FromJson(c.ToJson());
            Assert.AreEqual(c.name, back.name);
            Assert.AreEqual(12345, back.startingMoney);
            Assert.AreEqual(1.25f, back.budgetMultiplier, 1e-4);
            Assert.AreEqual(0.75f, back.repairCostMultiplier, 1e-4);
            Assert.AreEqual(1.5f, back.footfallMultiplier, 1e-4);
            Assert.AreEqual(0.9f, back.buyPriceMultiplier, 1e-4);
            Assert.AreEqual(300, back.weeklyOverheads);

            var wild = BalanceConfig.FromJson("{\"startingMoney\": -5, \"budgetMultiplier\": 99, \"footfallMultiplier\": 0}");
            Assert.AreEqual(1000, wild.startingMoney);
            Assert.AreEqual(3f, wild.budgetMultiplier);
            Assert.AreEqual(0.2f, wild.footfallMultiplier);
            Assert.AreEqual(1f, wild.repairCostMultiplier, "missing fields keep Normal");
            Assert.IsNull(BalanceConfig.FromJson("nope"));
            Assert.IsNull(BalanceConfig.FromJson(""));
        }

        [Test]
        public void Sandbox_IsDeterministic_AndLedgerReconciles()
        {
            var a = Sandbox.Run(BalanceConfig.Preset(Difficulty.Normal), 30, 7);
            var b = Sandbox.Run(BalanceConfig.Preset(Difficulty.Normal), 30, 7);
            Assert.AreEqual(a.endCash, b.endCash);
            Assert.AreEqual(a.carsSold, b.carsSold);
            Assert.AreEqual(30, a.cashByDay.Count);
            Assert.AreEqual(a.endCash, a.ledger.Balance, "every money movement went through the ledger");
            Assert.AreEqual(a.endCash, a.startCash + a.revenue - a.stockCost - a.repairCost - a.overheads);
            Assert.AreEqual(a.NetProfit, a.NetWorthChange, "unsold stock counts at cost");
            Assert.LessOrEqual(a.carsSold, a.carsBought);
            Assert.Greater(a.carsSold, 0);
            Assert.LessOrEqual(a.minCash, a.endCash);
        }

        [Test]
        public void Sandbox_Normal30Days_IsProfitable_ButNotABonanza()
        {
            var (net, sold, _) = Sandbox.Average(BalanceConfig.Preset(Difficulty.Normal), 30, 20);
            Assert.Greater(net, 0, "a sensible dealer should make money on Normal");
            Assert.Less(net, BalanceConfig.NormalStartingMoney * 3, "Normal shouldn't multiply your money in a month");
            Assert.Greater(sold, 10);
        }

        [Test]
        public void Sandbox_HarderPresets_EarnLess()
        {
            double Net(Difficulty d) => Sandbox.Average(BalanceConfig.Preset(d), 30, 20).netProfit;
            double e = Net(Difficulty.Easy), n = Net(Difficulty.Normal), h = Net(Difficulty.Hard), r = Net(Difficulty.Realistic);
            TestContext.WriteLine($"30-day net profit: easy {e:N0}, normal {n:N0}, hard {h:N0}, realistic {r:N0}");
            Assert.Greater(e, n);
            Assert.Greater(n, h);
            Assert.Greater(h, r);
        }

        // ------------------------------------------------------------------ SAVE MIGRATION MATRIX
        // The module keys and versions the game writes (SaveManager); kept here because tests can't see the runtime.
        static readonly (string key, int version)[] GameModules =
        {
            ("game", 6), ("ledger", 1), ("workshop", 1), ("auction", 1), ("market", 1), ("crm", 1),
            ("staff", 1), ("sourcing", 1), ("world", 1), ("business", 1),
        };

        static SaveMigrations GameLikeMigrations(List<string> log)
        {
            var m = new SaveMigrations();
            // A chain like the game module's history: each step leaves a mark so order can be checked.
            for (int v = 1; v < 6; v++)
            {
                int from = v;
                m.Register("game", from, json => { log.Add("game" + from); return json.TrimEnd('}') + ",\"m" + from + "\":true}"; });
            }
            return m;
        }

        [Test]
        public void MigrationMatrix_EveryOldVersion_ReachesCurrent([Values(1, 2, 3, 4, 5, 6)] int gameVersion)
        {
            var log = new List<string>();
            var migrations = GameLikeMigrations(log);
            var env = new SaveEnvelope { Day = 4, Money = 100, GameVersion = "old" };
            env.Set("game", gameVersion, "{\"day\":4}");
            env.Set("ledger", 1, "{\"entries\":[]}");
            var parsed = SaveEnvelope.Parse(env.ToJson(), out bool ok);
            Assert.IsTrue(ok);
            foreach (var (key, version) in GameModules) migrations.Migrate(parsed.Get(key), version);
            var game = parsed.Get("game");
            Assert.AreEqual(6, game.Version);
            Assert.AreEqual(6 - gameVersion, log.Count, "one step per version");
            for (int v = gameVersion; v < 6; v++) StringAssert.Contains("\"m" + v + "\":true", game.Data);
            CollectionAssert.AreEqual(log.OrderBy(x => x).ToList(), log, "steps run oldest first");
            Assert.IsTrue(MiniJson.TryParse(game.Data, out _, out _), "migrated data is still JSON");
        }

        [Test]
        public void MigrationMatrix_MissingModules_LoadAsDefaults()
        {
            // Saves from before each system existed lack its module: Get returns null and Migrate ignores it.
            for (int present = 1; present <= GameModules.Length; present++)
            {
                var env = new SaveEnvelope { Day = 1, Money = 1 };
                foreach (var (key, version) in GameModules.Take(present)) env.Set(key, version, "{\"v\":" + version + "}");
                var parsed = SaveEnvelope.Parse(env.ToJson(), out bool ok);
                Assert.IsTrue(ok);
                var migrations = new SaveMigrations();
                foreach (var (key, version) in GameModules)
                {
                    var m = parsed.Get(key);
                    Assert.DoesNotThrow(() => migrations.Migrate(m, version));
                    bool shouldExist = Array.FindIndex(GameModules, x => x.key == key) < present;
                    Assert.AreEqual(shouldExist, m != null, key);
                }
            }
        }

        [Test]
        public void MigrationMatrix_FutureModule_IsRefused_OthersUntouched()
        {
            foreach (var (key, version) in GameModules)
            {
                var env = new SaveEnvelope();
                foreach (var (k, v) in GameModules) env.Set(k, k == key ? v + 1 : v, "{}");
                var parsed = SaveEnvelope.Parse(env.ToJson(), out _);
                var migrations = new SaveMigrations();
                Assert.Throws<InvalidOperationException>(() => migrations.Migrate(parsed.Get(key), version), key);
                foreach (var (k, v) in GameModules.Where(x => x.key != key))
                {
                    migrations.Migrate(parsed.Get(k), v);
                    Assert.AreEqual(v, parsed.Get(k).Version);
                }
            }
        }

        [Test]
        public void MigrationMatrix_UnknownModules_SurviveARoundTrip()
        {
            var env = new SaveEnvelope { Day = 9 };
            foreach (var (k, v) in GameModules) env.Set(k, v, "{\"k\":\"" + k + "\"}");
            env.Set("modfromthefuture", 3, "{\"keep\":[1,2,3]}");
            var parsed = SaveEnvelope.Parse(env.ToJson(), out bool ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(GameModules.Length + 1, parsed.Modules.Count);
            Assert.AreEqual("{\"keep\":[1,2,3]}", parsed.Get("modfromthefuture").Data);
            var again = SaveEnvelope.Parse(parsed.ToJson(), out bool ok2);
            Assert.IsTrue(ok2);
            Assert.AreEqual(parsed.ComputeChecksum(), again.ComputeChecksum());
        }
    }
}
