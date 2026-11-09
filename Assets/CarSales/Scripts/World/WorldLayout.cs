using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    public enum SiteTemplate { Dealership = 0, PremiumDealership = 1, Workshop = 2, CarWash = 3, FuelStation = 4, PartsShop = 5, House = 6, Apartments = 7, Building = 8 }

    /// <summary>A road you drew in the World Builder (phase 8b).</summary>
    [Serializable]
    public class UserRoad
    {
        public CarSales.Sim.V2 a, b;
        public bool trees = true, lamps = true;
    }

    [Serializable]
    public class SiteRecord
    {
        public string id, name;
        public SiteTemplate template;
        public string buildingAssetId = "";   // AssetRegistry id ("" = the standard glass showroom)
        public string interiorAssetId = "";   // AssetRegistry id of an Interior placed inside the showroom
        public Vector3 position;
        public float yaw;
        public int price, dailyCost;
        public SiteFunction function;
    }

    /// <summary>
    /// The sites in a town (spec §7.3). Data/WorldLayout.asset keeps the sites you add in the World Builder so
    /// Build Everything rebuilds them; Data/Layout_DefaultTown.asset lists the generated v4 town.
    /// </summary>
    [CreateAssetMenu(menuName = "CarSales/World Layout", fileName = "WorldLayout")]
    public class WorldLayout : ScriptableObject
    {
        public List<SiteRecord> sites = new List<SiteRecord>();
        public List<CarSales.Sim.RoadSegment> roads = new List<CarSales.Sim.RoadSegment>();
        public List<UserRoad> userRoads = new List<UserRoad>();

        /// <summary>The town's road network: the default roads plus yours (split where they meet).</summary>
        public List<CarSales.Sim.RoadSegment> Network()
        {
            var net = roads.Count > 0 ? new List<CarSales.Sim.RoadSegment>(roads) : CarSales.Sim.RoadGraph.DefaultTown();
            foreach (var r in userRoads) CarSales.Sim.RoadEditing.Add(net, r.a, r.b);
            return net;
        }
    }
}
