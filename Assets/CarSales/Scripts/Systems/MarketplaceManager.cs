using CarSales.Foundation;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Two places to buy cars:
    ///  - Online (dealer computer): delivered to your dealership for a delivery fee.
    ///  - The physical Used Car Market lot: cheaper, free inspection, you drive the car away yourself.
    /// Both get new cars every morning.
    /// </summary>
    public class MarketplaceManager : MonoBehaviour
    {
        [SerializeField] int listingsPerDay = 8;
        [SerializeField] Vector2 priceVsMarket = new Vector2(0.70f, 0.95f);
        [SerializeField] int lotListingsPerDay = 8;
        [SerializeField] Vector2 lotPriceVsMarket = new Vector2(0.62f, 0.88f);
        [SerializeField] int deliveryFee = 300;

        public List<CarInstance> Listings { get; private set; } = new List<CarInstance>();
        public List<CarInstance> LotListings { get; private set; } = new List<CarInstance>();
        public int DeliveryFee => deliveryFee;
        public event Action OnListingsChanged;

        void Awake() => CarSalesServices.Marketplace = this;
        void Start() { if (Listings.Count == 0) RefreshListings(); }

        public void RefreshListings()
        {
            Listings = Generate(listingsPerDay, priceVsMarket);
            LotListings = Generate(lotListingsPerDay, lotPriceVsMarket);
            OnListingsChanged?.Invoke();
        }

        List<CarInstance> Generate(int count, Vector2 priceRange)
        {
            var list = new List<CarInstance>();
            var db = CarSalesServices.Database;
            if (db == null || db.cars.Count == 0) return list;
            for (int i = 0; i < count; i++)
            {
                var car = CarValueCalculator.GenerateRandom(db.GetRandom());
                car.fuel = UnityEngine.Random.Range(10f, 70f);
                car.purchasePrice = Mathf.RoundToInt(CarValueCalculator.MarketValue(car, useReported: true) *
                                                     UnityEngine.Random.Range(priceRange.x, priceRange.y) / 100f) * 100;
                list.Add(car);
            }
            return list;
        }

        public void SetListings(List<CarInstance> listings, List<CarInstance> lot)
        {
            if (listings == null || listings.Count == 0) { RefreshListings(); return; }
            foreach (var l in listings) Migrate(l);
            Listings = listings;
            LotListings = lot ?? new List<CarInstance>();
            foreach (var l in LotListings) Migrate(l);
            OnListingsChanged?.Invoke();
        }

        static void Migrate(CarInstance l)
        {
            if (l.reportedCondition <= 0f) l.reportedCondition = l.condition;
            CarParts.EnsureParts(l);
        }

        /// <summary>Online purchase: paid + delivery fee, delivered to a free dealership spot.</summary>
        public bool TryBuy(CarInstance listing, out string message)
        {
            if (DealershipManager.FindFreeSpotAnywhere(out _) == null)
            { message = "No free parking spot at your dealerships! Sell a car, expand the lot, or buy another dealership."; return false; }
            int total = listing.purchasePrice + deliveryFee;
            if (!CarSalesServices.Economy.CanAfford(total)) { message = $"Not enough money ({UIFactory.Money(total)} incl. delivery)."; return false; }
            CarSalesServices.Economy.TrySpend(listing.purchasePrice, TxCategory.VehiclePurchase, $"Bought online - {listing.Name}", listing.instanceId);
            CarSalesServices.Economy.TrySpend(deliveryFee, TxCategory.Delivery, $"Delivery - {listing.Name}", listing.instanceId);

            listing.state = CarValueCalculator.DefaultState(listing);
            Listings.Remove(listing);
            Finish(listing, total);
            CarSalesServices.Vehicles.AddOwnedCar(listing);
            OnListingsChanged?.Invoke();
            message = $"Delivered to your dealership (delivery {UIFactory.Money(deliveryFee)}).";
            return Reveal(listing, ref message);
        }

        /// <summary>Buying at the Used Car Market: the car stays where it stands - drive it away (F) or tow it.</summary>
        public bool TryBuyFromLot(CarDisplay listingDisplay, out string message)
        {
            var listing = listingDisplay.Data;
            if (!CarSalesServices.Economy.TrySpend(listing.purchasePrice, TxCategory.VehiclePurchase, $"Used Car Market - {listing.Name}", listing.instanceId)) { message = "Not enough money!"; return false; }
            listing.state = CarValueCalculator.DefaultState(listing);
            Vector3 pos = listingDisplay.transform.position;
            float yaw = listingDisplay.transform.eulerAngles.y;
            LotListings.Remove(listing);
            Finish(listing, listing.purchasePrice);
            CarSalesServices.Vehicles.AddOwnedCar(listing, pos, yaw);
            OnListingsChanged?.Invoke();   // the market rebuilds its displays (this one disappears)
            message = "It's yours! Get in with F and drive it to your dealership, or call a tow truck (P).";
            return Reveal(listing, ref message);
        }

        void Finish(CarInstance car, int paid)
        {
            CarSalesServices.DayCycle.RegisterPurchase(paid);
            if (CarSalesServices.Progress) CarSalesServices.Progress.RegisterPurchase();
            CarSalesServices.PlaySfx(Sfx.Purchase);
        }

        static bool Reveal(CarInstance listing, ref string message)
        {
            float lie = listing.reportedCondition - listing.condition;
            listing.inspected = true;
            if (lie >= 5f) message += $" Real condition is {listing.condition:0}% (seller claimed {listing.reportedCondition:0}%).";
            return true;
        }
    }
}
