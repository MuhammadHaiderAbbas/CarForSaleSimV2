using CarSales.Foundation;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    [Serializable]
    public class UpgradeInfo
    {
        public UpgradeType type;
        public string displayName;
        [TextArea] public string description;
        public int[] costs;   // one entry per level
    }

    /// <summary>Dealership upgrades bought from the computer. Other systems read the effect properties below.</summary>
    public class UpgradeManager : MonoBehaviour
    {
        [SerializeField] List<UpgradeInfo> upgrades = new List<UpgradeInfo>
        {
            new UpgradeInfo { type = UpgradeType.LotExpansion, displayName = "Lot Expansion",
                description = "Unlocks 3 more parking spots per level.", costs = new[] { 15000, 35000 } },
            new UpgradeInfo { type = UpgradeType.Marketing, displayName = "Marketing",
                description = "More customers, arriving faster, with 10% bigger budgets per level.", costs = new[] { 8000, 20000, 45000 } },
            new UpgradeInfo { type = UpgradeType.Mechanic, displayName = "Mechanic Workshop",
                description = "Repairs 15% cheaper per level. Level 1+ reveals the TRUE condition of marketplace cars.", costs = new[] { 10000, 25000, 50000 } },
            new UpgradeInfo { type = UpgradeType.Detailing, displayName = "Detailing Bay",
                description = "Cleaning 35% cheaper per level.", costs = new[] { 4000, 12000 } },
            new UpgradeInfo { type = UpgradeType.Showroom, displayName = "Showroom Upgrade",
                description = "Customers are 5% more likely to buy per level, and you gain +0.5 reputation per day per level.", costs = new[] { 12000, 30000, 60000 } },
        };

        int[] levels = new int[Enum.GetValues(typeof(UpgradeType)).Length];
        public event Action OnUpgradesChanged;

        void Awake() => CarSalesServices.Upgrades = this;

        public UpgradeInfo Info(UpgradeType t) => upgrades.Find(u => u.type == t);
        public int Level(UpgradeType t) => levels[(int)t];
        public int MaxLevel(UpgradeType t) { var i = Info(t); return i != null && i.costs != null ? i.costs.Length : 0; }
        public bool IsMaxed(UpgradeType t) => Level(t) >= MaxLevel(t);
        public int NextCost(UpgradeType t) => IsMaxed(t) ? -1 : Info(t).costs[Level(t)];

        public bool TryBuy(UpgradeType t, out string message)
        {
            if (IsMaxed(t)) { message = "Already at max level."; return false; }
            int cost = NextCost(t);
            if (!CarSalesServices.Economy.TrySpend(cost, TxCategory.Upgrades, $"Upgrade - {t}")) { message = "Not enough money!"; return false; }
            levels[(int)t]++;
            CarSalesServices.DayCycle.RegisterExpense(cost);
            if (CarSalesServices.Progress) CarSalesServices.Progress.Stats.upgradesBought++;
            OnUpgradesChanged?.Invoke();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            message = $"{Info(t).displayName} upgraded to level {Level(t)}!";
            return true;
        }

        public int[] GetLevels() => (int[])levels.Clone();

        public void SetLevels(int[] saved)
        {
            levels = new int[Enum.GetValues(typeof(UpgradeType)).Length];
            if (saved != null)
                for (int i = 0; i < saved.Length && i < levels.Length; i++)
                    levels[i] = Mathf.Clamp(saved[i], 0, MaxLevel((UpgradeType)i));
            OnUpgradesChanged?.Invoke();
        }

        // -------- Effects (read by other systems) --------
        public int LotLevel => Level(UpgradeType.LotExpansion);
        public float RepairCostMultiplier => 1f - 0.15f * Level(UpgradeType.Mechanic);
        public bool AccurateInspections => Level(UpgradeType.Mechanic) >= 1;
        public int CleanCost => Mathf.RoundToInt(CarValueCalculator.BaseCleanCost * (1f - 0.35f * Level(UpgradeType.Detailing)));
        public float SpawnIntervalMultiplier => 1f - 0.15f * Level(UpgradeType.Marketing);
        public int ExtraCustomers => Level(UpgradeType.Marketing);
        public float BudgetMultiplier => 1f + 0.1f * Level(UpgradeType.Marketing);
        public float ShowroomInterestBonus => 0.05f * Level(UpgradeType.Showroom);
        public float DailyReputationBonus => 0.5f * Level(UpgradeType.Showroom);
    }
}
