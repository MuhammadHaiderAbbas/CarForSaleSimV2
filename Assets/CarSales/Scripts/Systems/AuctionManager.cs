using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace CarSales
{
    /// <summary>Saved auction data ("auction" save module).</summary>
    [Serializable]
    public class AuctionState
    {
        public int version = 1;
        public List<AuctionEvent> events = new List<AuctionEvent>();
        public List<AuctionLot> lots = new List<AuctionLot>();
        public List<CarInstance> cars = new List<CarInstance>();
        public List<AuctionInvoice> invoices = new List<AuctionInvoice>();
        public List<BuyerAccount> accounts = new List<BuyerAccount>();
        public List<AuctionBidder> bidders = new List<AuctionBidder>();
        public int nextLotNo = 101, nextBuyerNo = 2140, nextInvoice = 1, lastDay;
    }

    /// <summary>
    /// The auction ecosystem (spec §6.9): a calendar of events at three venues (the Auction Center hall with its yard,
    /// TradeNet online, SalvageBid online), lots with condition reports, AI bidders, live sales in the hall (raise the
    /// paddle with E), timed online sales with proxy bids and soft close, invoices (premium + VAT), late fees,
    /// storage and collection from the Auction Center car park.
    /// </summary>
    public class AuctionManager : MonoBehaviour
    {
        [SerializeField] int lotsPerHallSale = 8;
        [SerializeField] int lotsPerOnlineSale = 8;
        [SerializeField] int daysPublished = 7;
        [SerializeField] int catalogueDays = 2;      // lots (and the yard) appear this many days before the sale
        [SerializeField] float openHour = 9f, closeHour = 18f;

        public Transform[] parkingBays;   // where won cars are left (set by the wizard)

        AuctionState state = new AuctionState();
        System.Random rng = new System.Random();
        InputAction interact;
        float onlineTimer;

        public float OpenHour => openHour;
        public float CloseHour => closeHour;
        public bool IsOpen => CarSalesServices.DayCycle && CarSalesServices.DayCycle.HourOfDay >= openHour && CarSalesServices.DayCycle.HourOfDay < closeHour;
        public IReadOnlyList<AuctionEvent> Events => state.events;
        public IReadOnlyList<AuctionInvoice> Invoices => state.invoices;
        public IReadOnlyList<BuyerAccount> Accounts => state.accounts;
        public event Action OnChanged;

        /// <summary>Lot cars still to be sold today at the hall (compatibility with v4 code).</summary>
        public List<CarInstance> Lots => TodayHallLots().Select(l => CarOf(l)).Where(c => c != null).ToList();

        void Awake()
        {
            CarSalesServices.Auction = this;
            if (!GetComponent<AuctionHallHUD>()) gameObject.AddComponent<AuctionHallHUD>();
        }
        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
            interact = CarSalesInput.Resolve(null, "Interact");
        }
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);
        void Start() => EnsureSchedule();

        static int Today => CarRecords.Today;
        static float Hour => CarSalesServices.DayCycle ? CarSalesServices.DayCycle.HourOfDay : 9f;
        static UIManager UI => CarSalesServices.UI;

        // ------------------------------------------------------------------ SAVE
        public AuctionState GetState() => state;

        public void SetState(AuctionState s)
        {
            EndLive();
            state = s ?? new AuctionState();
            foreach (var c in state.cars) CarParts.EnsureParts(c);
            EnsureSchedule();
            Changed();
        }

        /// <summary>New game: the calendar is filled again by GenerateLots once the day is set.</summary>
        public void ResetState()
        {
            EndLive();
            state = new AuctionState();
            Changed();
        }

        /// <summary>v4 name, called every morning and after loading: keeps the calendar filled.</summary>
        public void GenerateLots() => EnsureSchedule();

        void Changed() { OnChanged?.Invoke(); }

        // ------------------------------------------------------------------ LOOKUPS
        public AuctionLot Lot(string id) => state.lots.Find(l => l.id == id);
        public CarInstance CarOf(AuctionLot l) => l == null ? null : state.cars.Find(c => c.instanceId == l.carId);
        public AuctionEvent EventOf(AuctionLot l) => l == null ? null : state.events.Find(e => e.id == l.eventId);
        public IEnumerable<AuctionLot> LotsOf(AuctionEvent e) => e.lots.Select(Lot).Where(l => l != null);
        public BuyerAccount Account(string venueId) => state.accounts.Find(a => a.venueId == venueId && a.registered);
        public bool Registered(string venueId) => Account(venueId) != null;
        public AuctionBidder Bidder(string id) => state.bidders.Find(b => b.id == id);

        public string BidderName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "-";
            if (id == AuctionRules.PlayerId) return "You";
            var b = Bidder(id);
            return b != null ? b.name : id;
        }

        IEnumerable<AuctionLot> TodayHallLots() =>
            state.events.Where(e => e.day == Today && !e.online && !e.finished).SelectMany(LotsOf).Where(l => l.Status == LotStatus.Upcoming || l.Status == LotStatus.Live);

        /// <summary>Lots standing in the yard: hall sales today (not yet sold) and tomorrow (viewing).</summary>
        public IEnumerable<AuctionLot> YardLots() =>
            state.events.Where(e => !e.online && !e.finished && e.day >= Today && e.day <= Today + 1).OrderBy(e => e.day).ThenBy(e => e.startHour)
                        .SelectMany(LotsOf).Where(l => l.Status == LotStatus.Upcoming && (live == null || live.id != l.id));

        // ------------------------------------------------------------------ CALENDAR
        void EnsureSchedule()
        {
            if (state.bidders.Count == 0) state.bidders = AuctionRules.GenerateBidders(16, new System.Random(Environment.TickCount));
            if (!CarSalesServices.Database || CarSalesServices.Database.cars.Count == 0) return;
            for (int d = Today; d <= Today + daysPublished; d++)
            {
                var w = GameCalendar.WeekdayOf(d);
                if (w == Weekday.Tuesday || w == Weekday.Thursday || w == Weekday.Saturday) AddEvent(d, "hall", AuctionType.Dealer, 11f);
                if (w == Weekday.Wednesday) AddEvent(d, "hall", AuctionType.Fleet, 13f);
                if (w == Weekday.Friday) AddEvent(d, "hall", AuctionType.Repossession, 14f);
                AddEvent(d, "online", AuctionType.OnlineTimed, 8f);
                if (w == Weekday.Monday || w == Weekday.Thursday) AddEvent(d, "salvage", AuctionType.Salvage, 8f);
            }
            foreach (var e in state.events)
                if (!e.finished && e.lots.Count == 0 && e.day <= Today + catalogueDays) FillLots(e);
            // Forget finished events after a week (their invoices stay).
            var old = state.events.Where(e => e.finished && e.day < Today - 7).ToList();
            foreach (var e in old)
            {
                foreach (var lot in LotsOf(e).ToList())
                {
                    if (!state.invoices.Exists(i => i.lotId == lot.id && !i.paid && !i.cancelled)) state.cars.RemoveAll(c => c.instanceId == lot.carId);
                    state.lots.Remove(lot);
                }
                state.events.Remove(e);
            }
        }

        void AddEvent(int day, string venue, AuctionType type, float hour)
        {
            if (state.events.Exists(e => e.day == day && e.venueId == venue && e.type == (int)type)) return;
            state.events.Add(new AuctionEvent
            {
                id = $"{venue}-{day}-{(int)type}", venueId = venue, type = (int)type, day = day, startHour = hour,
                online = !AuctionRules.Venue(venue).Physical,
            });
        }

        void FillLots(AuctionEvent e)
        {
            var db = CarSalesServices.Database;
            var venue = AuctionRules.Venue(e.venueId);
            int count = e.online ? (e.Type == AuctionType.Salvage ? 6 : lotsPerOnlineSale) : (e.Type == AuctionType.Dealer ? lotsPerHallSale : 6);
            for (int i = 0; i < count; i++)
            {
                var car = CarValueCalculator.GenerateRandom(db.GetRandom());
                car.state = CarState.InTransit;
                car.fuel = Random.Range(5f, 40f);
                car.cleanliness = Random.Range(5f, 60f);
                Shape(car, e.Type);
                var r = new System.Random(VehicleRecords.Seed(car.instanceId) ^ 0x5a17);
                var faults = car.faults.Select(f => f.Def?.Name).Where(n => n != null).ToList();
                var damaged = car.components.Where(c => ComponentCatalog.Get(c.Id)?.Group == ComponentGroup.Body && c.health < 60f)
                                            .Select(c => $"{ComponentCatalog.Get(c.Id).Name} damaged").ToList();
                bool starts = CarParts.Effects(car).CanStart;
                var report = AuctionRules.Report(car.condition, faults, damaged, car.mileage, car.history.clocked, car.documents.keys, starts,
                                                 car.documents.v5cPresent, venue, r);
                // What the report says is now known to everyone.
                foreach (var f in car.faults) if (f.Def != null && report.faults.Contains(f.Def.Name)) { f.known = true; f.foundBy = (int)InspectMethod.Visual; }
                car.reportedCondition = car.condition;
                int retail = CarValueCalculator.MarketValue(car, useReported: true);
                float trade = e.Type == AuctionType.Salvage ? 0.45f : e.Type == AuctionType.Fleet ? 0.8f : 0.72f;
                int estimate = Round50(retail * trade);
                var lot = new AuctionLot
                {
                    id = Guid.NewGuid().ToString("N").Substring(0, 10), lotNo = state.nextLotNo++, eventId = e.id, carId = car.instanceId,
                    estimate = estimate, startPrice = Mathf.Max(100, Round50(estimate * 0.4f)), report = report,
                    reserve = r.NextDouble() < 0.3 ? 0 : Round50(estimate * (0.6f + (float)r.NextDouble() * 0.35f)),
                    status = (int)LotStatus.Upcoming,
                };
                if (e.online) lot.endHour = 14f + i * (6f / Mathf.Max(1, count - 1));
                car.purchasePrice = estimate;
                state.cars.Add(car);
                state.lots.Add(lot);
                e.lots.Add(lot.id);
            }
            if (state.nextLotNo > 999) state.nextLotNo = 101;
        }

        static int Round50(float v) => Mathf.Max(0, Mathf.RoundToInt(v / 50f) * 50);

        /// <summary>Each sale type has its own kind of car (spec Phase 6 step 5).</summary>
        static void Shape(CarInstance car, AuctionType type)
        {
            switch (type)
            {
                case AuctionType.Fleet:
                    foreach (var c in car.components) c.health = Mathf.Max(c.health, Random.Range(62f, 90f));
                    car.mileage = Random.Range(40000, 140000);
                    car.documents.service = ServiceHistory.Full;
                    car.documents.keys = 2;
                    car.documents.v5cPresent = true;
                    car.history.writeOff = WriteOff.None;
                    car.history.clocked = false;
                    car.faults.RemoveAll(f => f.severity > 0.5f);
                    break;
                case AuctionType.Salvage:
                    foreach (var c in car.components)
                        if (ComponentCatalog.Get(c.Id)?.Group == ComponentGroup.Body) c.health = Mathf.Min(c.health, Random.Range(10f, 55f));
                    car.history.writeOff = Random.value < 0.7f ? (Random.value < 0.6f ? WriteOff.CatN : WriteOff.CatS) : car.history.writeOff;
                    if (Random.value < 0.3f) car.documents.keys = Mathf.Max(0, car.documents.keys - 1);
                    // Some "non-runners" only need a battery: an opportunity for whoever inspects.
                    if (Random.value < 0.25f && ComponentSet.Find(car.components, ComponentId.Battery12V) != null)
                    {
                        ComponentSet.Set(car.components, ComponentId.Battery12V, 3f);
                        if (!car.faults.Exists(f => f.Id == FaultId.Battery12VWeak)) car.faults.Add(new FaultInstance(FaultId.Battery12VWeak, 1f));
                    }
                    break;
                case AuctionType.Repossession:
                    if (Random.value < 0.4f) car.documents.keys = 1;
                    if (Random.value < 0.3f) car.documents.v5cPresent = false;
                    car.history.outstandingFinance = true;
                    car.history.financeShare = 0.3f;
                    break;
            }
            CarParts.Recalculate(car);
        }

        // ------------------------------------------------------------------ ACCOUNTS
        public bool Register(string venueId)
        {
            if (Registered(venueId)) return true;
            var venue = AuctionRules.Venue(venueId);
            if (!CarSalesServices.Economy.TrySpend(AuctionRules.Deposit, TxCategory.Deposits, $"Deposit - {venue.Name}"))
            { UI.Error($"You need {UIFactory.Money(AuctionRules.Deposit)} for the refundable deposit."); return false; }
            var acc = state.accounts.Find(a => a.venueId == venueId);
            if (acc == null) { acc = new BuyerAccount { venueId = venueId, buyerNo = state.nextBuyerNo++ }; state.accounts.Add(acc); }
            acc.registered = true;
            acc.deposit = AuctionRules.Deposit;
            UI.Success("Registered", $"{venue.Name}: your buyer number is {acc.buyerNo}.");
            Changed();
            return true;
        }

        public bool CloseAccount(string venueId)
        {
            var acc = Account(venueId);
            if (acc == null) return false;
            if (state.invoices.Exists(i => i.venueId == venueId && !i.paid && !i.cancelled)) { UI.Error("Pay your open invoices first."); return false; }
            if (state.lots.Exists(l => l.playerMax > 0 && (l.Status == LotStatus.Upcoming || l.Status == LotStatus.Live) && EventOf(l)?.venueId == venueId))
            { UI.Error("Withdraw your bids first."); return false; }
            CarSalesServices.Economy.AddMoney(acc.deposit, TxCategory.Deposits, $"Deposit refund - {AuctionRules.Venue(venueId).Name}");
            acc.deposit = 0;
            acc.registered = false;
            Changed();
            return true;
        }

        // ------------------------------------------------------------------ BIDS
        public bool CanBidOn(AuctionLot lot, out string why)
        {
            why = "";
            var e = EventOf(lot);
            if (e == null || e.finished || (lot.Status != LotStatus.Upcoming && lot.Status != LotStatus.Live)) { why = "This lot is closed."; return false; }
            if (!Registered(e.venueId)) { why = $"Register with {AuctionRules.Venue(e.venueId).Name} first ({UIFactory.Money(AuctionRules.Deposit)} refundable deposit)."; return false; }
            return true;
        }

        /// <summary>Proxy / absentee bid: the system bids for you up to this amount (0 = withdraw).</summary>
        public bool SetMaxBid(AuctionLot lot, int max)
        {
            if (!CanBidOn(lot, out string why)) { UI.Error(why); return false; }
            if (max > 0 && max < AuctionRules.NextBid(lot)) { UI.Error($"Your maximum must be at least {UIFactory.Money(AuctionRules.NextBid(lot))}."); return false; }
            if (max > CarSalesServices.Economy.Money) { UI.Error("You can't bid more than the cash you have."); return false; }
            lot.playerMax = max;
            if (max > 0) lot.watched = true;
            var e = EventOf(lot);
            if (e.online && e.day == Today && Hour >= e.startHour) UpdateOnlineLot(e, lot, Hour, notify: false);
            UI.Notify(max > 0 ? $"Lot {lot.lotNo}: maximum bid {UIFactory.Money(max)}." : $"Lot {lot.lotNo}: bid withdrawn.");
            Changed();
            return true;
        }

        public void ToggleWatch(AuctionLot lot) { lot.watched = !lot.watched; Changed(); }

        /// <summary>Bidders who want the lot and how much they'd pay (seeded per lot so it doesn't change).</summary>
        List<(AuctionBidder bidder, int max)> AiInterest(AuctionLot lot, AuctionEvent e)
        {
            var car = CarOf(lot);
            var list = new List<(AuctionBidder, int)>();
            if (car == null) return list;
            var r = new System.Random(VehicleRecords.Seed(lot.id));
            var def = CarSalesServices.Database ? CarSalesServices.Database.Get(car.carId) : null;
            string brand = def ? def.brand.ToLowerInvariant() : "";
            bool jap = new[] { "toyota", "honda", "nissan", "mazda", "subaru", "mitsubishi", "suzuki", "lexus" }.Any(brand.Contains)
                       || (def && (def.category == CarCategory.SUV || def.category == CarCategory.Pickup));
            // Bidders go by the report, not by what you found yourself.
            int repairs = car.faults.Where(f => f.Def != null && lot.report.faults.Contains(f.Def.Name)).Sum(f => CarRecords.RepairEstimate(car, f)) + lot.report.damage.Count * 400;
            var view = new AuctionRules.LotView
            {
                retailIfAsReported = CarValueCalculator.MarketValue(car, useReported: true), reportedRepairs = repairs, grade = lot.report.grade,
                starts = lot.report.starts, mileageVerified = lot.report.mileageVerified, japaneseOr4x4 = jap, salvage = e.Type == AuctionType.Salvage,
                mileage = car.mileage, faultCount = lot.report.faults.Count, damageCount = lot.report.damage.Count,
            };
            foreach (var b in state.bidders)
            {
                if (r.NextDouble() < (e.online ? 0.45 : 0.55)) continue;   // not attending / not interested today
                if (b.wonDay == e.day && b.wonToday >= b.maxLotsPerDay) continue;
                int max = AuctionRules.MaxBid(b, view, r);
                if (max >= lot.startPrice) list.Add((b, max));
            }
            return list;
        }

        // ------------------------------------------------------------------ RESULTS
        void Settle(AuctionLot lot, AuctionEvent e, LotStatus status)
        {
            lot.Status = status;
            var car = CarOf(lot);
            if (status == LotStatus.Sold && lot.buyer == AuctionRules.PlayerId) IssueInvoice(lot, e);
            else if (status == LotStatus.Provisional && lot.buyer == AuctionRules.PlayerId)
                UI?.Toast(ToastType.Info, $"Lot {lot.lotNo}: provisional", $"Your {UIFactory.Money(lot.hammer)} didn't meet the reserve. The seller decides by tonight.");
            else if (status == LotStatus.Provisional)
                lot.Status = rng.NextDouble() < AuctionRules.SellerAcceptChance(lot.hammer, lot.reserve) ? LotStatus.Sold : LotStatus.Unsold;
            if (lot.Status == LotStatus.Sold && lot.buyer != AuctionRules.PlayerId)
            {
                var b = Bidder(lot.buyer);
                if (b != null) { if (b.wonDay != e.day) { b.wonDay = e.day; b.wonToday = 0; } b.wonToday++; }
                if (lot.playerMax > 0 || lot.watched)
                    UI?.Toast(ToastType.Warning, $"Lot {lot.lotNo} sold", $"{car?.Name} went to {BidderName(lot.buyer)} for {UIFactory.Money(lot.hammer)}.");
            }
            if (lot.Status == LotStatus.Unsold || (lot.Status == LotStatus.Sold && lot.buyer != AuctionRules.PlayerId))
                state.cars.RemoveAll(c => c.instanceId == lot.carId);
            Changed();
        }

        void IssueInvoice(AuctionLot lot, AuctionEvent e)
        {
            var venue = AuctionRules.Venue(e.venueId);
            var inv = AuctionRules.Invoice(lot, venue, Today, "INV" + (state.nextInvoice++).ToString("0000"));
            state.invoices.Add(inv);
            var car = CarOf(lot);
            CarSalesServices.PlaySfx(Sfx.Success);
            string msg = $"{car?.Name} for {UIFactory.Money(lot.hammer)}. Invoice {inv.id}: {UIFactory.Money(inv.Total)} incl. fees and VAT, due day {inv.dueDay}. " +
                         $"Pay it in the Auctions app, then collect the car from the Auction Center car park.";
            UI?.Toast(ToastType.Success, $"You won lot {lot.lotNo}!", msg);
            if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Info, $"{venue.Name}: you won lot {lot.lotNo}", msg);
        }

        public bool PayInvoice(AuctionInvoice inv)
        {
            if (inv.paid || inv.cancelled) return false;
            var eco = CarSalesServices.Economy;
            if (!eco.CanAfford(inv.Total)) { UI.Error($"You need {UIFactory.Money(inv.Total)}."); return false; }
            var lot = Lot(inv.lotId);
            var car = state.cars.Find(c => c.instanceId == inv.carId);
            if (car == null) { UI.Error("This car is no longer available."); return false; }
            var venue = AuctionRules.Venue(inv.venueId);
            eco.TrySpend(inv.hammer, TxCategory.VehiclePurchase, $"{venue.Name} lot {inv.lotNo} - {car.Name}", car.instanceId);
            eco.TrySpend(inv.Fees, TxCategory.Auction, $"Buyer's fees lot {inv.lotNo}", car.instanceId);
            eco.TrySpend(inv.vat, TxCategory.Vat, $"VAT on fees lot {inv.lotNo}", car.instanceId);
            if (inv.lateFees > 0) eco.TrySpend(inv.lateFees, TxCategory.Fines, $"Late payment lot {inv.lotNo}", car.instanceId);
            inv.paid = true;
            CarSalesServices.DayCycle.RegisterPurchase(inv.Total);
            if (CarSalesServices.Progress) CarSalesServices.Progress.RegisterPurchase();

            car.purchasePrice = inv.hammer;
            car.state = CarValueCalculator.DefaultState(car);
            car.inspected = true;
            if (inv.titleGuaranteed && car.history.outstandingFinance) car.history.financeSettled = true;   // the auction clears it
            state.cars.Remove(car);
            var bay = FreeParkingBay();
            CarSalesServices.Vehicles.AddOwnedCar(car, bay.position, bay.eulerAngles.y);
            UI.Success("Invoice paid", $"The {car.Name} waits in the Auction Center car park. Free storage until day {inv.freeStorageUntil}, then {UIFactory.Money(AuctionRules.StoragePerDay)}/day.");
            Changed();
            return true;
        }

        // ------------------------------------------------------------------ DAILY
        void OnDayStarted(DayStarted ev)
        {
            // Sales that ran while you slept or were away.
            foreach (var e in state.events.Where(x => !x.finished && x.day < ev.Day).ToList()) FinishEvent(e, instant: true);
            foreach (var inv in state.invoices.Where(i => !i.paid && !i.cancelled).ToList())
            {
                int before = inv.lateFees;
                if (AuctionRules.ApplyLateFees(inv, ev.Day))
                {
                    var acc = state.accounts.Find(a => a.venueId == inv.venueId);
                    if (acc != null) { acc.deposit = 0; acc.registered = false; }
                    state.cars.RemoveAll(c => c.instanceId == inv.carId);
                    CarSalesServices.Economy.ChangeReputation(-3f);
                    UI?.Toast(ToastType.Warning, "Sale cancelled", $"You didn't pay invoice {inv.id}: the sale is cancelled and your deposit is kept.");
                }
                else if (inv.lateFees > before)
                {
                    CarSalesServices.Economy.ChangeReputation(-1f);
                    UI?.Toast(ToastType.Warning, "Invoice overdue", $"{inv.id} is overdue: +{UIFactory.Money(AuctionRules.LateFeePerDay)} late fee. Pay it in the Auctions app.");
                }
            }
            CheckCollections();
            foreach (var inv in state.invoices.Where(i => i.paid && !i.collected && !i.cancelled))
            {
                int fee = AuctionRules.StorageForNight(inv, ev.Day);
                if (fee <= 0) continue;
                inv.storageFees += fee;
                CarSalesServices.Economy.ForceSpend(fee, TxCategory.Auction, $"Storage lot {inv.lotNo}", inv.carId);
            }
            EnsureSchedule();
            Changed();
        }

        /// <summary>A paid car counts as collected once it has left the Auction Center car park (or been sold/repossessed).</summary>
        void CheckCollections()
        {
            var zone = ServiceZone.Find(ServiceType.Auction);
            foreach (var inv in state.invoices.Where(i => i.paid && !i.collected))
            {
                var d = CarSalesServices.Vehicles ? CarSalesServices.Vehicles.GetDisplay(inv.carId) : null;
                bool owned = CarSalesServices.Inventory && CarSalesServices.Inventory.OwnedCars.Exists(c => c.instanceId == inv.carId);
                if (!owned || (d && (!zone || !zone.Contains(d.transform.position)))) inv.collected = true;
            }
        }

        public Transform FreeParkingBay()
        {
            var zone = ServiceZone.Find(ServiceType.Auction);
            var bay = zone ? zone.FreeBay() : null;
            if (bay) return bay;
            return parkingBays != null && parkingBays.Length > 0 ? parkingBays[0] : transform;
        }

        // ------------------------------------------------------------------ CLOCK
        float collectTimer;

        void Update()
        {
            if (!CarSalesServices.DayCycle) return;
            int day = Today;
            float hour = Hour;
            if (state.lastDay != day) { state.lastDay = day; }
            collectTimer -= Time.deltaTime;
            if (collectTimer <= 0f) { collectTimer = 3f; CheckCollections(); }

            // Online timed sales (checked every half second).
            onlineTimer -= Time.deltaTime;
            if (onlineTimer <= 0f)
            {
                onlineTimer = 0.5f;
                foreach (var e in state.events.Where(x => x.online && !x.finished && x.day == day && hour >= x.startHour).ToList())
                {
                    foreach (var lot in LotsOf(e).Where(l => l.Status == LotStatus.Upcoming || l.Status == LotStatus.Live).ToList())
                    {
                        lot.Status = LotStatus.Live;
                        UpdateOnlineLot(e, lot, hour, notify: true);
                        if (hour >= lot.endHour) { AuctionRules.Hammer(lot); Settle(lot, e, string.IsNullOrEmpty(lot.leader) ? LotStatus.Unsold : lot.currentBid >= lot.reserve ? LotStatus.Sold : LotStatus.Provisional); }
                    }
                    if (LotsOf(e).All(l => l.Status != LotStatus.Upcoming && l.Status != LotStatus.Live)) FinishEvent(e, instant: true);
                }
            }

            // Live hall sale.
            var sale = state.events.FirstOrDefault(x => !x.online && !x.finished && x.day == day && hour >= x.startHour);
            if (sale != null) RunHall(sale, hour);
            else if (live != null) EndLive();

            if (hour >= 23.5f) foreach (var e in state.events.Where(x => !x.finished && x.day == day).ToList()) FinishEvent(e, instant: true);
        }

        /// <summary>Online lot: AI bidders "arrive" during the last hours; proxy bidding decides the current price; late bids extend the end.</summary>
        void UpdateOnlineLot(AuctionEvent e, AuctionLot lot, float hour, bool notify)
        {
            var interest = AiInterest(lot, e);
            var r = new System.Random(VehicleRecords.Seed(lot.id + "arrive"));
            var maxima = new List<int>();
            var ids = new List<string>();
            if (lot.playerMax > 0) { maxima.Add(lot.playerMax); ids.Add(AuctionRules.PlayerId); }
            foreach (var (b, max) in interest)
            {
                float arrive = lot.endHour - (float)r.NextDouble() * 6f;
                if (hour >= arrive || hour >= lot.endHour) { maxima.Add(max); ids.Add(b.id); }
            }
            // An earlier leader keeps priority on ties: put them first.
            int li = ids.IndexOf(lot.leader);
            if (li > 0) { (maxima[0], maxima[li]) = (maxima[li], maxima[0]); (ids[0], ids[li]) = (ids[li], ids[0]); }
            var (price, winner) = AuctionRules.ResolveProxy(maxima, lot.startPrice);
            string newLeader = winner >= 0 ? ids[winner] : "";
            if (newLeader != lot.leader)
            {
                if (lot.leader == AuctionRules.PlayerId && notify)
                {
                    string msg = $"Lot {lot.lotNo} ({CarOf(lot)?.Name}): someone bid {UIFactory.Money(price)}. Raise your maximum in the Auctions app.";
                    UI?.Toast(ToastType.Warning, "Outbid!", msg);
                    if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Info, "Outbid", msg);
                }
                if (!string.IsNullOrEmpty(lot.leader)) lot.endHour = AuctionRules.SoftClose(lot.endHour, hour);
                lot.leader = newLeader;
                Changed();
            }
            if (lot.currentBid != price) { lot.currentBid = price; Changed(); }
        }

        void FinishEvent(AuctionEvent e, bool instant)
        {
            if (e.finished) return;
            if (live != null && live.eventId == e.id) EndLive();
            foreach (var lot in LotsOf(e).ToList())
            {
                if (lot.Status == LotStatus.Upcoming || lot.Status == LotStatus.Live)
                {
                    if (e.online) UpdateOnlineLot(e, lot, 99f, notify: false);
                    else ResolveInstantly(lot, e);
                    var st = AuctionRules.Hammer(lot);
                    Settle(lot, e, st);
                }
                // The seller answers provisional bids at the end of the sale.
                if (lot.Status == LotStatus.Provisional && lot.buyer == AuctionRules.PlayerId)
                {
                    if (rng.NextDouble() < AuctionRules.SellerAcceptChance(lot.hammer, lot.reserve))
                    {
                        lot.Status = LotStatus.Sold;
                        UI?.Toast(ToastType.Success, $"Lot {lot.lotNo}: accepted", "The seller accepted your provisional bid.");
                        IssueInvoice(lot, e);
                    }
                    else
                    {
                        lot.Status = LotStatus.Unsold;
                        state.cars.RemoveAll(c => c.instanceId == lot.carId);
                        UI?.Toast(ToastType.Info, $"Lot {lot.lotNo}: declined", "The seller declined your provisional bid.");
                    }
                }
            }
            e.finished = true;
            Changed();
        }

        /// <summary>A lot sold while you're not in the hall: every bidder (and your absentee bid) bids up to their maximum.</summary>
        void ResolveInstantly(AuctionLot lot, AuctionEvent e)
        {
            var interest = AiInterest(lot, e);
            var maxima = new List<int>();
            var ids = new List<string>();
            if (lot.playerMax > 0) { maxima.Add(lot.playerMax); ids.Add(AuctionRules.PlayerId); }
            foreach (var (b, max) in interest) { maxima.Add(max); ids.Add(b.id); }
            var (price, winner) = AuctionRules.ResolveProxy(maxima, lot.startPrice);
            lot.currentBid = price;
            lot.leader = winner >= 0 ? ids[winner] : "";
        }

        // ------------------------------------------------------------------ LIVE HALL
        AuctionLot live;
        List<(AuctionBidder bidder, int max)> liveInterest;
        public enum Call { Opening, None, GoingOnce, GoingTwice, Sold }
        public Call LiveCall { get; private set; }
        public AuctionLot LiveLot => live;
        string liveMessage = "";
        /// <summary>The auctioneer's last call (also shown as a subtitle when you're in the hall).</summary>
        public string LiveMessage
        {
            get => liveMessage;
            private set { liveMessage = value; if (!string.IsNullOrEmpty(value) && PlayerInHall()) Subtitles.Say("Auctioneer", value, 4f); }
        }
        float callTimer, aiTimer, pauseTimer, nextInstantHour;
        CarDisplay stageCar;

        public static bool PlayerInHall()
        {
            var hall = AuctionHall.Instance;
            if (!hall) return false;
            var p = FindFirstObjectByType<PlayerInteractor>();
            return p && hall.Contains(p.transform.position) && !(CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving);
        }

        void RunHall(AuctionEvent e, float hour)
        {
            if (pauseTimer > 0f) { pauseTimer -= Time.deltaTime; if (pauseTimer <= 0f) LiveMessage = ""; return; }
            if (live == null)
            {
                var next = LotsOf(e).FirstOrDefault(l => l.Status == LotStatus.Upcoming);
                if (next == null) { FinishEvent(e, instant: false); return; }
                if (!PlayerInHall())
                {
                    // Nobody's watching: lots go every 6 game minutes.
                    if (hour < nextInstantHour) return;
                    nextInstantHour = hour + 0.1f;
                    next.Status = LotStatus.Live;
                    ResolveInstantly(next, e);
                    Settle(next, e, AuctionRules.Hammer(next));
                    return;
                }
                StartLive(next, e);
                return;
            }
            // Live lot
            callTimer -= Time.deltaTime;
            aiTimer -= Time.deltaTime;
            if (aiTimer <= 0f)
            {
                aiTimer = Random.Range(0.7f, 1.6f);
                var maxima = liveInterest.Select(x => x.max).ToList();
                var ids = liveInterest.Select(x => x.bidder.id).ToList();
                if (live.playerMax > 0) { maxima.Add(live.playerMax); ids.Add(AuctionRules.PlayerId); }   // your absentee bid
                float eagerness = LiveCall == Call.Opening ? 0.8f : 0.6f;
                int i = AuctionRules.LiveAiBid(live, maxima, ids, eagerness, rng);
                if (i >= 0) PlaceLiveBid(ids[i]);
            }
            if (callTimer <= 0f)
            {
                if (LiveCall == Call.Opening && string.IsNullOrEmpty(live.leader)) { LiveCall = Call.GoingTwice; }
                else if (LiveCall == Call.Opening || LiveCall == Call.None) LiveCall = Call.GoingOnce;
                else if (LiveCall == Call.GoingOnce) LiveCall = Call.GoingTwice;
                else { Hammer(e); return; }
                callTimer = AuctionRules.CallSeconds;
                LiveMessage = LiveCall == Call.GoingOnce ? "Going once..." : string.IsNullOrEmpty(live.leader) ? "Any bids? Last chance..." : "Going twice...";
            }
            // Paddle: E while you're in the hall and not looking at something else.
            if (interact != null && interact.WasPressedThisFrame() && PlayerInHall() && !(UI && UI.IsMenuOpen))
            {
                var p = FindFirstObjectByType<PlayerInteractor>();
                if (p == null || p.CurrentTarget == null) PaddleBid();
            }
        }

        void StartLive(AuctionLot lot, AuctionEvent e)
        {
            live = lot;
            lot.Status = LotStatus.Live;
            lot.leader = "";
            lot.currentBid = 0;
            liveInterest = AiInterest(lot, e);
            LiveCall = Call.Opening;
            callTimer = AuctionRules.CallSeconds * 1.5f;
            aiTimer = Random.Range(1.5f, 2.5f);
            var car = CarOf(lot);
            LiveMessage = $"Lot {lot.lotNo}: {car?.Name}. Who'll start me at {UIFactory.Money(lot.startPrice)}?";
            var hall = AuctionHall.Instance;
            if (hall && car != null) stageCar = hall.ShowOnStage(car, lot.lotNo);
            CarSalesServices.PlaySfx(Sfx.Click, 0.4f);
            Changed();
        }

        void PlaceLiveBid(string bidder)
        {
            int next = AuctionRules.NextBid(live);
            live.currentBid = next;
            live.leader = bidder;
            LiveCall = Call.None;
            callTimer = AuctionRules.CallSeconds;
            var b = Bidder(bidder);
            LiveMessage = bidder == AuctionRules.PlayerId ? $"{UIFactory.Money(next)} - the bid's with you, paddle {Account("hall")?.buyerNo}!"
                                                          : $"{UIFactory.Money(next)} from {(b != null ? b.name : "the room")}!";
            Changed();
        }

        /// <summary>Raise your paddle for the next increment (live hall sale).</summary>
        public void PaddleBid()
        {
            if (live == null) return;
            if (!Registered("hall")) { UI.Error("Register at the auction desk first to get a buyer number."); return; }
            if (live.leader == AuctionRules.PlayerId) { UI.Notify("The bid is already with you."); return; }
            int next = AuctionRules.NextBid(live);
            if (!CarSalesServices.Economy.CanAfford(next)) { UI.Error("You can't afford that bid."); return; }
            CarSalesServices.PlaySfx(Sfx.Click);
            PlaceLiveBid(AuctionRules.PlayerId);
        }

        void Hammer(AuctionEvent e)
        {
            var lot = live;
            var st = AuctionRules.Hammer(lot);
            LiveCall = Call.Sold;
            LiveMessage = st == LotStatus.Unsold ? $"No bids - lot {lot.lotNo} withdrawn." :
                          st == LotStatus.Provisional ? $"{UIFactory.Money(lot.hammer)} - provisional, the reserve wasn't met." :
                          $"SOLD for {UIFactory.Money(lot.hammer)} to {(lot.buyer == AuctionRules.PlayerId ? "you" : BidderName(lot.buyer))}!";
            Settle(lot, e, st);
            EndLive(keepMessage: true);
            pauseTimer = 2.5f;
        }

        void EndLive(bool keepMessage = false)
        {
            if (live != null && live.Status == LotStatus.Live) { live.Status = LotStatus.Upcoming; live.leader = ""; live.currentBid = 0; }
            live = null;
            liveInterest = null;
            if (stageCar) Destroy(stageCar.gameObject);
            stageCar = null;
            if (!keepMessage) LiveMessage = "";
            Changed();
        }

        /// <summary>The next sale at the hall today (or null).</summary>
        public AuctionEvent NextHallSale() => state.events.Where(e => !e.online && !e.finished && e.day >= Today).OrderBy(e => e.day).ThenBy(e => e.startHour).FirstOrDefault();
    }
}
