using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    [Serializable]
    public class StaffState
    {
        public int version = 1;
        public List<Employee> employees = new List<Employee>();
        public List<Employee> candidates = new List<Employee>();
        public int candidatesWeek = -1;
        public float minMarginPct = 10f;          // salespeople never sell below cost + this
        public bool salesHandleCustomers = true;  // salespeople talk to customers you don't reach in time
        public int nextId = 1;
    }

    /// <summary>
    /// Staff (spec §6.11): hire from weekly candidates, pay wages every week (ledger: Wages), sick days and morale.
    /// Salespeople close deals with customers you don't reach, mechanics work the workshop board, detailers clean the
    /// cars on your lots, drivers bring paid auction cars to your dealership. Saved as module "staff".
    /// </summary>
    public class StaffManager : MonoBehaviour
    {
        public const float SalespersonDelay = 10f;   // seconds an interested customer waits for you
        StaffState state = new StaffState();
        readonly System.Random rng = new System.Random();
        float lastHour = -1f;
        readonly Dictionary<string, float> busyUntil = new Dictionary<string, float>();

        public StaffState State => state;
        public IReadOnlyList<Employee> Employees => state.employees;
        public event Action OnChanged;

        void Awake() => CarSalesServices.Staff = this;
        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
            EventBus.Subscribe<WeekEnded>(OnWeekEnded);
        }
        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            EventBus.Unsubscribe<WeekEnded>(OnWeekEnded);
        }
        void Start()
        {
            if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.OnTimeChanged += OnTime;
            EnsureCandidates();
        }
        void OnDestroy() { if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.OnTimeChanged -= OnTime; }

        public void SetState(StaffState s)
        {
            state = s ?? new StaffState();
            // Phase 5b's single mechanic becomes a member of staff.
            var ws = CarSalesServices.Workshop;
            if (ws && ws.MechanicHired)
            {
                ws.FireMechanicQuietly();
                state.employees.Add(new Employee { id = "e" + state.nextId++, name = "Your mechanic", role = (int)StaffRole.Mechanic, skill = 65,
                                                   wage = StaffRules.Wage(StaffRole.Mechanic, 65), hiredDay = CarRecords.Today });
            }
            EnsureCandidates();
            OnChanged?.Invoke();
        }

        public void ResetState() => SetState(new StaffState());

        void Changed() => OnChanged?.Invoke();

        // ------------------------------------------------------------------ HIRING
        void EnsureCandidates()
        {
            int week = GameCalendar.WeekOf(CarRecords.Today);
            if (state.candidatesWeek == week && state.candidates.Count > 0) return;
            state.candidates = StaffRules.Candidates(week, new System.Random(week * 7919 + 13));
            state.candidatesWeek = week;
        }

        public bool Hire(Employee candidate)
        {
            if (!state.candidates.Contains(candidate)) return false;
            var biz = CarSalesServices.Business;
            if (biz && state.employees.Count >= biz.MaxStaff)
            { CarSalesServices.UI.Error($"A {CarSales.Sim.BusinessRules.LevelNames[biz.Level].ToLower()} has room for {biz.MaxStaff} staff. Grow the business (Business app) for more."); return false; }
            state.candidates.Remove(candidate);
            candidate.id = "e" + state.nextId++;
            candidate.hiredDay = CarRecords.Today;
            state.employees.Add(candidate);
            CarSalesServices.UI.Success("Hired", $"{candidate.name} starts today as your {StaffRules.RoleName(candidate.Role).ToLower()} ({UIFactory.Money(candidate.wage)}/week).");
            Changed();
            return true;
        }

        public void Fire(Employee e)
        {
            if (!state.employees.Remove(e)) return;
            // A week's notice pay.
            CarSalesServices.Economy.ForceSpend(e.wage, TxCategory.Wages, $"Notice pay - {e.name}");
            CarSalesServices.UI.Notify($"{e.name} has left (one week's notice pay).");
            Changed();
        }

        public void SetMinMargin(float pct) { state.minMarginPct = Mathf.Clamp(pct, 0f, 50f); Changed(); }
        public void SetSalesHandle(bool on) { state.salesHandleCustomers = on; Changed(); }

        static bool WorkingHours(float h) => h >= BoardLogic.WorkStart && h < BoardLogic.WorkEnd;

        public IEnumerable<Employee> Working(StaffRole role) =>
            state.employees.Where(e => e.Role == role && e.sickDay != CarRecords.Today);

        /// <summary>Best available mechanic skill (0 = none at work). Used by the workshop board.</summary>
        public int MechanicSkill => Working(StaffRole.Mechanic).Select(e => e.skill).DefaultIfEmpty(0).Max();
        public int MechanicCount => Working(StaffRole.Mechanic).Count();
        /// <summary>A manager at work speeds everyone up by 10%.</summary>
        public float Productivity => Working(StaffRole.Manager).Any() ? 1.1f : 1f;

        public void MechanicFinishedJob()
        {
            foreach (var e in Working(StaffRole.Mechanic)) StaffRules.GainExperience(e, 0.4f);
        }

        // ------------------------------------------------------------------ SALESPEOPLE
        /// <summary>A free salesperson for an interested customer, or null.</summary>
        public Employee FreeSalesperson()
        {
            if (!state.salesHandleCustomers || !WorkingHours(CarSalesServices.DayCycle ? CarSalesServices.DayCycle.HourOfDay : 9f)) return null;
            return Working(StaffRole.Salesperson).Where(e => !busyUntil.TryGetValue(e.id, out var t) || Time.time >= t)
                                                 .OrderByDescending(e => e.skill).FirstOrDefault();
        }

        /// <summary>
        /// The salesperson talks to the customer about their car: tells them about known faults (staff are honest),
        /// then closes at the best price they can above your floor (cost + minimum margin).
        /// </summary>
        public bool HandleCustomer(CustomerAI customer, Employee sp)
        {
            if (customer == null || sp == null || !customer.TargetCar) return false;
            busyUntil[sp.id] = Time.time + 25f;
            var car = customer.TargetCar.Data;
            var quote = CustomerDeal.Quote(customer.Profile, customer.TargetCar, disclose: true);
            int floor = Mathf.RoundToInt((car.purchasePrice + car.repairSpent) * (1f + state.minMarginPct / 100f));
            int price = StaffRules.SalespersonDeal(quote.opening, quote.max, floor, sp.skill);
            StaffRules.GainExperience(sp, 0.25f);
            if (price <= 0)
            {
                customer.EndNegotiation(false);
                CarSalesServices.UI.Notify($"{sp.name}: {customer.Profile.customerName} wouldn't pay enough for the {customer.TargetCar.Definition.displayName}.");
                Changed();
                return false;
            }
            int known = InspectionEngine.KnownCount(car.faults);
            CustomerDeal.CompleteSale(customer, price, quote.marketValue, disclosed: known, hiddenKnown: 0, rounds: 1, soldBy: sp.name, tradeIn: null, tradeInCredit: 0, finance: false);
            Changed();
            return true;
        }

        // ------------------------------------------------------------------ CLOCK
        void OnTime(float hour)
        {
            if (lastHour < 0f || hour < lastHour) { lastHour = hour; return; }
            float from = lastHour;
            lastHour = hour;
            float hours = BoardLogic.WorkingHours(from % 24f, from % 24f + (hour - from));
            if (hours <= 0f) return;
            Detail(hours);
            Drive(hours);
        }

        float driveAccum;

        /// <summary>Detailers clean the dirtiest car on your lots.</summary>
        void Detail(float hours)
        {
            var detailers = Working(StaffRole.Detailer).ToList();
            if (detailers.Count == 0 || !CarSalesServices.Inventory) return;
            foreach (var d in detailers)
            {
                var car = CarSalesServices.Inventory.OwnedCars.Where(c => c.location == CarLocation.Spot && c.cleanliness < 85f)
                                                              .OrderBy(c => c.cleanliness).FirstOrDefault();
                if (car == null) return;
                car.cleanliness = Mathf.Min(100f, car.cleanliness + StaffRules.DetailPerHour(d.skill) * hours * Productivity);
                var disp = CarSalesServices.Vehicles ? CarSalesServices.Vehicles.GetDisplay(car.instanceId) : null;
                if (disp) disp.Refresh();
                StaffRules.GainExperience(d, 0.1f * hours);
            }
        }

        /// <summary>Drivers bring paid auction cars from the car park to a free spot at your dealerships (one per 2 hours).</summary>
        void Drive(float hours)
        {
            var drivers = Working(StaffRole.Driver).ToList();
            var a = CarSalesServices.Auction;
            if (drivers.Count == 0 || !a) return;
            driveAccum += hours * drivers.Count;
            while (driveAccum >= 2f)
            {
                var inv = a.Invoices.FirstOrDefault(i => i.paid && !i.collected && !i.cancelled);
                if (inv == null) { driveAccum = 0f; return; }
                var disp = CarSalesServices.Vehicles.GetDisplay(inv.carId);
                var spot = DealershipManager.FindFreeSpotAnywhere(out var dealer);
                if (!disp || !spot) { driveAccum = 0f; return; }
                driveAccum -= 2f;
                CarSalesServices.Vehicles.PlaceAtSpot(disp, dealer, spot);
                inv.collected = true;
                var dr = drivers[0];
                StaffRules.GainExperience(dr, 0.3f);
                CarSalesServices.UI.Notify($"{dr.name} brought the {disp.Data.Name} to {dealer.displayName}.");
            }
        }

        void OnDayStarted(DayStarted e)
        {
            foreach (var emp in state.employees)
                emp.sickDay = rng.NextDouble() < StaffRules.SickChance(emp) ? e.Day : emp.sickDay;
            var sick = state.employees.Where(x => x.sickDay == e.Day).ToList();
            if (sick.Count > 0) CarSalesServices.UI?.Notify($"Off sick today: {string.Join(", ", sick.Select(x => x.name))}.");
            EnsureCandidates();
            Changed();
        }

        void OnWeekEnded(WeekEnded w)
        {
            int total = 0;
            foreach (var emp in state.employees)
            {
                CarSalesServices.Economy.ForceSpend(emp.wage, TxCategory.Wages, $"Wage - {emp.name} ({StaffRules.RoleName(emp.Role)})");
                total += emp.wage;
                StaffRules.UpdateMorale(emp, manager: Working(StaffRole.Manager).Any() && emp.Role != StaffRole.Manager);
            }
            if (total > 0)
            {
                if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.RegisterExpense(total);
                CarSalesServices.UI?.Notify($"Weekly wages paid: {UIFactory.Money(total)}.");
            }
            Changed();
        }

        public int WeeklyWages => state.employees.Sum(e => e.wage);
    }
}
