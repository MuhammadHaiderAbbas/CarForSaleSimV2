using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>Saved workshop data ("workshop" save module).</summary>
    [Serializable]
    public class WorkshopState
    {
        public int version = 1;
        public PartsInventory inventory = new PartsInventory();
        public List<WorkshopJob> jobs = new List<WorkshopJob>();
        public bool hasScanner;
        public float skill = 0.3f;
        // Phase 5b
        public List<BoardCard> board = new List<BoardCard>();
        public bool mechanicHired;
        public int jobsDone;   // phase 9: dealership levels
    }

    /// <summary>
    /// Phase 5: inspections, the history check, spare parts (three suppliers), repair jobs at the town garage or
    /// done yourself, MOT tests and paperwork. Faults get worse every night; orders arrive in the morning.
    /// Added automatically by <see cref="CarSalesBootstrap"/> if the systems prefab doesn't have it.
    /// </summary>
    public class WorkshopManager : MonoBehaviour
    {
        public const int V5cFee = 25, LocksmithFee = 150;
        public const float RoadTestKm = 1f;
        const float RepossessChancePerDay = 0.04f;

        WorkshopState state = new WorkshopState();
        float lastHour = -1f;
        System.Random rng = new System.Random();

        public PartsInventory Inventory => state.inventory;
        public IReadOnlyList<WorkshopJob> Jobs => state.jobs;
        public bool HasScanner => state.hasScanner;
        /// <summary>Your inspection skill (0-1): better at noticing faults, fewer botched DIY jobs. Grows with practice.</summary>
        public float Skill => state.skill;
        public event Action OnChanged;
        /// <summary>Hands-on inspections and repairs (markers on the car).</summary>
        public HandsOnSession Session { get; private set; }
        /// <summary>True while the parts counter in town is open (counter prices).</summary>
        public bool AtCounter { get; set; }
        public IReadOnlyList<BoardCard> Board => state.board;
        public bool MechanicHired => state.mechanicHired;

        void Awake()
        {
            CarSalesServices.Workshop = this;
            Session = GetComponent<HandsOnSession>();
            if (!Session) Session = gameObject.AddComponent<HandsOnSession>();
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        void Start()
        {
            if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.OnTimeChanged += OnTimeChanged;
            if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged += OnMenu;
        }

        void OnDestroy()
        {
            if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.OnTimeChanged -= OnTimeChanged;
            if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged -= OnMenu;
        }

        void OnMenu(bool open) { if (!open) AtCounter = false; }

        // ------------------------------------------------------------------ SAVE
        public WorkshopState GetState() => state;

        public void SetState(WorkshopState s)
        {
            state = s ?? new WorkshopState();
            if (state.inventory == null) state.inventory = new PartsInventory();
            if (state.jobs == null) state.jobs = new List<WorkshopJob>();
            if (state.board == null) state.board = new List<BoardCard>();
            if (Session && Session.Running) Session.Stop(false);
            state.skill = Mathf.Clamp(state.skill <= 0f ? 0.3f : state.skill, 0f, 0.95f);
            lastHour = -1f;
            OnChanged?.Invoke();
        }

        public void ResetState() => SetState(new WorkshopState());

        // ------------------------------------------------------------------ HELPERS
        static EconomyManager Eco => CarSalesServices.Economy;
        static int Today => CarRecords.Today;
        static UIManager UI => CarSalesServices.UI;

        static int BasePrice(CarInstance c)
        {
            var def = CarSalesServices.Database ? CarSalesServices.Database.Get(c.carId) : null;
            return def ? def.basePrice : 20000;
        }

        static void PassTime(float hours)
        {
            if (hours > 0f && CarSalesServices.DayCycle) CarSalesServices.DayCycle.AdvanceTime(hours);
        }

        void Practice(float amount) => state.skill = Mathf.Min(0.95f, state.skill + amount);

        bool Pay(int amount, TxCategory cat, string memo, CarInstance car)
        {
            if (amount <= 0) return true;
            if (!Eco.TrySpend(amount, cat, memo, car?.instanceId)) { UI.Error($"Not enough money ({UIFactory.Money(amount)})."); return false; }
            if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.RegisterExpense(amount);
            return true;
        }

        void Changed(CarInstance car)
        {
            if (car != null) CarParts.Recalculate(car);
            OnChanged?.Invoke();
            if (UI && UI.computerHub) UI.computerHub.RefreshCurrent();
        }

        // ------------------------------------------------------------------ INSPECTION
        public enum Context { Online, OnTheLot, Owned }

        public static Context ContextOf(CarInstance c, CarDisplay display)
        {
            if (CarRecords.IsOwned(c)) return Context.Owned;
            if (display || (CarSalesServices.Marketplace && CarSalesServices.Marketplace.LotListings.Contains(c))) return Context.OnTheLot;
            return Context.Online;
        }

        /// <summary>Whether a method can be used on this car right now, and why not.</summary>
        public bool CanUse(InspectMethod m, CarInstance c, Context ctx, out string reason)
        {
            reason = "";
            if (m == InspectMethod.History) return true;
            if (ctx == Context.Online) { reason = "Go and see the car, or pay for a full inspection"; return false; }
            switch (m)
            {
                case InspectMethod.Scanner:
                    if (!state.hasScanner) { reason = "Buy an OBD scanner (Workshop app)"; return false; }
                    return true;
                case InspectMethod.RoadTest:
                    if (ctx != Context.Owned) { reason = "Only cars you own can be driven"; return false; }
                    reason = c.knowledge.Done(InspectMethod.RoadTest) ? "" : $"Drive it ({c.knowledge.roadTestKm:0.0}/{RoadTestKm:0} km)";
                    return false;   // happens by driving
                case InspectMethod.Lift:
                    if (ctx != Context.Owned) { reason = "Buy it first - then book the lift"; return false; }
                    return true;
            }
            return true;
        }

        /// <param name="freeLift">On your own lift: no fee, and it works wherever the session allows it.</param>
        public InspectionResult Inspect(CarInstance c, InspectMethod m, Context ctx, bool freeLift = false, bool passTime = true)
        {
            if (!(freeLift && m == InspectMethod.Lift) && !CanUse(m, c, ctx, out string why)) { UI.Error(why); return null; }
            CarParts.EnsureParts(c);
            var info = InspectionEngine.Info(m);
            if (m == InspectMethod.History) { RunHistoryCheck(c); return null; }
            bool free = freeLift && m == InspectMethod.Lift;
            if (!free && info != null && info.Cost > 0 && !Pay(info.Cost, TxCategory.Inspection, $"{info.Name} - {c.Name}", c)) return null;
            var r = InspectionEngine.Run(m, c.components, c.faults, c.history, c.knowledge, state.skill, rng);
            if (passTime) PassTime((info?.Minutes ?? 0) / 60f);
            Practice(0.01f);
            Changed(c);
            return r;
        }

        /// <summary>Paid full inspection by the town garage (works on online listings too).</summary>
        public InspectionResult FullInspection(CarInstance c)
        {
            CarParts.EnsureParts(c);
            int fee = CarValueCalculator.InspectionCost();
            bool alreadyDone = c.inspected && c.knowledge.Done(InspectMethod.Lift);
            if (!alreadyDone && !Pay(fee, TxCategory.Inspection, $"Full inspection - {c.Name}", c)) return null;
            var r = InspectionEngine.Full(c.components, c.faults, c.history, c.knowledge, rng);
            c.inspected = true;
            Changed(c);
            return r;
        }

        public List<string> RunHistoryCheck(CarInstance c)
        {
            CarParts.EnsureParts(c);
            if (!c.history.checkedHistory && !Pay(InspectionEngine.HistoryCheckCost, TxCategory.Paperwork, $"History check - {c.Name}", c)) return null;
            var lines = InspectionEngine.HistoryCheck(c.documents, c.history, c.mileage, Today, c.knowledge);
            Changed(c);
            return lines;
        }

        public bool BuyScanner()
        {
            if (state.hasScanner) return true;
            if (!Pay(InspectionEngine.ScannerPrice, TxCategory.Parts, "OBD scanner", null)) return false;
            state.hasScanner = true;
            UI.Success("OBD scanner", "You can now read fault codes on any car you can get into.");
            Changed(null);
            return true;
        }

        // Road test: distance driven in a car counts towards its road test.
        Vector3 lastPos;
        CarDisplay lastCar;

        void Update()
        {
            var drive = CarSalesServices.TestDrive;
            if (!drive || !drive.IsDriving || !drive.CurrentCar) { lastCar = null; return; }
            var car = drive.CurrentCar;
            if (car != lastCar) { lastCar = car; lastPos = car.transform.position; return; }
            float d = Vector3.Distance(car.transform.position, lastPos) / 1000f;
            lastPos = car.transform.position;
            if (d <= 0f || d > 0.1f) return;   // teleports don't count
            var c = car.Data;
            if (c == null || c.knowledge == null || c.knowledge.Done(InspectMethod.RoadTest)) return;
            c.knowledge.roadTestKm += d;
            if (c.knowledge.roadTestKm < RoadTestKm) return;
            CarParts.EnsureParts(c);
            var r = InspectionEngine.Run(InspectMethod.RoadTest, c.components, c.faults, c.history, c.knowledge, state.skill, rng);
            Practice(0.01f);
            if (r.found.Count > 0)
                UI.Toast(ToastType.Warning, "Road test", "You notice: " + string.Join("; ", r.found.Select(f => f.Def.Symptom.ToLowerInvariant())));
            else UI.Notify($"Road test done: the {c.Name} drives fine.");
            Changed(c);
        }

        // ------------------------------------------------------------------ PARTS
        public int PartPrice(string part, PartTier tier, Supplier s, CarInstance c) =>
            Mathf.RoundToInt(WorkshopCatalog.PartPrice(part, tier, s, c != null ? BasePrice(c) : 20000) * RepairCostFactor);

        /// <summary>Phase 10 difficulty: parts and garage labour cost more on harder settings.</summary>
        public static float RepairCostFactor => CarSalesServices.Business ? CarSalesServices.Business.Balance.repairCostMultiplier : 1f;

        /// <summary>Town-garage labour for a job (difficulty applied).</summary>
        public static int LabourCost(RepairJobDef job) => Mathf.RoundToInt(WorkshopCatalog.LabourCost(job) * RepairCostFactor);

        /// <summary>Parts still missing for a job (in that tier).</summary>
        public List<(string part, int count)> Missing(RepairJobDef job, PartTier tier)
        {
            var list = new List<(string, int)>();
            foreach (var (part, count) in job.Parts)
            {
                int have = state.inventory.Count(part, tier);
                if (have < count) list.Add((part, count - have));
            }
            return list;
        }

        public bool Available(Supplier s, string part, PartTier tier)
        {
            var def = WorkshopCatalog.Part(part);
            if (def == null || !WorkshopCatalog.Sells(s, def, tier)) return false;
            return s != Supplier.BreakersYard || WorkshopCatalog.BreakerHasToday(part, Today);
        }

        /// <summary>Buys the missing parts for a job. Same-day suppliers deliver at once; online arrives next morning.</summary>
        public bool OrderParts(CarInstance c, RepairJobDef job, PartTier tier, Supplier s)
        {
            var missing = Missing(job, tier);
            if (missing.Count == 0) { UI.Notify("You already have those parts."); return true; }
            foreach (var (part, _) in missing)
                if (!Available(s, part, tier)) { UI.Error($"{SupplierName(s)} doesn't have {WorkshopCatalog.Part(part).Name} ({tier}) today."); return false; }
            int total = missing.Sum(m => PartPrice(m.part, tier, s, c) * m.count);
            if (!Pay(total, TxCategory.Parts, $"Parts for {job.Name} ({tier}, {SupplierName(s)})", c)) return false;
            int lead = WorkshopCatalog.LeadDays(s);
            foreach (var (part, count) in missing)
            {
                if (lead == 0) state.inventory.Add(part, tier, count);
                else state.inventory.orders.Add(new PartOrder { part = part, tier = (int)tier, count = count, arrivesDay = Today + lead, supplier = (int)s });
            }
            UI.Success("Parts ordered", lead == 0 ? $"{job.Name}: parts are in your storage." : $"{job.Name}: parts arrive tomorrow morning.");
            Changed(c);
            return true;
        }

        public static string SupplierName(Supplier s) => s == Supplier.MotorFactor ? "Motor factor" : s == Supplier.Online ? "Online" : "Breaker's yard";

        // ------------------------------------------------------------------ JOBS
        public WorkshopJob JobOn(CarInstance c) => state.jobs.Find(j => j.carId == c.instanceId);

        /// <summary>The town garage fits the parts: you pay labour, the car is done after the labour hours.</summary>
        public bool StartGarageJob(CarInstance c, RepairJobDef job, PartTier tier)
        {
            if (JobOn(c) != null) { UI.Error("The garage is already working on this car."); return false; }
            if (!state.inventory.HasPartsFor(job, tier)) { UI.Error("Order the parts first."); return false; }
            int labour = LabourCost(job);
            if (!Pay(labour, TxCategory.Labour, $"Labour: {job.Name} - {c.Name}", c)) return false;
            state.inventory.TakePartsFor(job, tier);
            state.jobs.Add(new WorkshopJob { job = job.Id, carId = c.instanceId, tier = (int)tier, hoursLeft = job.Hours, hoursTotal = job.Hours, labourCost = labour, startedDay = Today });
            UI.Success("Job booked", $"{job.Name} on the {c.Name}: ready in about {job.Hours:0.#} h.");
            Changed(c);
            return true;
        }

        /// <summary>Do it yourself: no labour bill, the time passes now, a small chance to botch it (less with skill).</summary>
        public bool DoItYourself(CarInstance c, RepairJobDef job, PartTier tier)
        {
            if (JobOn(c) != null) { UI.Error("The garage is working on this car."); return false; }
            if (!state.inventory.TakePartsFor(job, tier)) { UI.Error("Order the parts first."); return false; }
            bool botched = rng.NextDouble() < WorkshopCatalog.DiyFailChance(state.skill);
            int fixedFaults = WorkshopCatalog.Apply(job, c.components, c.faults, tier, rng, botched);
            Spend(c, job, tier, 0);
            PassTime(job.Hours * 1.2f);
            Practice(0.02f);
            MarkCardDone(c.instanceId, job.Id);
            if (botched) UI.Toast(ToastType.Warning, "Not quite right", $"{job.Name}: it works, but you didn't fit it perfectly.");
            else UI.Success("Job done", $"{job.Name} finished{(fixedFaults > 0 ? $", {fixedFaults} fault(s) fixed" : "")}.");
            Changed(c);
            return true;
        }

        // ------------------------------------------------------------------ HANDS-ON (your own bay)
        /// <summary>Starts doing a job by hand. The car must stand in a matching bay of your service centre.</summary>
        public bool StartHandsOnRepair(CarInstance c, RepairJobDef job, PartTier tier)
        {
            var display = CarSalesServices.Vehicles ? CarSalesServices.Vehicles.GetDisplay(c.instanceId) : null;
            var types = WorkStation.StationsFor(job);
            if (!WorkStation.Exists(StationType.Mechanical)) { UI.Error("You have no service centre yet (run Build Everything to add it to AUTO WORLD)."); return false; }
            if (!display || !WorkStation.BayOf(display, types)) { UI.Error($"Drive the {c.Name} into your {(types[0] == StationType.Tyres ? "tyre or mechanical" : "mechanical")} bay at the AUTO WORLD service centre first."); return false; }
            if (JobOn(c) != null) { UI.Error("The garage is working on this car."); return false; }
            if (!state.inventory.TakePartsFor(job, tier)) { UI.Error("Order the parts first."); return false; }
            Session.StartRepair(display, job, tier);
            return true;
        }

        public void ReturnParts(RepairJobDef job, PartTier tier)
        {
            foreach (var (part, count) in job.Parts) state.inventory.Add(part, tier, count);
            OnChanged?.Invoke();
        }

        public void CompleteHandsOn(CarInstance c, RepairJobDef job, PartTier tier)
        {
            if (c == null || job == null) return;
            bool botched = rng.NextDouble() < WorkshopCatalog.DiyFailChance(state.skill) * HandsOn.RiskFactor;
            int fixedFaults = WorkshopCatalog.Apply(job, c.components, c.faults, tier, rng, botched);
            Spend(c, job, tier, 0);
            PassTime(job.Hours * HandsOn.TimeFactor);
            Practice(0.03f);
            MarkCardDone(c.instanceId, job.Id);
            if (botched) UI.Toast(ToastType.Warning, "Not quite right", $"{job.Name}: it works, but something isn't seated perfectly.");
            else UI.Success("Done by hand", $"{job.Name} on the {c.Name}{(fixedFaults > 0 ? $": {fixedFaults} fault(s) fixed" : "")}. It took {job.Hours * HandsOn.TimeFactor:0.#} h.");
            Changed(c);
        }

        public MotRecord RunMotAtBay(CarInstance c)
        {
            CarParts.EnsureParts(c);
            var rec = MotTest.Run(c.components, c.faults, c.mileage, Today, VehicleRecords.CurrentYear(Today));
            c.documents.motHistory.Add(rec);
            if (rec.passed) c.documents.motExpiryDay = Today + VehicleRecords.MotValidDays;
            PassTime(0.75f);
            Changed(c);
            string body = rec.passed ? $"PASS. Valid for {VehicleRecords.MotValidDays} days." : "FAIL:\n- " + string.Join("\n- ", rec.fails);
            if (rec.advisories.Count > 0) body += "\n\nAdvisories:\n- " + string.Join("\n- ", rec.advisories);
            UIFactory.Confirm($"MOT - {c.Name}", body, "OK", rec.passed ? UIFactory.Good : UIFactory.Bad, null);
            return rec;
        }

        // ------------------------------------------------------------------ BOARD & MECHANIC
        public BoardCard AddToBoard(CarInstance c, RepairJobDef job, PartTier tier)
        {
            var existing = state.board.Find(b => !b.done && b.carId == c.instanceId && b.job == job.Id);
            if (existing != null) { existing.tier = (int)tier; Changed(c); return existing; }
            var card = new BoardCard { id = Guid.NewGuid().ToString("N").Substring(0, 8), job = job.Id, carId = c.instanceId, tier = (int)tier };
            state.board.Add(card);
            UI.Notify($"Added to the workshop board: {job.Name} ({c.Name}).");
            Changed(c);
            return card;
        }

        public void RemoveCard(BoardCard card)
        {
            if (card == null || card.byMechanic) return;
            state.board.Remove(card);
            Changed(null);
        }

        public BoardColumn ColumnOf(BoardCard card)
        {
            var running = state.jobs.Find(j => j.carId == card.carId && j.job == card.job);
            return BoardLogic.ColumnOf(card, state.inventory, running != null);
        }

        public int JobsDone => state.jobsDone;

        void MarkCardDone(string carId, string jobId)
        {
            state.jobsDone++;
            var card = state.board.Find(b => !b.done && b.carId == carId && b.job == jobId);
            if (card == null) return;
            card.done = true;
            card.byMechanic = false;
            card.doneDay = Today;
        }

        public bool HireMechanic()
        {
            if (state.mechanicHired) return true;
            state.mechanicHired = true;
            UI.Success("Mechanic hired", $"They work {BoardLogic.WorkStart:00}:00-{BoardLogic.WorkEnd:00}:00 on the board's ready jobs, for {UIFactory.Money(BoardLogic.MechanicWage)} a day.");
            Changed(null);
            return true;
        }

        /// <summary>Phase 7: the single mechanic moves to the staff system.</summary>
        public void FireMechanicQuietly() { state.mechanicHired = false; }

        public void FireMechanic()
        {
            state.mechanicHired = false;
            foreach (var b in state.board) if (b.byMechanic) { b.byMechanic = false; ReturnPartsFor(b); }
            UI.Notify("The mechanic has left.");
            Changed(null);
        }

        void ReturnPartsFor(BoardCard b)
        {
            var job = WorkshopCatalog.Job(b.job);
            if (job != null && b.hoursLeft >= 0f) ReturnParts(job, (PartTier)b.tier);
            b.hoursLeft = -1f;
        }

        /// <summary>Your mechanic works through the Ready column during working hours at your mechanical bay.</summary>
        void MechanicWork(float fromHour, float toHour)
        {
            var staff = CarSalesServices.Staff;
            bool staffMech = staff && staff.MechanicCount > 0;
            if ((!state.mechanicHired && !staffMech) || !WorkStation.Exists(StationType.Mechanical)) return;
            float mechSkill = staffMech ? staff.MechanicSkill / 100f : BoardLogic.MechanicSkill;
            float hours = BoardLogic.WorkingHours(fromHour, toHour) * (staffMech ? StaffRules.MechanicSpeed(staff.MechanicSkill) * staff.MechanicCount * staff.Productivity : 1f);
            while (hours > 0f)
            {
                var card = state.board.Find(b => b.byMechanic && !b.done);
                if (card == null)
                {
                    card = state.board.Find(b => !b.done && ColumnOf(b) == BoardColumn.Ready && JobOn(Car(b.carId)) == null && Car(b.carId) != null);
                    if (card == null) return;
                    var job = WorkshopCatalog.Job(card.job);
                    if (!state.inventory.TakePartsFor(job, (PartTier)card.tier)) return;
                    card.byMechanic = true;
                    card.hoursLeft = job.Hours;
                }
                float work = Mathf.Min(hours, card.hoursLeft);
                card.hoursLeft -= work;
                hours -= work;
                if (card.hoursLeft > 0.0001f) return;
                var c = Car(card.carId);
                var j = WorkshopCatalog.Job(card.job);
                if (c != null && j != null)
                {
                    bool botched = rng.NextDouble() < WorkshopCatalog.DiyFailChance(mechSkill) * HandsOn.RiskFactor;
                    if (staffMech) staff.MechanicFinishedJob();
                    int fixedFaults = WorkshopCatalog.Apply(j, c.components, c.faults, (PartTier)card.tier, rng, botched);
                    Spend(c, j, (PartTier)card.tier, 0);
                    UI.Success("Your mechanic", $"{j.Name} on the {c.Name} is done{(fixedFaults > 0 ? $" ({fixedFaults} fault(s) fixed)" : "")}{(botched ? " - but it's not perfect" : "")}.");
                    CarParts.Recalculate(c);
                }
                card.hoursLeft = -1f;
                MarkCardDone(card.carId, card.job);
                OnChanged?.Invoke();
            }
        }

        static CarInstance Car(string id) => CarSalesServices.Inventory ? CarSalesServices.Inventory.OwnedCars.Find(x => x.instanceId == id) : null;

        void Spend(CarInstance c, RepairJobDef job, PartTier tier, int labour)
        {
            // Parts were paid when ordered; book their value against the car so its costs add up.
            int parts = WorkshopCatalog.PartsPrice(job, tier, Supplier.MotorFactor, BasePrice(c));
            c.repairSpent += parts + labour;
            if (c.state == CarState.NeedsRepair && c.condition >= CarValueCalculator.RepairThreshold) c.state = CarState.Ready;
        }

        void OnTimeChanged(float hour)
        {
            if (lastHour < 0f || hour < lastHour) { lastHour = hour; return; }
            float delta = hour - lastHour;
            float from = lastHour;
            lastHour = hour;
            if (delta <= 0f) return;
            MechanicWork(from, hour);
            if (state.jobs.Count == 0) return;
            for (int i = state.jobs.Count - 1; i >= 0; i--)
            {
                var j = state.jobs[i];
                j.hoursLeft -= delta;
                if (j.hoursLeft > 0f) continue;
                state.jobs.RemoveAt(i);
                Complete(j);
            }
        }

        void Complete(WorkshopJob j)
        {
            var c = CarSalesServices.Inventory.OwnedCars.Find(x => x.instanceId == j.carId);
            var job = WorkshopCatalog.Job(j.job);
            if (c == null || job == null) return;
            CarParts.EnsureParts(c);
            int fixedFaults = WorkshopCatalog.Apply(job, c.components, c.faults, (PartTier)j.tier, rng);
            Spend(c, job, (PartTier)j.tier, j.labourCost);
            if (c.documents.service != ServiceHistory.None || job.Id == "oil_service")
            {
                if (job.Id == "oil_service") { c.documents.serviceStamps++; c.documents.lastServiceKm = c.mileage; if (c.documents.service == ServiceHistory.None) c.documents.service = ServiceHistory.Partial; }
            }
            MarkCardDone(c.instanceId, job.Id);
            UI.Success("Garage: job done", $"{job.Name} on the {c.Name} is finished{(fixedFaults > 0 ? $" ({fixedFaults} fault(s) fixed)" : "")}.");
            Changed(c);
        }

        // ------------------------------------------------------------------ MOT & PAPERWORK
        public MotRecord RunMot(CarInstance c)
        {
            CarParts.EnsureParts(c);
            if (!Pay(MotTest.Fee, TxCategory.Paperwork, $"MOT test - {c.Name}", c)) return null;
            var rec = MotTest.Run(c.components, c.faults, c.mileage, Today, VehicleRecords.CurrentYear(Today));
            c.documents.motHistory.Add(rec);
            if (rec.passed) c.documents.motExpiryDay = Today + VehicleRecords.MotValidDays;
            PassTime(1f);
            Changed(c);
            return rec;
        }

        public bool OrderLogbook(CarInstance c)
        {
            if (c.documents.v5cPresent || c.documents.v5cArrivesDay > 0) return false;
            if (!Pay(V5cFee, TxCategory.Paperwork, $"Replacement logbook - {c.Name}", c)) return false;
            c.documents.v5cArrivesDay = Today + VehicleRecords.V5cReplacementDays;
            UI.Success("Logbook ordered", $"The replacement V5C arrives on day {c.documents.v5cArrivesDay}.");
            Changed(c);
            return true;
        }

        public bool CutKey(CarInstance c)
        {
            if (c.documents.keys >= 2) return false;
            if (!Pay(LocksmithFee, TxCategory.Paperwork, $"Locksmith: new key - {c.Name}", c)) return false;
            c.documents.keys++;
            UI.Success("Locksmith", $"The {c.Name} now has {c.documents.keys} key{(c.documents.keys == 1 ? "" : "s")}.");
            Changed(c);
            return true;
        }

        public int FinanceOwed(CarInstance c) =>
            c.history.outstandingFinance && !c.history.financeSettled ? Mathf.RoundToInt(CarValueCalculator.MarketValue(c) * c.history.financeShare / 10f) * 10 : 0;

        public bool SettleFinance(CarInstance c)
        {
            int owed = FinanceOwed(c);
            if (owed <= 0) return false;
            if (!Pay(owed, TxCategory.FinanceSettlement, $"Finance settlement - {c.Name}", c)) return false;
            c.history.financeSettled = true;
            UI.Success("Finance settled", $"The {c.Name} is now fully yours.");
            Changed(c);
            return true;
        }

        // ------------------------------------------------------------------ DAILY
        void OnDayStarted(DayStarted e)
        {
            foreach (var o in state.inventory.Receive(e.Day))
                UI?.Notify($"Parts delivered: {o.count} x {WorkshopCatalog.Part(o.part)?.Name ?? o.part} ({(PartTier)o.tier}).");

            BoardLogic.ClearOldDone(state.board, e.Day);
            if (state.mechanicHired)
            {
                if (!Eco.TrySpend(BoardLogic.MechanicWage, TxCategory.Wages, "Mechanic's wage"))
                {
                    UI?.Toast(ToastType.Warning, "Mechanic quit", "You couldn't pay your mechanic's wage, so they left.");
                    FireMechanic();
                }
                else if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.RegisterExpense(BoardLogic.MechanicWage);
            }
            var inv = CarSalesServices.Inventory;
            if (!inv) return;
            state.board.RemoveAll(b => !b.done && !b.byMechanic && Car(b.carId) == null);
            foreach (var c in inv.OwnedCars.ToList())
            {
                CarParts.EnsureParts(c);
                foreach (var f in FaultModel.Progress(c.components, c.faults, 1))
                    if (f.known) UI?.Toast(ToastType.Warning, "Getting worse", $"{c.Name}: {f.Def.Name} is now severe.");
                CarParts.Recalculate(c);
                var d = c.documents;
                if (d.v5cArrivesDay > 0 && e.Day >= d.v5cArrivesDay)
                {
                    d.v5cPresent = true; d.v5cArrivesDay = 0;
                    UI?.Notify($"The replacement logbook for the {c.Name} has arrived.");
                }
                if (VehicleRecords.NeedsMot(CarRecords.ModelYear(c), e.Day) && d.motExpiryDay == e.Day)
                    UI?.Toast(ToastType.Warning, "MOT expired", $"The {c.Name}'s MOT ran out today. Book a test in the Workshop app.");
                // Outstanding finance: the lender may take the car back.
                if (!c.personal && c.history.outstandingFinance && !c.history.financeSettled && rng.NextDouble() < RepossessChancePerDay)
                    Repossess(c);
            }
            OnChanged?.Invoke();
        }

        void Repossess(CarInstance c)
        {
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving && CarSalesServices.TestDrive.CurrentCar && CarSalesServices.TestDrive.CurrentCar.Data == c)
                CarSalesServices.TestDrive.EndDrive();
            state.jobs.RemoveAll(j => j.carId == c.instanceId);
            CarSalesServices.Vehicles.Despawn(c.instanceId);
            CarSalesServices.Inventory.Remove(c.instanceId);
            UI?.Toast(ToastType.Warning, "Car repossessed!",
                $"A finance company took the {c.Name}: it still had outstanding finance. A history check would have shown it.");
            if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Info, "Repossession notice",
                $"The {c.Name} (plate {c.identity.plate}) was collected by the lender. The previous owner never paid off the loan.");
        }
    }
}
