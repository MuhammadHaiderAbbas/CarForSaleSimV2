using System;
using CarSales.Foundation;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Cash and reputation. Every change of cash is also recorded in the <see cref="Ledger"/>
    /// (bank statement, daily report, accounts) - pass a category so it shows up in the right place.
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        [SerializeField] int startingMoney = 50000;
        [SerializeField] float startingReputation = 50f;
        [Tooltip("Days of individual transactions kept in the save. Older ones are folded into the opening balance.")]
        [SerializeField] int ledgerDaysKept = 60;

        public int Money { get; private set; }          // int, never float, for money
        public float Reputation { get; private set; }
        public Ledger Ledger { get; } = new Ledger();

        public event Action<int, int> OnMoneyChanged;    // (newMoney, delta)
        public event Action<float> OnReputationChanged;

        void Awake()
        {
            CarSalesServices.Economy = this;
            Money = startingMoney;
            Reputation = startingReputation;
            Ledger.Reset(Money, 1);
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        void OnDayStarted(DayStarted e) => Ledger.Compact(e.Day, ledgerDaysKept);

        /// <summary>Phase 9: you can spend into your agreed overdraft.</summary>
        public bool CanAfford(int amount) => Money + (CarSalesServices.Business ? CarSalesServices.Business.OverdraftLimit : 0) >= amount;

        public void AddMoney(int amount, TxCategory category = TxCategory.Other, string memo = null, string refId = null)
        {
            if (amount <= 0) return;
            Apply(amount, category, memo, refId);
        }

        public bool TrySpend(int amount, TxCategory category = TxCategory.Other, string memo = null, string refId = null)
        {
            if (amount < 0 || !CanAfford(amount)) return false;
            Apply(-amount, category, memo, refId);
            return true;
        }

        /// <summary>Rent and bills: allowed to go negative.</summary>
        public void ForceSpend(int amount, TxCategory category = TxCategory.Other, string memo = null, string refId = null)
        {
            if (amount <= 0) return;
            Apply(-amount, category, memo, refId);
        }

        void Apply(int delta, TxCategory category, string memo, string refId)
        {
            Money += delta;
            var day = CarSalesServices.DayCycle;
            Ledger.Post(delta, category, day ? day.CurrentDay : 1, day ? day.HourOfDay : 0f, memo, refId);
            OnMoneyChanged?.Invoke(Money, delta);
            EventBus.Publish(new MoneyChanged(Money, delta));
        }

        public void ChangeReputation(float delta)
        {
            Reputation = Mathf.Clamp(Reputation + delta, 0f, 100f);
            OnReputationChanged?.Invoke(Reputation);
        }

        /// <summary>New game: starting cash and an empty ledger.</summary>
        public void ResetToStart()
        {
            // Phase 10: the difficulty scales the starting money (Normal = the Inspector value).
            var cfg = BalanceSettings.Resolve(GameSettings.Difficulty, null);
            int start = Mathf.RoundToInt(startingMoney * cfg.startingMoney / (float)CarSales.Sim.BalanceConfig.NormalStartingMoney);
            SetState(start, startingReputation);
            Ledger.Reset(start, 1);
        }

        public void SetState(int money, float reputation)
        {
            Money = money;
            Reputation = reputation;
            OnMoneyChanged?.Invoke(Money, 0);
            OnReputationChanged?.Invoke(Reputation);
            EventBus.Publish(new MoneyChanged(Money, 0));
        }

        /// <summary>
        /// Loads a saved ledger (null for saves made before v5: starts a new ledger at the current cash).
        /// Call after <see cref="SetState"/>; any difference to the real cash is posted as an adjustment.
        /// </summary>
        public void LoadLedger(LedgerState state, int day)
        {
            if (state == null) Ledger.Reset(Money, day);
            else
            {
                Ledger.Load(state);
                int diff = Ledger.Reconcile(Money, day, 0f);
                if (diff != 0) Debug.LogWarning($"[CarSales] Ledger was off by {diff} - corrected.");
            }
        }
    }
}
