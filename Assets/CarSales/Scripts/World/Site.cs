using UnityEngine;

namespace CarSales
{
    public enum SiteFunction { Dealership = 0, ServiceCentre = 1, Workshop = 2, CarWash = 3, FuelStation = 4, AuctionCenter = 5, UsedCarMarket = 6, House = 7, Investment = 8, PartsShop = 9, Street = 10, Other = 11 }

    /// <summary>
    /// A place in the town (spec §7.3): what it's for, its entrance, and - for dealerships you add with the World
    /// Builder - its price and running cost (registered as a property you can buy).
    /// </summary>
    public class Site : MonoBehaviour
    {
        public string siteId;
        public string displayName = "Site";
        public SiteFunction function;
        public Transform entrance;
        public int price;          // 0 = yours from the start
        public int dailyCost;
        public bool userBuilt;
        public string buildingAssetId = "";

        public Vector3 EntrancePosition => entrance ? entrance.position : transform.position;

        void Start()
        {
            if (!userBuilt || price <= 0 || string.IsNullOrEmpty(siteId)) return;
            var props = CarSalesServices.Properties;
            if (!props) return;
            switch (function)
            {
                case SiteFunction.Dealership:
                    props.RegisterProperty(new PropertyInfo { id = siteId, displayName = displayName, type = PropertyType.Dealership, price = price, dailyCost = dailyCost,
                                                              description = $"Dealership you built with the World Builder. Rent ${dailyCost:N0}/day." });
                    break;
                case SiteFunction.House:
                    props.RegisterProperty(new PropertyInfo { id = siteId, displayName = displayName, type = PropertyType.Home, price = price, wakeHour = 7f, dailyReputation = 0.3f,
                                                              description = "A house you placed with the World Builder. Wake up at 07:00 (+0.3 reputation a day)." });
                    break;
                case SiteFunction.Investment:
                    props.RegisterProperty(new PropertyInfo { id = siteId, displayName = displayName, type = PropertyType.Investment, price = price, dailyIncome = dailyCost,
                                                              description = $"Rental building you placed with the World Builder. Earns ${dailyCost:N0} a day." });
                    break;
            }
        }
    }
}
