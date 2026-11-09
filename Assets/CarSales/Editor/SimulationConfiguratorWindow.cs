using System.IO;
using CarSales.Sim;
using UnityEditor;
using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Phase 10 Simulation Configurator: runs the balancing sandbox (Sim/Balance) for every difficulty preset and a
    /// custom set of knobs, shows the profit and loss side by side, and can save the custom knobs as
    /// Resources/CarSalesBalance.json (Settings > General > Difficulty: CUSTOM).
    /// </summary>
    public class SimulationConfiguratorWindow : EditorWindow
    {
        public const string CustomPath = "Assets/CarSales/Resources/CarSalesBalance.json";

        BalanceConfig custom = new BalanceConfig { name = "Custom" };
        int days = 30, seeds = 10, firstSeed = 1;
        SandboxResult[] results;
        (double net, double sold, double cash)[] averages;
        Vector2 scroll;

        [MenuItem("Tools/Car Sales/Simulation Configurator", priority = 31)]
        public static void Open() => GetWindow<SimulationConfiguratorWindow>("Simulation Configurator").minSize = new Vector2(720f, 520f);

        void OnEnable()
        {
            if (File.Exists(CustomPath))
            {
                var c = BalanceConfig.FromJson(File.ReadAllText(CustomPath));
                if (c != null) custom = c;
            }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Plays a simple dealer for N days with the game's buyer model (personas, budgets, scores, weekend footfall) " +
                                    "and compares the difficulty presets. Average over several seeds to smooth out luck. " +
                                    "This tunes the shape of the economy; play-test to confirm.", UnityEditor.MessageType.Info);
            EditorGUILayout.LabelField("Run", EditorStyles.boldLabel);
            days = EditorGUILayout.IntSlider("Days", days, 7, 120);
            seeds = EditorGUILayout.IntSlider("Seeds to average", seeds, 1, 50);
            firstSeed = EditorGUILayout.IntField("First seed", firstSeed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Custom balance", EditorStyles.boldLabel);
            custom.name = EditorGUILayout.TextField("Name", custom.name);
            custom.startingMoney = EditorGUILayout.IntSlider("Starting money", custom.startingMoney, 5000, 200000);
            custom.budgetMultiplier = EditorGUILayout.Slider("Customer budgets ×", custom.budgetMultiplier, 0.3f, 3f);
            custom.repairCostMultiplier = EditorGUILayout.Slider("Repair costs ×", custom.repairCostMultiplier, 0.3f, 3f);
            custom.footfallMultiplier = EditorGUILayout.Slider("Footfall ×", custom.footfallMultiplier, 0.2f, 3f);
            custom.buyPriceMultiplier = EditorGUILayout.Slider("Stock prices ×", custom.buyPriceMultiplier, 0.5f, 1.5f);
            custom.weeklyOverheads = EditorGUILayout.IntSlider("Weekly overheads (sandbox)", custom.weeklyOverheads, 0, 10000);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Realistic })
                    if (GUILayout.Button("Copy " + d)) { custom = BalanceConfig.Preset(d); custom.name = "Custom"; }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run sandbox", GUILayout.Height(30))) Run();
                if (GUILayout.Button("Save custom balance", GUILayout.Height(30))) SaveCustom();
            }

            if (results != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Results ({days} days, seed {firstSeed}; averages over {seeds} seeds)", EditorStyles.boldLabel);
                Header();
                for (int i = 0; i < results.Length; i++) Row(results[i], averages[i]);
                EditorGUILayout.Space();
                foreach (var r in results) EditorGUILayout.LabelField(r.Summary(), EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        static void Header()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                foreach (var h in new[] { "Preset", "Sold", "Revenue", "Net profit", "End cash", "Lowest cash", "Avg net", "Avg sold" })
                    GUILayout.Label(h, EditorStyles.miniBoldLabel, GUILayout.Width(84));
            }
        }

        static void Row(SandboxResult r, (double net, double sold, double cash) a)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(r.config.name, GUILayout.Width(84));
                GUILayout.Label($"{r.carsSold}/{r.carsBought}", GUILayout.Width(84));
                GUILayout.Label($"${r.revenue:N0}", GUILayout.Width(84));
                GUILayout.Label($"${r.NetProfit:N0}", GUILayout.Width(84));
                GUILayout.Label($"${r.endCash:N0}", GUILayout.Width(84));
                GUILayout.Label($"${r.minCash:N0}", GUILayout.Width(84));
                GUILayout.Label($"${a.net:N0}", GUILayout.Width(84));
                GUILayout.Label($"{a.sold:0.0}", GUILayout.Width(84));
            }
        }

        void Run()
        {
            var configs = new[]
            {
                BalanceConfig.Preset(Difficulty.Easy), BalanceConfig.Preset(Difficulty.Normal), BalanceConfig.Preset(Difficulty.Hard),
                BalanceConfig.Preset(Difficulty.Realistic), custom.Clamped(),
            };
            results = new SandboxResult[configs.Length];
            averages = new (double, double, double)[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                results[i] = Sandbox.Run(configs[i], days, firstSeed);
                averages[i] = Sandbox.Average(configs[i], days, seeds, firstSeed);
            }
        }

        void SaveCustom()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CustomPath));
            File.WriteAllText(CustomPath, custom.Clamped().ToJson());
            AssetDatabase.ImportAsset(CustomPath);
            Debug.Log("[Car Sales] Saved custom balance to " + CustomPath + ". Choose Settings > General > Difficulty: CUSTOM, then start a new game.");
        }
    }
}
