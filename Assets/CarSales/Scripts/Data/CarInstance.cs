using System;
using System.Collections.Generic;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>One specific car (in the marketplace or owned). Saved to JSON.</summary>
    [Serializable]
    public class CarInstance
    {
        public string instanceId;
        public string carId;
        public float condition;      // 0-100 (TRUE condition)
        public float reportedCondition; // what the seller claims in the marketplace (can be a lie)
        public float cleanliness;    // 0-100
        public int mileage;
        public Color paintColor = Color.white;
        public int purchasePrice;
        public int repairSpent;
        public int listedPrice;
        public CarState state;
        public int parkingSpotIndex = -1;
        public float[] parts;           // per-group condition, kept in sync with `components` (see CarParts)
        public bool inspected;          // a paid/free inspection revealed the true condition

        // v4: physical location, fuel, personal car
        public CarLocation location = CarLocation.Spot;
        public string dealershipId;     // dealership whose spot it occupies (location == Spot)
        public Vector3 worldPosition;   // location == World
        public float worldRotationY;
        public float fuel = 100f;       // 0-100 %
        public bool personal;           // the player's own car - never sold to customers

        // v5: every individual component (engine internals, pads, each tyre, each panel...). See CarSales.Sim.ComponentCatalog.
        public List<ComponentState> components = new List<ComponentState>();

        // v5 phase 5: identity, paperwork, past, faults and what the player knows (see CarRecords).
        public VehicleIdentity identity = new VehicleIdentity();
        public DocumentSet documents = new DocumentSet();
        public HistoryRecord history = new HistoryRecord();
        public List<FaultInstance> faults = new List<FaultInstance>();
        public VehicleKnowledge knowledge = new VehicleKnowledge();
        public ListingInfo listing = new ListingInfo();   // phase 7b listing editor

        /// <summary>Display name of the car model (not saved).</summary>
        public string Name => CarSalesServices.Database && CarSalesServices.Database.Get(carId) ? CarSalesServices.Database.Get(carId).displayName : carId;
    }
}
