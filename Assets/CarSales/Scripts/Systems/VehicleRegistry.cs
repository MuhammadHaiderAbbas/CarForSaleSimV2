using CarSales.Foundation;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Owns the GameObjects of every car you own, wherever it is: on a dealership spot, parked in the street,
    /// in the workshop, at home... Also handles parking, refuelling and selling.
    /// </summary>
    public class VehicleRegistry : MonoBehaviour
    {
        [SerializeField] CarDisplay carDisplayPrefab;
        public CarDisplay DisplayPrefab => carDisplayPrefab;
        [SerializeField] float fuelPricePerPercent = 0.9f;   // a full tank (100%) = $90

        readonly Dictionary<string, CarDisplay> displays = new Dictionary<string, CarDisplay>();
        public IEnumerable<CarDisplay> All => displays.Values.Where(d => d);
        /// <summary>Phase 8: today's fuel price (the world's fuel index moves it daily).</summary>
        public float FuelPricePerPercent => fuelPricePerPercent * (CarSalesServices.World ? CarSalesServices.World.FuelIndex : 1f);
        public CarDisplay Prefab => carDisplayPrefab;

        void Awake() => CarSalesServices.Vehicles = this;

        public CarDisplay GetDisplay(string id) => displays.TryGetValue(id, out var d) ? d : null;

        // ------------------------------------------------------------------ SPAWNING
        public CarDisplay Spawn(CarInstance car)
        {
            Despawn(car.instanceId);
            var d = Instantiate(carDisplayPrefab);
            d.Initialize(car);
            displays[car.instanceId] = d;
            PlaceFromData(d);
            return d;
        }

        public void Despawn(string id)
        {
            if (!displays.TryGetValue(id, out var d)) return;
            foreach (var dealer in DealershipManager.All) dealer.ReleaseSpot(d);
            if (d) Destroy(d.gameObject);
            displays.Remove(id);
        }

        public void ClearAll()
        {
            foreach (var d in displays.Values) if (d) Destroy(d.gameObject);
            displays.Clear();
            foreach (var dealer in DealershipManager.All) dealer.ReleaseAllSpots();
        }

        /// <summary>Re-creates all car GameObjects from inventory data (after load / new game).</summary>
        public void RebuildAll()
        {
            ClearAll();
            foreach (var car in CarSalesServices.Inventory.OwnedCars) Spawn(car);
        }

        void PlaceFromData(CarDisplay d)
        {
            var car = d.Data;
            if (car.location == CarLocation.Spot)
            {
                var dealer = DealershipManager.Find(car.dealershipId) ?? DealershipManager.Main;
                var spot = dealer ? dealer.GetSpot(car.parkingSpotIndex) : null;
                if (spot == null || !spot.IsFree || !spot.IsUnlocked) spot = dealer ? dealer.GetFreeSpot() : null;
                if (spot == null)
                {
                    var any = DealershipManager.FindFreeSpotAnywhere(out var otherDealer);
                    if (any != null) { PlaceAtSpot(d, otherDealer, any); return; }
                    // No room anywhere: leave it in the street next to the main dealership.
                    var basePos = DealershipManager.Main ? DealershipManager.Main.transform.position : Vector3.zero;
                    PlaceInWorld(d, basePos + new Vector3(Random.Range(-14f, 14f), 0.05f, -19f), 90f);
                    return;
                }
                PlaceAtSpot(d, dealer, spot);
            }
            else PlaceInWorld(d, car.worldPosition, car.worldRotationY);
        }

        public void PlaceAtSpot(CarDisplay d, DealershipManager dealer, ParkingSpot spot)
        {
            foreach (var other in DealershipManager.All) other.ReleaseSpot(d);
            d.transform.SetPositionAndRotation(spot.transform.position, spot.transform.rotation);
            spot.Occupant = d;
            d.Data.location = CarLocation.Spot;
            d.Data.dealershipId = dealer.dealershipId;
            d.Data.parkingSpotIndex = spot.index;
            d.Refresh();
        }

        public void PlaceInWorld(CarDisplay d, Vector3 pos, float rotY)
        {
            foreach (var other in DealershipManager.All) other.ReleaseSpot(d);
            d.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, rotY, 0f));
            d.Data.location = CarLocation.World;
            d.Data.worldPosition = pos;
            d.Data.worldRotationY = rotY;
            d.Data.parkingSpotIndex = -1;
            if (d.Data.state == CarState.Listed)
            {
                d.Data.state = CarValueCalculator.DefaultState(d.Data);
                CarSalesServices.UI.Notify($"{d.Definition.displayName} left the dealership, so it's no longer listed for sale.");
            }
            d.Refresh();
        }

        /// <summary>Adds a newly bought car to the inventory and puts it on a free dealership spot (or the given world position).</summary>
        public CarDisplay AddOwnedCar(CarInstance car, Vector3? worldPos = null, float rotY = 0f)
        {
            CarSalesServices.Inventory.Add(car);
            if (worldPos.HasValue)
            {
                car.location = CarLocation.World;
                car.worldPosition = worldPos.Value;
                car.worldRotationY = rotY;
            }
            else
            {
                var spot = DealershipManager.FindFreeSpotAnywhere(out var dealer);
                car.location = CarLocation.Spot;
                car.dealershipId = dealer ? dealer.dealershipId : null;
                car.parkingSpotIndex = spot ? spot.index : -1;
            }
            return Spawn(car);
        }

        // ------------------------------------------------------------------ QUERIES
        public CarDisplay FirstCarIn(ServiceZone zone)
        {
            if (!zone) return null;
            foreach (var d in All)
                if (d.gameObject.activeInHierarchy && zone.Contains(d.transform.position)) return d;
            return null;
        }

        public string LocationText(CarInstance car)
        {
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving && CarSalesServices.TestDrive.CurrentCar && CarSalesServices.TestDrive.CurrentCar.Data == car)
                return "Driving";
            if (car.location == CarLocation.Spot)
            {
                var dealer = DealershipManager.Find(car.dealershipId);
                return $"{(dealer ? dealer.displayName : "Dealership")} spot #{car.parkingSpotIndex + 1}";
            }
            var zone = ServiceZone.At(car.worldPosition);
            return zone ? zone.displayName : "Parked on the street";
        }

        // ------------------------------------------------------------------ SERVICES
        public int RefuelCost(CarInstance car) => Mathf.CeilToInt((100f - car.fuel) * FuelPricePerPercent);

        public bool Refuel(CarDisplay car)
        {
            int cost = RefuelCost(car.Data);
            if (cost <= 0) { CarSalesServices.UI.Notify("The tank is already full."); return false; }
            if (!CarSalesServices.Economy.TrySpend(cost, TxCategory.Fuel, $"Fuel - {car.Definition.displayName}", car.Data.instanceId))
            {
                // Buy as much as you can afford
                int affordable = Mathf.Max(0, CarSalesServices.Economy.Money);
                if (affordable <= 0) { CarSalesServices.UI.Error("Not enough money for fuel!"); return false; }
                CarSalesServices.Economy.TrySpend(affordable, TxCategory.Fuel, $"Fuel (partial) - {car.Definition.displayName}", car.Data.instanceId);
                car.Data.fuel = Mathf.Min(100f, car.Data.fuel + affordable / FuelPricePerPercent);
                cost = affordable;
            }
            else car.Data.fuel = 100f;
            CarSalesServices.DayCycle.RegisterExpense(cost);
            CarSalesServices.PlaySfx(Sfx.Purchase);
            CarSalesServices.UI.Success("Refuelled", $"{car.Definition.displayName}: tank at {car.Data.fuel:0}% (paid {UIFactory.Money(cost)}).");
            car.Refresh();
            return true;
        }

        // ------------------------------------------------------------------ SALES
        public void CompleteSale(CarDisplay car, int price)
        {
            var d = car.Data;
            int value = CarValueCalculator.MarketValue(d);
            int profit = price - d.purchasePrice - d.repairSpent;
            CarSalesServices.Economy.AddMoney(price, TxCategory.VehicleSale, $"Sold {car.Definition.displayName}", d.instanceId);
            CarSalesServices.Economy.ChangeReputation(price <= value ? 2f : 0.5f);
            RemoveSoldCar(car, price, profit);
            CarSalesServices.PlaySfx(Sfx.Success);
            CarSalesServices.UI.Success("Car sold!", $"{car.Definition.displayName} sold for {UIFactory.Money(price)}. Profit: {UIFactory.Money(profit)}");
        }

        /// <summary>Instant wholesale sale (75% of market value). Doesn't change reputation.</summary>
        public void QuickSell(CarDisplay car)
        {
            if (!car) return;
            if (car.Data.personal) { CarSalesServices.UI.Error("You can't sell your personal car."); return; }
            var d = car.Data;
            int price = CarValueCalculator.QuickSellPrice(d);
            int profit = price - d.purchasePrice - d.repairSpent;
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving && CarSalesServices.TestDrive.CurrentCar == car)
                CarSalesServices.TestDrive.EndDrive();
            CarSalesServices.Economy.AddMoney(price, TxCategory.VehicleSale, $"Wholesale - {car.Definition.displayName}", d.instanceId);
            RemoveSoldCar(car, price, profit);
            CarSalesServices.PlaySfx(Sfx.Coin);
            CarSalesServices.UI.Success("Wholesale complete", $"{car.Definition.displayName} sold to the wholesaler for {UIFactory.Money(price)}.");
        }

        void RemoveSoldCar(CarDisplay car, int price, int profit)
        {
            var d = car.Data;
            d.state = CarState.Sold;
            if (CarSalesServices.Business) CarSalesServices.Business.RecordSale(d, price);   // phase 9: accounts, VAT, analytics
            CarSalesServices.Inventory.Remove(d.instanceId);
            Despawn(d.instanceId);
            CarSalesServices.DayCycle.RegisterSale(price, profit);
            if (CarSalesServices.Progress) CarSalesServices.Progress.RegisterSale(price, profit);
        }
    }
}
