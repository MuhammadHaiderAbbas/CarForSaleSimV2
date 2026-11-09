using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Counter at a service location (workshop, car wash, fuel station, auction).
    /// Serves your cars that are parked inside the location's ServiceZone.
    /// </summary>
    public class ServiceDesk : MonoBehaviour, IInteractable
    {
        public ServiceZone zone;
        public ServiceType type;

        public bool CanInteract() => true;

        public string GetInteractionText()
        {
            switch (type)
            {
                case ServiceType.Workshop: return "[E] Mechanic - repair a car parked in a service bay";
                case ServiceType.CarWash: return "[E] Car wash - wash a car parked in the wash bay";
                case ServiceType.FuelStation: return $"[E] Fuel kiosk - fill up cars at the pumps (${CarSalesServices.Vehicles.FuelPricePerPercent:0.##}/%)";
                case ServiceType.Auction: return "[E] Auction desk: register, catalogue, invoices (sales in the hall behind)";
                default: return "[E] Service";
            }
        }

        public void Interact(GameObject interactor)
        {
            if (type == ServiceType.Auction) { CarSalesServices.UI.auction.Show(); return; }
            var car = CarSalesServices.Vehicles.FirstCarIn(zone);
            if (!car)
            {
                CarSalesServices.UI.Error(type == ServiceType.FuelStation
                    ? "Park one of your cars at the pumps first (drive it here or call a tow with P)."
                    : "Bring one of your cars into the bay first: drive it here, or tow it with the phone (P).");
                return;
            }
            switch (type)
            {
                case ServiceType.Workshop: CarSalesServices.UI.garage.Show(car); break;
                case ServiceType.CarWash: CarSalesServices.UI.carWash.Show(car); break;
                case ServiceType.FuelStation: CarSalesServices.Vehicles.Refuel(car); break;
            }
        }
    }
}
