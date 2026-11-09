using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    [Serializable] public class ActiveCampaign { public string id; public int untilDay; }
    [Serializable] public class PropertyLevel { public string id; public int level; }

    [Serializable]
    public class BusinessState
    {
        public int version = 1;
        public List<SaleRecord> sales = new List<SaleRecord>();
        public List<VatReturn> vatReturns = new List<VatReturn>();
        public int insurance;                     // InsuranceTier
        public bool security;
        public List<int> claimDays = new List<int>();
        public List<ActiveCampaign> campaigns = new List<ActiveCampaign>();
        public List<PropertyLevel> developments = new List<PropertyLevel>();
        public List<int> overdrawnDays = new List<int>();
        public int level = 1;
        public int overdraftLimit;
        public int difficulty = 1;                // Phase 10: Sim.Difficulty chosen when the game started
        public string balanceJson = "";           // Phase 10: the custom balance (Difficulty.Custom), kept with the save
        public InsuranceTier Insurance => (InsuranceTier)insurance;
    }

    /// <summary>
    /// Phase 9 business (spec §6.12): sale records, VAT (margin scheme) with quarterly returns, overdraft by credit
    /// rating, insurance and claims, security, utilities and business rates, marketing campaigns, dealership levels
    /// 1-6 with unlocks, property development. Every money movement goes through the ledger. Module "business".
    /// </summary>
    public class BusinessManager : MonoBehaviour
    {
        BusinessState state = new BusinessState();
        readonly System.Random rng = new System.Random();
        (int persona, bool viaLead, string soldBy)? saleContext;

        public BusinessState State => state;
        public int Level => state.level;
        public int OverdraftLimit => state.overdraftLimit;
        public event Action OnChanged;

        void Awake() => CarSalesServices.Business = this;
        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
            EventBus.Subscribe<WeekEnded>(OnWeekEnded);
            EventBus.Subscribe<QuarterEnded>(OnQuarterEnded);
        }
        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            EventBus.Unsubscribe<WeekEnded>(OnWeekEnded);
            EventBus.Unsubscribe<QuarterEnded>(OnQuarterEnded);
        }
        void Start() => Recalculate();

        public void SetState(BusinessState s) { state = s ?? new BusinessState(); balance = null; Recalculate(); OnChanged?.Invoke(); }

        /// <summary>New game: takes the difficulty from Settings (and the custom balance file, for Custom).</summary>
        public void ResetState()
        {
            int d = GameSettings.Difficulty;
            SetState(new BusinessState { difficulty = d, balanceJson = d == (int)Difficulty.Custom ? BalanceSettings.CustomJson() : "" });
        }

        // ------------------------------------------------------------------ BALANCE (phase 10)
        BalanceConfig balance;
        /// <summary>The balance knobs of this save (difficulty preset or its custom file).</summary>
        public BalanceConfig Balance => balance ??= BalanceSettings.Resolve(state.difficulty, state.balanceJson);
        public Difficulty Difficulty => (Difficulty)Mathf.Clamp(state.difficulty, 0, 4);

        static EconomyManager Eco => CarSalesServices.Economy;
        static int Today => CarRecords.Today;

        // ------------------------------------------------------------------ SALES
        /// <summary>Set by the sale paths that know who bought (negotiation, salespeople) just before the car is sold.</summary>
        public void SetSaleContext(int persona, bool viaLead, string soldBy) => saleContext = (persona, viaLead, soldBy ?? "");

        /// <summary>Called from VehicleRegistry for every sale (customer, salesperson, wholesale).</summary>
        public void RecordSale(CarInstance car, int price)
        {
            var def = CarSalesServices.Database ? CarSalesServices.Database.Get(car.carId) : null;
            int bought = Today;
            var ledger = Eco.Ledger;
            var buy = ledger.ForRef(car.instanceId).FirstOrDefault(e => e.category == TxCategory.VehiclePurchase);
            if (buy != null) bought = buy.day;
            var ctx = saleContext ?? (-1, false, "");
            saleContext = null;
            state.sales.Add(new SaleRecord
            {
                day = Today, carName = car.Name, category = def ? (int)def.category : 0, persona = ctx.persona, price = price,
                cost = car.purchasePrice + car.repairSpent, daysInStock = Math.Max(0, Today - bought), viaLead = ctx.viaLead, soldBy = ctx.soldBy,
            });
            while (state.sales.Count > 500) state.sales.RemoveAt(0);
            CheckLevel();
            OnChanged?.Invoke();
        }

        // ------------------------------------------------------------------ VAT
        static (int from, int to) QuarterDays(int quarter) => ((quarter - 1) * GameCalendar.DaysPerQuarter + 1, quarter * GameCalendar.DaysPerQuarter);

        /// <summary>The quarter's figures so far (or for a finished quarter).</summary>
        public VatReturn Estimate(int quarter)
        {
            var (from, to) = QuarterDays(quarter);
            int input = 0, warranty = 0;
            foreach (var e in Eco.Ledger.Entries.Where(x => x.day >= from && x.day <= to))
            {
                if (e.category == TxCategory.Vat && e.amount < 0 && (e.memo ?? "").StartsWith("VAT on fees")) input += -e.amount;
                else if ((e.category == TxCategory.Parts || e.category == TxCategory.Labour) && e.amount < 0) input += BusinessRules.VatInside(-e.amount);
                else if (e.category == TxCategory.Warranty && e.amount > 0) warranty += e.amount;
            }
            return BusinessRules.Return(quarter, state.sales.Where(s => s.day >= from && s.day <= to), warranty, input, to + BusinessRules.VatPaymentDays);
        }

        void OnQuarterEnded(QuarterEnded q)
        {
            if (state.vatReturns.Exists(r => r.quarter == q.Quarter)) return;
            var r = Estimate(q.Quarter);
            if (r.Due < 0)
            {
                Eco.AddMoney(-r.Due, TxCategory.Vat, $"VAT refund Q{q.Quarter}");
                r.paid = true;
            }
            else if (r.Due == 0) r.paid = true;
            state.vatReturns.Add(r);
            string msg = r.Due > 0 ? $"VAT return for Q{q.Quarter}: you owe {UIFactory.Money(r.Due)} (margin VAT {UIFactory.Money(r.outputMargin)}, other {UIFactory.Money(r.outputStandard)}, " +
                                     $"reclaimed {UIFactory.Money(r.input)}). Pay by day {r.dueDay} in Accounts > VAT."
                                   : $"VAT return for Q{q.Quarter}: {(r.Due < 0 ? $"refund of {UIFactory.Money(-r.Due)} paid in" : "nothing to pay")}.";
            CarSalesServices.UI?.Toast(ToastType.Info, "VAT return", msg);
            if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Bank, $"VAT return Q{q.Quarter}", msg);
            OnChanged?.Invoke();
        }

        public bool PayVat(VatReturn r)
        {
            if (r.paid) return false;
            int total = r.Due + r.fine;
            if (!Eco.TrySpend(total, TxCategory.Vat, $"VAT payment Q{r.quarter}" + (r.fine > 0 ? $" (incl. {UIFactory.Money(r.fine)} late fine)" : "")))
            { CarSalesServices.UI.Error($"You need {UIFactory.Money(total)}."); return false; }
            r.paid = true;
            CarSalesServices.UI.Success("VAT paid", $"Q{r.quarter}: {UIFactory.Money(total)}.");
            OnChanged?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ CREDIT
        void Recalculate()
        {
            int netWorth = CarSalesServices.Progress ? CarSalesServices.Progress.NetWorth : Eco ? Eco.Money : 0;
            state.overdrawnDays.RemoveAll(d => d < Today - 30);
            int rating = BusinessRules.CreditRating(Eco ? Eco.Reputation : 50f, state.overdrawnDays.Count, netWorth);
            state.overdraftLimit = BusinessRules.OverdraftLimit(rating);
        }

        public int CreditRating
        {
            get
            {
                int netWorth = CarSalesServices.Progress ? CarSalesServices.Progress.NetWorth : Eco.Money;
                return BusinessRules.CreditRating(Eco.Reputation, state.overdrawnDays.Count, netWorth);
            }
        }

        // ------------------------------------------------------------------ INSURANCE & SECURITY
        int StockValue() => CarSalesServices.Inventory ? CarSalesServices.Inventory.OwnedCars.Sum(c => CarValueCalculator.MarketValue(c)) : 0;
        int ClaimsLastYear => state.claimDays.Count(d => d >= Today - 364);
        public int WeeklyPremium(InsuranceTier t) => BusinessRules.WeeklyPremium(t, StockValue(), ClaimsLastYear, state.security);

        public void SetInsurance(InsuranceTier t) { state.insurance = (int)t; CarSalesServices.UI.Notify($"Insurance: {t} ({UIFactory.Money(WeeklyPremium(t))}/week)."); OnChanged?.Invoke(); }
        public void SetSecurity(bool on) { state.security = on; OnChanged?.Invoke(); }
        public bool Security => state.security;
        public float TheftFactor => BusinessRules.TheftFactor(state.security);

        /// <summary>A theft/vandalism loss on one of your cars: the insurer pays (minus the excess) if you're covered.</summary>
        public int Claim(int loss, bool forecourtDamage, string what)
        {
            int pay = BusinessRules.Payout(state.Insurance, loss, forecourtDamage);
            if (pay <= 0)
            {
                CarSalesServices.UI?.Notify(state.Insurance == InsuranceTier.None ? $"Not insured: you pay the {UIFactory.Money(loss)} {what} yourself." : $"Your policy doesn't cover {what}.");
                return 0;
            }
            Eco.AddMoney(pay, TxCategory.Insurance, $"Insurance claim - {what}");
            state.claimDays.Add(Today);
            CarSalesServices.UI?.Toast(ToastType.Success, "Insurance claim", $"The insurer paid {UIFactory.Money(pay)} for the {what} (excess {UIFactory.Money(BusinessRules.Excess(state.Insurance))}).");
            OnChanged?.Invoke();
            return pay;
        }

        // ------------------------------------------------------------------ MARKETING
        public IEnumerable<Campaign> Running => state.campaigns.Where(c => c.untilDay >= Today).Select(c => BusinessRules.CampaignById(c.id)).Where(c => c != null);

        public void StartCampaign(string id, int days)
        {
            var c = BusinessRules.CampaignById(id);
            if (c == null) return;
            var a = state.campaigns.Find(x => x.id == id);
            if (a == null) { a = new ActiveCampaign { id = id }; state.campaigns.Add(a); }
            a.untilDay = Math.Max(a.untilDay, Today - 1) + days;
            CarSalesServices.UI.Success("Campaign", $"{c.Name} runs until day {a.untilDay} ({UIFactory.Money(c.CostPerDay)}/day, charged each morning).");
            OnChanged?.Invoke();
        }

        public void StopCampaign(string id) { state.campaigns.RemoveAll(x => x.id == id); OnChanged?.Invoke(); }

        public float FootfallFactor
        {
            get
            {
                var crm = CarSalesServices.Crm;
                float stars = crm ? BusinessRules.StarsFootfall(crm.AverageStars, crm.State.reviews.Count) : 1f;
                return (1f + BusinessRules.Combined(Running.Select(c => c.Footfall))) * stars;
            }
        }

        public float LeadsFactor => 1f + BusinessRules.Combined(Running.Select(c => c.Leads));

        /// <summary>A campaign may bring its kind of buyer (e.g. radio → luxury buyers).</summary>
        public PersonaId? CampaignPersona(System.Random r)
        {
            foreach (var c in Running) if (c.Attracts.HasValue && r.NextDouble() < c.Footfall * 1.5f) return c.Attracts;
            return null;
        }

        // ------------------------------------------------------------------ LEVELS
        public BusinessRules.LevelInputs Inputs() => new BusinessRules.LevelInputs
        {
            carsSold = CarSalesServices.Progress ? CarSalesServices.Progress.Stats.carsSold : state.sales.Count,
            reputation = Eco.Reputation,
            staff = CarSalesServices.Staff ? CarSalesServices.Staff.Employees.Count : 0,
            dealerships = DealershipManager.All.Count(d => d && d.IsOwned),
            workshopJobs = CarSalesServices.Workshop ? CarSalesServices.Workshop.JobsDone : 0,
            netWorth = CarSalesServices.Progress ? CarSalesServices.Progress.NetWorth : Eco.Money,
        };

        public (int level, string next) LevelInfo() => BusinessRules.Level(Inputs());

        public int MaxStaff => BusinessRules.MaxStaff(state.level);

        void CheckLevel()
        {
            var (level, _) = LevelInfo();
            if (level <= state.level) return;
            state.level = level;
            string msg = $"AUTO WORLD is now a {BusinessRules.LevelNames[level]} (level {level}). Staff slots: {BusinessRules.MaxStaff(level)}" +
                         (level >= 5 ? ". Luxury buyers now visit all your dealerships." : ".");
            CarSalesServices.UI?.Toast(ToastType.Success, "Level up!", msg);
            if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Goal, "Dealership level up", msg);
        }

        // ------------------------------------------------------------------ PROPERTY DEVELOPMENT
        public int DevelopmentLevel(string id) => state.developments.Find(d => d.id == id)?.level ?? 0;
        public float IncomeFactor(string id) => BusinessRules.IncomeFactor(DevelopmentLevel(id));

        public bool Develop(PropertyInfo p)
        {
            int lvl = DevelopmentLevel(p.id);
            if (lvl >= BusinessRules.MaxDevelopment) return false;
            int cost = BusinessRules.DevelopmentCost(p.price, lvl);
            if (!Eco.TrySpend(cost, TxCategory.PropertyDevelopment, $"Develop {p.displayName} (level {lvl + 1})")) { CarSalesServices.UI.Error($"You need {UIFactory.Money(cost)}."); return false; }
            var d = state.developments.Find(x => x.id == p.id);
            if (d == null) { d = new PropertyLevel { id = p.id }; state.developments.Add(d); }
            d.level++;
            CarSalesServices.UI.Success("Developed", $"{p.displayName} is now level {d.level}: earns {UIFactory.Money(CarSalesServices.Properties.IncomeOf(p))} a night.");
            OnChanged?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ DAILY / WEEKLY
        void OnDayStarted(DayStarted e)
        {
            // Overdraft interest and record.
            if (Eco.Money < 0)
            {
                state.overdrawnDays.Add(e.Day);
                int interest = BusinessRules.OverdraftInterest(Eco.Money);
                if (interest > 0) Eco.ForceSpend(interest, TxCategory.LoanInterest, "Overdraft interest");
            }
            // Utilities per owned dealership.
            int utilities = DealershipManager.All.Where(d => d && d.IsOwned).Sum(d => BusinessRules.Utilities(d.spots.Count));
            if (utilities > 0) { Eco.ForceSpend(utilities, TxCategory.Utilities, "Utilities (electricity, water, heating)"); CarSalesServices.DayCycle?.RegisterExpense(utilities); }
            // Marketing
            foreach (var c in Running.ToList())
            {
                Eco.ForceSpend(c.CostPerDay, TxCategory.Marketing, $"Campaign - {c.Name}");
                CarSalesServices.DayCycle?.RegisterExpense(c.CostPerDay);
            }
            state.campaigns.RemoveAll(c => c.untilDay < e.Day);
            // VAT overdue
            foreach (var r in state.vatReturns.Where(x => !x.paid && x.fine == 0 && e.Day > x.dueDay))
            {
                r.fine = BusinessRules.VatFine(r.Due);
                Eco.ChangeReputation(-1f);
                CarSalesServices.UI?.Toast(ToastType.Warning, "VAT overdue", $"Q{r.quarter} VAT is late: a fine of {UIFactory.Money(r.fine)} was added.");
            }
            Recalculate();
            CheckLevel();
            OnChanged?.Invoke();
        }

        void OnWeekEnded(WeekEnded w)
        {
            int rates = DealershipManager.All.Where(d => d && d.IsOwned).Sum(d => BusinessRules.BusinessRates(d.spots.Count));
            if (rates > 0) Eco.ForceSpend(rates, TxCategory.BusinessRates, "Business rates");
            int premium = WeeklyPremium(state.Insurance);
            if (premium > 0) Eco.ForceSpend(premium, TxCategory.Insurance, $"Insurance premium ({state.Insurance})");
            if (state.security) Eco.ForceSpend(BusinessRules.SecurityWeekly, TxCategory.Security, "CCTV & alarm monitoring");
            OnChanged?.Invoke();
        }
    }
}
