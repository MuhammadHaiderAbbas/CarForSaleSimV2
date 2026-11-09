using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 10: the balance in effect. A save keeps the difficulty it was started with (business module);
    /// before a game is loaded (main menu) the Settings choice is used. The custom balance is
    /// Resources/CarSalesBalance.json, written by Tools > Car Sales > Simulation Configurator.
    /// </summary>
    public static class BalanceSettings
    {
        public const string CustomResource = "CarSalesBalance";

        public static string CustomJson()
        {
            var t = Resources.Load<TextAsset>(CustomResource);
            return t ? t.text : "";
        }

        public static bool HasCustom => Resources.Load<TextAsset>(CustomResource) != null;

        public static BalanceConfig Resolve(int difficulty, string customJson)
        {
            if (difficulty == (int)Difficulty.Custom)
            {
                var c = BalanceConfig.FromJson(string.IsNullOrEmpty(customJson) ? CustomJson() : customJson);
                if (c != null) return c;
                return BalanceConfig.Preset(Difficulty.Normal);
            }
            return BalanceConfig.Preset((Difficulty)Mathf.Clamp(difficulty, 0, 3));
        }

        /// <summary>The current game's balance (Normal when nothing is loaded).</summary>
        public static BalanceConfig Current => CarSalesServices.Business ? CarSalesServices.Business.Balance : Resolve(GameSettings.Difficulty, null);
    }
}
