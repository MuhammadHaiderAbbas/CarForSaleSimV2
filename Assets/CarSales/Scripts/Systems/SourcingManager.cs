using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CarSales
{
    [Serializable] public class PendingLead { public string carId, name; public int persona, day; public float hour; }

    [Serializable]
    public class Warranty
    {
        public string carName, customer;
        public int soldDay, expiresDay, claimCost;
        public List<float> severities = new List<float>();
        public bool claimed;
    }

    [Serializable]
    public class SourcingState
    {
        public int version = 1;
        public List<PrivateAd> ads = new List<PrivateAd>();
        public List<CarInstance> adCars = new List<CarInstance>();
        public List<PendingLead> leads = new List<PendingLead>();
        public List<Warranty> warranties = new List<Warranty>();
        public int lastAdDay;
    }

    /// <summary>
    /// Phase 7b sourcing and after-sale (spec §6.8, §6.10): private-seller classifieds with viewings at a house in town,
    /// online listings that bring enquiries (leads who come to view a specific car), and warranties with claims.
    /// Saved as module "sourcing". Added by CarSalesBootstrap if missing.
    /// </summary>
    public class SourcingManager : MonoBehaviour
    {
        SourcingState state = new SourcingState();
        readonly System.Random rng = new System.Random();
        GameObject shownSeller;
        CarDisplay shownCar;
        string shownAd;
        float checkTimer;

        public SourcingState State => state;
        public event Action OnChanged;

        void Awake() => CarSalesServices.Sourcing = this;
        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);
        void Start() { if (state.ads.Count == 0) PostAds(CarRecords.Today, 4); }

        public void SetState(SourcingState s)
        {
            HideViewing();
            state = s ?? new SourcingState();
            foreach (var c in state.adCars) CarParts.EnsureParts(c);
            if (state.ads.Count == 0) PostAds(CarRecords.Today, 4);
            OnChanged?.Invoke();
        }

        public void ResetState() { HideViewing(); state = new SourcingState(); OnChanged?.Invoke(); }

        public PrivateAd Ad(string id) => state.ads.Find(a => a.id == id && !a.sold);
        public CarInstance CarOf(PrivateAd ad) => ad == null ? null : state.adCars.Find(c => c.instanceId == ad.carId);

        // ------------------------------------------------------------------ CLASSIFIEDS
        void PostAds(int day, int count)
        {
            var db = CarSalesServices.Database;
            if (!db || db.cars.Count == 0) return;
            for (int i = 0; i < count; i++)
            {
                var car = CarValueCalculator.GenerateRandom(db.GetRandom());
                car.state = CarState.InTransit;
                car.fuel = Random.Range(20f, 80f);
                var ad = new PrivateAd
                {
                    id = Guid.NewGuid().ToString("N").Substring(0, 8), carId = car.instanceId, seller = PrivateSellers.Name(rng),
                    honesty = (float)rng.NextDouble(), postedDay = day, expiresDay = day + 4, patience = 2 + rng.Next(3),
                };
                car.reportedCondition = Mathf.Clamp(car.condition + (1f - ad.honesty) * Random.Range(5f, 25f), 5f, 100f);
                int value = CarValueCalculator.MarketValue(car, useReported: true);
                ad.asking = PrivateSellers.Asking(value, rng);
                ad.minPrice = PrivateSellers.MinPrice(ad.asking, rng);
                var def = db.Get(car.carId);
                ad.claim = (car.reportedCondition >= 80f ? "Excellent condition" : car.reportedCondition >= 60f ? "Good runner" : "Needs a little TLC") +
                           $", {car.mileage / 1000}k km" + (car.documents.service == ServiceHistory.Full || ad.honesty < 0.4f ? ", full service history" : "") + ". " +
                           (ad.honesty < 0.3f ? "No time wasters!" : "Viewing welcome.");
                car.purchasePrice = ad.asking;
                state.adCars.Add(car);
                state.ads.Add(ad);
            }
            state.lastAdDay = day;
        }

        /// <summary>Books the next free viewing slot (from an hour from now, 09:00-19:00) at a seller's house.</summary>
        public bool BookViewing(PrivateAd ad)
        {
            var dc = CarSalesServices.DayCycle;
            float h = dc ? dc.HourOfDay : 9f;
            int day = CarRecords.Today;
            float hour = Mathf.Ceil(h + 1f);
            if (hour < 9f) hour = 9f;
            if (hour > 19f) { day++; hour = 10f; }
            ad.appointmentDay = day;
            ad.appointmentHour = hour;
            int spots = Mathf.Max(1, SellerSpot.All.Count);
            var used = state.ads.Where(a => a != ad && a.appointmentDay == day && Mathf.Abs(a.appointmentHour - hour) < 2f).Select(a => a.spot).ToList();
            ad.spot = Enumerable.Range(0, spots).FirstOrDefault(i => !used.Contains(i));
            CarSalesServices.UI.Success("Viewing booked", $"{ad.seller}: {Address(ad)}, {(day == CarRecords.Today ? "today" : "tomorrow")} at {hour:00}:00. They'll wait about two hours.");
            OnChanged?.Invoke();
            return true;
        }

        public string Address(PrivateAd ad)
        {
            var spot = ad.spot >= 0 && ad.spot < SellerSpot.All.Count ? SellerSpot.All[ad.spot] : null;
            return spot ? spot.address : "near the Used Car Market";
        }

        public Vector3? SpotPosition(PrivateAd ad, out Quaternion rot, out Vector3 sellerPos)
        {
            rot = Quaternion.identity; sellerPos = Vector3.zero;
            var spot = ad.spot >= 0 && ad.spot < SellerSpot.All.Count ? SellerSpot.All[ad.spot] : null;
            if (spot)
            {
                rot = spot.transform.rotation;
                sellerPos = spot.sellerPoint ? spot.sellerPoint.position : spot.transform.position + spot.transform.right * 2.2f;
                return spot.transform.position;
            }
            var market = MapPlace.All.FirstOrDefault(m => m && m.placeName.Contains("Used Car Market"));
            if (!market) return null;
            var p = market.ArrivalPosition + Vector3.right * (4f * Mathf.Max(0, ad.spot) + 6f);
            sellerPos = p + Vector3.right * 2.2f;
            return p;
        }

        /// <summary>Viewing window: from the booked hour to two hours later.</summary>
        static bool InWindow(PrivateAd a, int day, float hour) => a.appointmentDay == day && hour >= a.appointmentHour - 0.25f && hour <= a.appointmentHour + 2f;

        void Update()
        {
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 1f;
            var dc = CarSalesServices.DayCycle;
            if (!dc) return;
            int day = CarRecords.Today;
            float hour = dc.HourOfDay;
            var due = state.ads.FirstOrDefault(a => !a.sold && InWindow(a, day, hour));
            if (due == null) { if (shownAd != null) HideViewing(); return; }
            if (shownAd != due.id) ShowViewing(due);
        }

        void ShowViewing(PrivateAd ad)
        {
            HideViewing();
            var car = CarOf(ad);
            var prefab = CarSalesServices.Vehicles ? CarSalesServices.Vehicles.DisplayPrefab : null;
            var pos = SpotPosition(ad, out var rot, out var sellerPos);
            if (car == null || !prefab || pos == null) return;
            shownAd = ad.id;
            shownCar = Instantiate(prefab, pos.Value, rot);
            shownCar.InitializeForSale(car);
            shownCar.MarkPrivateAd(ad.id, ad.asking);
            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = "Seller_" + ad.seller;
            npc.layer = 20;
            npc.transform.position = sellerPos + Vector3.up * 1f;
            var r = npc.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader);
            var c = Color.HSVToRGB((float)rng.NextDouble(), 0.5f, 0.75f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            r.sharedMaterial = m;
            var look = AssetRegistry.RandomCustomerModel();
            if (look)
            {
                r.enabled = false;
                var v = Instantiate(look, npc.transform);
                v.transform.localPosition = Vector3.down;
                foreach (var col in v.GetComponentsInChildren<Collider>()) Destroy(col);
            }
            var s = npc.AddComponent<SellerNpc>();
            s.adId = ad.id; s.sellerName = ad.seller; s.car = shownCar;
            shownSeller = npc;
            MinimapMarker.Attach(npc, UIFactory.Gold, 3f);
        }

        public void HideViewing()
        {
            if (shownCar) Destroy(shownCar.gameObject);
            if (shownSeller) Destroy(shownSeller);
            shownCar = null; shownSeller = null; shownAd = null;
        }

        public CarDisplay ShownCar(string adId) => shownAd == adId ? shownCar : null;

        /// <summary>You bought it: the car becomes yours where it stands.</summary>
        public bool Buy(PrivateAd ad, int price)
        {
            var car = CarOf(ad);
            if (car == null) return false;
            if (!CarSalesServices.Economy.TrySpend(price, TxCategory.VehiclePurchase, $"Private sale - {ad.seller} - {car.Name}", car.instanceId))
            { CarSalesServices.UI.Error("Not enough money."); return false; }
            var at = shownCar ? shownCar.transform : null;
            Vector3 pos = at ? at.position : Vector3.zero;
            float yaw = at ? at.eulerAngles.y : 0f;
            ad.sold = true;
            state.adCars.Remove(car);
            HideViewing();
            car.purchasePrice = price;
            car.state = CarValueCalculator.DefaultState(car);
            car.inspected = true;
            CarSalesServices.Vehicles.AddOwnedCar(car, pos, yaw);
            CarSalesServices.DayCycle.RegisterPurchase(price);
            if (CarSalesServices.Progress) CarSalesServices.Progress.RegisterPurchase();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            string v5c = car.documents.v5cPresent ? $"{ad.seller} hands you the logbook (V5C) and {car.documents.keys} key{(car.documents.keys == 1 ? "" : "s")}."
                                                   : $"{ad.seller} can't find the logbook. Order a replacement in the Workshop app.";
            CarSalesServices.UI.Success("Bought privately", $"The {car.Name} is yours for {UIFactory.Money(price)}. {v5c} Drive it away (F) or tow it.");
            OnChanged?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ LEADS
        bool HasStaff(StaffRole r) => CarSalesServices.Staff && CarSalesServices.Staff.Working(r).Any();

        public float ListingQuality(CarInstance c)
        {
            var l = c.listing ?? new ListingInfo();
            int value = Mathf.Max(1, CarValueCalculator.MarketValue(c));
            return Listings.Quality(l.photos, l.highlights.Count(h => h != (int)Highlight.FaultsListed), l.highlights.Contains((int)Highlight.FaultsListed),
                                    c.listedPrice / (float)value, CarSalesServices.Crm ? CarSalesServices.Crm.AverageStars : 0f);
        }

        public float ExpectedLeads(CarInstance c) => c.listing != null && c.listing.online
            ? Listings.LeadsPerDay(ListingQuality(c), HasStaff(StaffRole.Receptionist)) * (CarSalesServices.Business ? CarSalesServices.Business.LeadsFactor : 1f) : 0f;

        /// <summary>A booked viewing for a car at this dealership that's due now, or null.</summary>
        public PendingLead TakeLead(DealershipManager dealer, float hour)
        {
            int day = CarRecords.Today;
            var lead = state.leads.FirstOrDefault(l => l.day == day && hour >= l.hour && CarAt(l.carId, dealer));
            if (lead != null) { state.leads.Remove(lead); OnChanged?.Invoke(); }
            return lead;
        }

        static bool CarAt(string carId, DealershipManager dealer)
        {
            var c = CarSalesServices.Inventory ? CarSalesServices.Inventory.OwnedCars.Find(x => x.instanceId == carId) : null;
            return c != null && c.state == CarState.Listed && c.dealershipId == dealer.dealershipId;
        }

        // ------------------------------------------------------------------ WARRANTIES
        public void RegisterWarranty(CarInstance car, string customer)
        {
            var w = new Warranty { carName = car.Name, customer = customer, soldDay = CarRecords.Today, expiresDay = CarRecords.Today + AfterSale.WarrantyDays };
            foreach (var f in car.faults) w.severities.Add(f.severity);
            w.claimCost = car.faults.Count == 0 ? 0 : car.faults.Max(f => CarRecords.RepairEstimate(car, f));
            state.warranties.Add(w);
        }

        // ------------------------------------------------------------------ DAILY
        void OnDayStarted(DayStarted e)
        {
            // Classifieds
            var expired = state.ads.Where(a => a.sold || a.expiresDay < e.Day).ToList();
            foreach (var a in expired) { state.ads.Remove(a); state.adCars.RemoveAll(c => c.instanceId == a.carId); }
            PostAds(e.Day, Random.Range(2, 4));

            // Online enquiries → booked viewings at your dealership today.
            state.leads.RemoveAll(l => l.day < e.Day);
            var inv = CarSalesServices.Inventory;
            if (inv)
                foreach (var c in inv.OwnedCars.Where(x => x.state == CarState.Listed && x.listing != null && x.listing.online))
                {
                    float expected = ExpectedLeads(c);
                    int n = 0;
                    while (rng.NextDouble() < expected - n && n < 3) n++;
                    for (int i = 0; i < n; i++)
                    {
                        var persona = Buyers.Pick(rng);
                        var lead = new PendingLead { carId = c.instanceId, name = PrivateSellers.Name(rng), persona = (int)persona.Id, day = e.Day, hour = 10f + rng.Next(8) };
                        state.leads.Add(lead);
                        c.listing.leads++;
                        if (CarSalesServices.Messages)
                            CarSalesServices.Messages.Add(MessageType.Info, $"Enquiry: {c.Name}", $"{lead.name} saw your online advert and is coming to view the {c.Name} today at {lead.hour:00}:00.");
                    }
                }

            // Warranty claims
            foreach (var w in state.warranties.Where(x => !x.claimed && x.expiresDay >= e.Day && x.claimCost > 0).ToList())
            {
                if (rng.NextDouble() >= AfterSale.ClaimChancePerDay(w.severities)) continue;
                w.claimed = true;
                CarSalesServices.Economy.ForceSpend(w.claimCost, TxCategory.Warranty, $"Warranty claim - {w.customer} - {w.carName}");
                string msg = $"{w.customer}'s {w.carName} broke down under your warranty. The repair cost you {UIFactory.Money(w.claimCost)} (their review stays good).";
                CarSalesServices.UI?.Toast(ToastType.Warning, "Warranty claim", msg);
                if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Info, "Warranty claim", msg);
            }
            state.warranties.RemoveAll(w => w.expiresDay < e.Day - 30);
            OnChanged?.Invoke();
        }
    }
}
