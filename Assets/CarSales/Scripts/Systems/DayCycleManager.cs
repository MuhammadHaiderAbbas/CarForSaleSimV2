using CarSales.Foundation;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// The 24-hour day. Time always runs. Dealerships open 08:00-20:00. Energy drains while awake.
    /// The day only ends when you SLEEP at home (from 18:00) - or pass out at 02:00 (penalty).
    /// Sleeping pays rent/interest, collects property income, shows the report and saves.
    /// </summary>
    public class DayCycleManager : MonoBehaviour
    {
        public const float EarliestSleepHour = 18f;

        [SerializeField] float realSecondsPerGameHour = 50f;   // ~15 real minutes per working day
        [SerializeField] float openHour = 8f, closeHour = 20f;
        [SerializeField] float passOutHour = 26f;              // 02:00 next morning
        [SerializeField] int baseRent = 500, rentPerCar = 50, passOutFee = 300;
        [SerializeField] InputActionReference toggleShopAction;

        public int CurrentDay { get; private set; } = 1;
        /// <summary>Hours since midnight of the current day (can go past 24 after midnight).</summary>
        public float Hour { get; private set; } = 8f;
        public float HourOfDay => Hour % 24f;
        public float Energy => energyAtWake * Mathf.Clamp01((passOutHour - Hour) / Mathf.Max(0.1f, passOutHour - wakeHour));
        public bool ClosedByPlayer { get; private set; }
        public bool IsBusinessHours => Hour >= OpenHour && Hour < CloseHour;
        public bool IsOpen => IsBusinessHours && !ClosedByPlayer;
        public bool IsDayOver => Hour >= CloseHour;
        public bool CanSleep => Hour >= EarliestSleepHour || Energy < 25f;
        /// <summary>Phase 8: Sunday trading hours (an hour later, two hours earlier).</summary>
        public float OpenHour => CarSales.Sim.WorldSim.Hours(CurrentDay, openHour, closeHour).open;
        public float CloseHour => CarSales.Sim.WorldSim.Hours(CurrentDay, openHour, closeHour).close;
        public DailyReport Today { get; private set; } = new DailyReport { day = 1 };

        public event Action<float> OnTimeChanged;
        public event Action<bool> OnShopStateChanged;
        public event Action<int> OnDayStarted;

        float wakeHour = 8f, energyAtWake = 100f;
        bool lastOpen, warnedTired, warnedVeryTired, sleeping;
        InputAction toggleShop;

        void Awake() => CarSalesServices.DayCycle = this;

        void OnEnable()
        {
            toggleShop = CarSalesInput.Resolve(toggleShopAction, "ToggleShop");
            if (toggleShop != null) toggleShop.performed += OnToggleShopInput;
        }

        void OnDisable() { if (toggleShop != null) toggleShop.performed -= OnToggleShopInput; }

        void OnToggleShopInput(InputAction.CallbackContext _)
        {
            if (CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen) return;
            ToggleShop();
        }

        /// <summary>Close (or reopen) all your dealerships for the rest of the day.</summary>
        public void ToggleShop()
        {
            ClosedByPlayer = !ClosedByPlayer;
            foreach (var d in DealershipManager.All) d.ClosedByPlayer = ClosedByPlayer;
            if (!IsBusinessHours)
                CarSalesServices.UI.Notify($"Dealerships are only open {OpenHour:00}:00-{CloseHour:00}:00.");
            else CarSalesServices.UI.Notify(ClosedByPlayer ? "All dealerships closed for now." : "Dealerships are OPEN again!");
            CheckOpenState(true);
        }

        void Update()
        {
            if (sleeping || Time.deltaTime <= 0f) return;
            Hour += Time.deltaTime / realSecondsPerGameHour;
            OnTimeChanged?.Invoke(Hour);
            CheckOpenState(false);

            if (!warnedTired && Hour >= 23f)
            {
                warnedTired = true;
                CarSalesServices.UI.Toast(ToastType.Warning, "Getting tired", "It's late. Go home and sleep (your bed is at your home - see the map, M).");
            }
            if (!warnedVeryTired && Hour >= 25f)
            {
                warnedVeryTired = true;
                CarSalesServices.UI.Toast(ToastType.Warning, "Exhausted!", "You'll pass out at 02:00 and wake up at home with a fine. Go to bed!");
            }
            if (Hour >= passOutHour) Sleep(true);
        }

        void CheckOpenState(bool force)
        {
            bool open = IsOpen;
            if (open == lastOpen && !force) return;
            bool changed = open != lastOpen;
            lastOpen = open;
            OnShopStateChanged?.Invoke(open);
            if (!changed) return;
            if (open && IsBusinessHours && Hour < OpenHour + 0.1f)
            {
                CarSalesServices.PlaySfx(Sfx.DoorChime);
                CarSalesServices.UI.Toast(ToastType.Info, "Dealerships open", $"It's {OpenHour:00}:00 - customers are on their way to your listed cars.");
                if (CarSalesServices.Progress) CarSalesServices.Progress.Stats.shopOpened++;
            }
            else if (!open && Hour >= CloseHour && Hour < CloseHour + 0.1f)
                CarSalesServices.UI.Toast(ToastType.Info, "Closing time", "Dealerships are closed. Finish your work, then go home to sleep.");
            if (open && CarSalesServices.Progress) CarSalesServices.Progress.Stats.shopOpened = Mathf.Max(1, CarSalesServices.Progress.Stats.shopOpened);
        }

        /// <summary>Skips time (taxi rides etc.).</summary>
        public void AdvanceTime(float hours) { Hour += Mathf.Max(0f, hours); OnTimeChanged?.Invoke(Hour); }

        /// <summary>Ends the day: accounting, report, new morning at home, autosave.</summary>
        public void Sleep(bool passedOut)
        {
            if (sleeping) return;
            sleeping = true;
            try
            {
                var drive = CarSalesServices.TestDrive;
                if (drive && drive.IsDriving) drive.EndDrive();
                CustomerSpawner.StopAll(true);
                CarSalesServices.UI.Close();

                var props = CarSalesServices.Properties;
                int carsForRent = CarSalesServices.Inventory.OwnedCars.Count(c => !c.personal);
                int rent = baseRent + rentPerCar * carsForRent + (props ? props.DailyCosts : 0);
                CarSalesServices.Economy.ForceSpend(rent, TxCategory.Rent, $"Rent & bills ({carsForRent} cars in stock)");
                int interest = CarSalesServices.Bank ? CarSalesServices.Bank.ChargeInterest() : 0;
                int income = props ? props.DailyIncome : 0;
                if (income > 0) CarSalesServices.Economy.AddMoney(income, TxCategory.PropertyIncome, "Property income");
                int fee = passedOut ? passOutFee : 0;
                if (fee > 0) CarSalesServices.Economy.ForceSpend(fee, TxCategory.Fines, "Passed out - taxi & clinic");

                Today.rent = rent;
                Today.interest = interest;
                Today.propertyIncome = income;
                Today.passOutFee = fee;
                Today.passedOut = passedOut;
                Today.expenses += rent + interest + fee;

                float rep = (CarSalesServices.Upgrades ? CarSalesServices.Upgrades.DailyReputationBonus : 0f) +
                            (props && props.ActiveHomeInfo != null ? props.ActiveHomeInfo.dailyReputation : 0f);
                if (rep > 0f) CarSalesServices.Economy.ChangeReputation(rep);

                if (CarSalesServices.Progress && CarSalesServices.Progress.OnDayEnded())
                {
                    CarSalesServices.Save.DeleteSaveFile();   // "Continue" must not load a bankrupt game
                    return;
                }

                var report = Today;
                int endedDay = CurrentDay;
                EventBus.Publish(new DayEnded(endedDay, passedOut));
                if (GameCalendar.IsLastDayOfWeek(endedDay)) EventBus.Publish(new WeekEnded(GameCalendar.WeekOf(endedDay)));
                if (GameCalendar.IsLastDayOfQuarter(endedDay)) EventBus.Publish(new QuarterEnded(GameCalendar.QuarterOf(endedDay)));
                StartDay(endedDay + 1, passedOut ? 60f : 100f);
                CarSalesServices.Marketplace.RefreshListings();
                if (CarSalesServices.Auction) CarSalesServices.Auction.GenerateLots();
                if (CarSalesServices.Messages) CarSalesServices.Messages.GenerateDaily(CurrentDay);
                TeleportPlayerHome();
                CarSalesServices.UI.dailyReport.Show(report);
                CarSalesServices.Save.SaveGame();
            }
            finally { sleeping = false; }
        }

        public void StartDay(int day, float energy)
        {
            CurrentDay = Mathf.Max(1, day);
            wakeHour = CarSalesServices.Properties ? CarSalesServices.Properties.WakeHour : 8f;
            Hour = wakeHour;
            energyAtWake = Mathf.Clamp(energy, 10f, 100f);
            warnedTired = warnedVeryTired = false;
            ClosedByPlayer = false;
            foreach (var d in DealershipManager.All) d.ClosedByPlayer = false;
            Today = new DailyReport { day = CurrentDay };
            lastOpen = !IsOpen;
            CheckOpenState(true);
            OnTimeChanged?.Invoke(Hour);
            OnDayStarted?.Invoke(CurrentDay);
            EventBus.Publish(new DayStarted(CurrentDay));
        }

        /// <summary>Compatibility: start a fresh day.</summary>
        public void SetDay(int day) => StartDay(day, 100f);

        /// <summary>Restores a saved game's clock.</summary>
        public void SetState(int day, float hour, float energy, bool closedByPlayer)
        {
            StartDay(day, 100f);
            if (hour > 0f)
            {
                Hour = Mathf.Clamp(hour, 0f, passOutHour - 0.5f);
                float ratio = Mathf.Clamp01((passOutHour - Hour) / Mathf.Max(0.1f, passOutHour - wakeHour));
                energyAtWake = ratio > 0.01f ? Mathf.Clamp(energy / ratio, 10f, 100f) : 100f;
            }
            if (closedByPlayer) { ClosedByPlayer = true; foreach (var d in DealershipManager.All) d.ClosedByPlayer = true; }
            lastOpen = !IsOpen;
            CheckOpenState(true);
        }

        public static void TeleportPlayerHome()
        {
            var props = CarSalesServices.Properties;
            var bed = props ? HomeBed.Find(props.ActiveHome) : null;
            if (!bed) return;
            var p = bed.wakePoint ? bed.wakePoint : bed.transform;
            TowService.TeleportPlayer(p.position, p.eulerAngles.y);
        }

        public void RegisterSale(int price, int profit) { Today.carsSold++; Today.revenue += price; Today.profit += profit; }
        public void RegisterPurchase(int price) { Today.carsBought++; Today.expenses += price; }
        public void RegisterExpense(int amount) => Today.expenses += amount;
    }
}
