using CarSales.Foundation;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Tow trucks (move any of your cars to a dealership, workshop, car wash, fuel station or home) and taxis
    /// (move yourself). Both cost money based on distance.
    /// </summary>
    public class TowService : MonoBehaviour
    {
        [SerializeField] int towBaseFee = 120;
        [SerializeField] float towPerMeter = 0.8f;
        [SerializeField] int taxiBaseFee = 25;
        [SerializeField] float taxiPerMeter = 0.25f;

        public class Destination
        {
            public string name;
            public DealershipManager dealer;   // either a dealership...
            public ServiceZone zone;           // ...or a service zone
            public Vector3 Position => dealer ? dealer.transform.position : zone ? zone.transform.position : Vector3.zero;
        }

        void Awake() => CarSalesServices.Tow = this;

        public int TowCost(Vector3 from, Vector3 to) => Round10(towBaseFee + Flat(from, to) * towPerMeter);
        public int TaxiCost(Vector3 from, Vector3 to) => Round10(taxiBaseFee + Flat(from, to) * taxiPerMeter);
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static int Round10(float v) => Mathf.CeilToInt(v / 10f) * 10;

        public List<Destination> Destinations()
        {
            var list = new List<Destination>();
            foreach (var d in DealershipManager.Owned) list.Add(new Destination { name = d.displayName, dealer = d });
            foreach (var type in new[] { ServiceType.Workshop, ServiceType.CarWash, ServiceType.FuelStation })
            {
                var z = ServiceZone.Find(type);
                if (z) list.Add(new Destination { name = z.displayName, zone = z });
            }
            var home = CarSalesServices.Properties ? ServiceZone.Find(ServiceType.Home, CarSalesServices.Properties.ActiveHome) : null;
            if (home) list.Add(new Destination { name = "Home (" + home.displayName + ")", zone = home });
            return list;
        }

        public Destination DestinationFor(ServiceType type) =>
            Destinations().Find(d => d.zone && d.zone.type == type);

        public bool Tow(CarDisplay car, Destination dest, out string message)
        {
            message = "";
            if (!car || dest == null) { message = "Nothing to tow."; return false; }
            var drive = CarSalesServices.TestDrive;
            if (drive && drive.IsDriving && drive.CurrentCar == car) drive.EndDrive();
            int cost = TowCost(car.transform.position, dest.Position);

            ParkingSpot spot = null;
            Transform bay = null;
            if (dest.dealer)
            {
                spot = dest.dealer.GetFreeSpot();
                if (spot == null) { message = $"{dest.dealer.displayName} has no free parking spot."; return false; }
            }
            else
            {
                bay = dest.zone.FreeBay();
                if (bay == null) { message = $"{dest.name} is full right now."; return false; }
            }
            if (!CarSalesServices.Economy.TrySpend(cost, TxCategory.Transport, $"Tow truck - {car.Definition.displayName} to {dest.name}", car.Data.instanceId)) { message = $"The tow costs {UIFactory.Money(cost)} - not enough money."; return false; }
            CarSalesServices.DayCycle.RegisterExpense(cost);

            if (spot != null) CarSalesServices.Vehicles.PlaceAtSpot(car, dest.dealer, spot);
            else CarSalesServices.Vehicles.PlaceInWorld(car, bay.position, bay.eulerAngles.y);
            CarSalesServices.PlaySfx(Sfx.Purchase);
            message = $"Tow truck delivered the {car.Definition.displayName} to {dest.name} ({UIFactory.Money(cost)}).";
            return true;
        }

        public bool Taxi(MapPlace place, out string message)
        {
            message = "";
            var player = FindFirstObjectByType<PlayerInteractor>();
            if (!player || !place) { message = "No taxi available."; return false; }
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving) { message = "Get out of the car first (F)."; return false; }
            int cost = TaxiCost(player.transform.position, place.ArrivalPosition);
            if (!CarSalesServices.Economy.TrySpend(cost, TxCategory.Transport, $"Taxi to {place.placeName}")) { message = $"The taxi costs {UIFactory.Money(cost)} - not enough money."; return false; }
            CarSalesServices.DayCycle.RegisterExpense(cost);
            float minutes = Mathf.Clamp(Flat(player.transform.position, place.ArrivalPosition) / 8f, 5f, 40f);
            CarSalesServices.DayCycle.AdvanceTime(minutes / 60f);
            TeleportPlayer(place.ArrivalPosition, place.transform.eulerAngles.y);
            message = $"The taxi dropped you at {place.placeName} ({UIFactory.Money(cost)}, {minutes:0} min).";
            return true;
        }

        public static void TeleportPlayer(Vector3 pos, float yaw)
        {
            var player = FindFirstObjectByType<PlayerInteractor>();
            if (!player) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            player.transform.SetPositionAndRotation(pos + Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f));
            if (cc) cc.enabled = true;
        }
    }
}
