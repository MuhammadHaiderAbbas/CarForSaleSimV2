using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 7 market (spec §6.8): daily price drift per model and body type, seasons, demand that dips after each
    /// sale, supply from your own listings; three competitor dealers with their own prices; weekly wholesale
    /// batches from the trade. Saved as module "market". Added by CarSalesBootstrap if missing.
    /// </summary>
    public class MarketManager : MonoBehaviour
    {
        MarketState state = new MarketState();
        readonly System.Random rng = new System.Random();
        readonly int[] listedCache = new int[6];
        float cacheTime = -10f;

        public MarketState State => state;
        public event Action OnChanged;

        void Awake() => CarSalesServices.Market = this;
        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);
        void Start() { if (state.competitorListings.Count == 0) Refresh(CarRecords.Today, newBatch: true); }

        public void SetState(MarketState s)
        {
            state = s ?? new MarketState();
            if (state.category == null || state.category.Length != 6) state.category = new[] { 1f, 1f, 1f, 1f, 1f, 1f };
            if (state.demand == null || state.demand.Length != 6) state.demand = new[] { 1f, 1f, 1f, 1f, 1f, 1f };
            if (state.competitorListings.Count == 0) Refresh(CarRecords.Today, newBatch: true);
            OnChanged?.Invoke();
        }

        public void ResetState() => SetState(new MarketState());

        static MarketCategory Cat(CarDefinition d) => d ? (MarketCategory)(int)d.category : MarketCategory.Sedan;

        int ListedOf(MarketCategory c)
        {
            if (Time.unscaledTime - cacheTime > 2f)
            {
                cacheTime = Time.unscaledTime;
                Array.Clear(listedCache, 0, listedCache.Length);
                var inv = CarSalesServices.Inventory;
                var db = CarSalesServices.Database;
                if (inv && db)
                    foreach (var car in inv.OwnedCars)
                        if (car.state == CarState.Listed) { var d = db.Get(car.carId); if (d) listedCache[(int)d.category]++; }
            }
            return listedCache[(int)c];
        }

        /// <summary>Today's price factor for a model (1 = its base value).</summary>
        public float Multiplier(CarDefinition def)
        {
            if (!def) return 1f;
            var c = Cat(def);
            return MarketModel.Multiplier(state, def.carId, c, GameCalendar.QuarterInYear(CarRecords.Today), ListedOf(c));
        }

        public float CategoryMultiplier(MarketCategory c)
        {
            float cat = state.category[(int)c] * state.demand[(int)c];
            return cat * MarketModel.Season(c, GameCalendar.QuarterInYear(CarRecords.Today)) * MarketModel.Supply(ListedOf(c));
        }

        public void RegisterSale(CarDefinition def)
        {
            if (!def) return;
            MarketModel.RegisterSale(state, Cat(def));
            OnChanged?.Invoke();
        }

        void OnDayStarted(DayStarted e)
        {
            var db = CarSalesServices.Database;
            if (db) MarketModel.Step(state, db.cars.Where(c => c).Select(c => c.carId), e.Day, rng);
            Refresh(e.Day, newBatch: GameCalendar.WeekdayOf(e.Day) == Weekday.Monday);
        }

        /// <summary>Competitor stock changes every day; a new trade batch arrives on Mondays.</summary>
        void Refresh(int day, bool newBatch)
        {
            var db = CarSalesServices.Database;
            if (!db || db.cars.Count == 0) return;
            state.competitorListings.Clear();
            foreach (var comp in MarketModel.Competitors)
                for (int i = 0; i < comp.Size; i++)
                {
                    var def = db.GetRandom();
                    var car = CarValueCalculator.GenerateRandom(def);
                    float cond = Mathf.Clamp(comp.Quality + UnityEngine.Random.Range(-15f, 15f), 20f, 98f);
                    CarParts.Randomize(car, cond);
                    int value = CarValueCalculator.MarketValue(car);
                    state.competitorListings.Add(new CompetitorListing
                    {
                        dealer = comp.Name, carId = def.carId, category = (int)def.category, price = MarketModel.CompetitorPrice(comp, value, rng),
                        condition = car.condition, mileage = car.mileage,
                    });
                }
            state.tradeBatches.RemoveAll(b => b.taken || b.expiresDay < day);
            if (newBatch || state.tradeBatches.Count == 0)
            {
                int n = UnityEngine.Random.Range(3, 7);
                var comp = MarketModel.Competitors[UnityEngine.Random.Range(0, MarketModel.Competitors.Length)];
                var b = new TradeBatch { id = Guid.NewGuid().ToString("N").Substring(0, 8), dealer = comp.Name, day = day, expiresDay = day + 6 };
                int total = 0;
                for (int i = 0; i < n; i++)
                {
                    var def = db.GetRandom();
                    float cond = UnityEngine.Random.Range(45f, 85f);
                    int miles = UnityEngine.Random.Range(30000, 180000);
                    b.carIds.Add(def.carId); b.conditions.Add(cond); b.mileages.Add(miles);
                    var probe = new CarInstance { carId = def.carId, condition = cond, cleanliness = 50f, mileage = miles };
                    total += CarValueCalculator.MarketValue(probe);
                }
                b.price = Mathf.RoundToInt(total * (1f - MarketModel.BatchDiscount(n)) / 100f) * 100;
                state.tradeBatches.Add(b);
            }
            OnChanged?.Invoke();
        }

        /// <summary>Buys a whole trade batch: the cars are delivered to free spots at your dealerships.</summary>
        public bool BuyBatch(TradeBatch b)
        {
            var ui = CarSalesServices.UI;
            if (b.taken) return false;
            int free = DealershipManager.TotalFreeSpots;
            if (free < b.carIds.Count) { ui.Error($"You need {b.carIds.Count} free parking spots (you have {free})."); return false; }
            if (!CarSalesServices.Economy.TrySpend(b.price, TxCategory.VehiclePurchase, $"Trade batch from {b.dealer} ({b.carIds.Count} cars)"))
            { ui.Error($"You need {UIFactory.Money(b.price)}."); return false; }
            var db = CarSalesServices.Database;
            int each = b.price / Mathf.Max(1, b.carIds.Count);
            for (int i = 0; i < b.carIds.Count; i++)
            {
                var def = db.Get(b.carIds[i]);
                if (!def) continue;
                var car = CarValueCalculator.GenerateRandom(def);
                CarParts.Randomize(car, b.conditions[i]);
                car.mileage = b.mileages[i];
                car.purchasePrice = each;
                car.inspected = true;
                car.state = CarValueCalculator.DefaultState(car);
                CarSalesServices.Vehicles.AddOwnedCar(car);
            }
            b.taken = true;
            CarSalesServices.DayCycle.RegisterPurchase(b.price);
            ui.Success("Batch bought", $"{b.carIds.Count} cars from {b.dealer} are on your lots.");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>The cheapest competitor price for the same model, or the same body type if nobody has the model.</summary>
        public CompetitorListing CheapestRival(CarDefinition def)
        {
            if (!def) return null;
            var same = state.competitorListings.Where(l => l.carId == def.carId).OrderBy(l => l.price).FirstOrDefault();
            return same ?? state.competitorListings.Where(l => l.category == (int)def.category).OrderBy(l => l.price).FirstOrDefault();
        }
    }
}
