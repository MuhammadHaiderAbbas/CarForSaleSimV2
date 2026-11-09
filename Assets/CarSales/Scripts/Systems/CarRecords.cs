using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Runtime glue for phase 5: gives every car (new listings and cars from older saves) a VIN, plate, paperwork,
    /// history and hidden faults, generated once from the car's id so they never change.
    /// </summary>
    public static class CarRecords
    {
        public static int Today => CarSalesServices.DayCycle ? CarSalesServices.DayCycle.CurrentDay : 1;

        public static int ModelYear(CarInstance c)
        {
            var def = CarSalesServices.Database ? CarSalesServices.Database.Get(c.carId) : null;
            return def ? def.year : VehicleRecords.GameStartYear - 6;
        }

        public static void Ensure(CarInstance c)
        {
            if (c == null) return;
            if (c.documents == null) c.documents = new DocumentSet();
            if (c.history == null) c.history = new HistoryRecord();
            if (c.identity == null) c.identity = new VehicleIdentity();
            if (c.knowledge == null) c.knowledge = new VehicleKnowledge();
            if (c.faults == null) c.faults = new List<FaultInstance>();
            if (c.documents.generated || c.components == null || c.components.Count == 0) return;

            var rng = new System.Random(VehicleRecords.Seed(c.instanceId));
            int year = ModelYear(c);
            if (!c.identity.IsSet) c.identity = VehicleRecords.NewIdentity(year, rng);
            VehicleRecords.Generate(c.documents, c.history, year, c.mileage, Today, rng);
            // A repaired write-off sometimes shows as a paint mismatch.
            c.faults.AddRange(FaultModel.Generate(c.components, c.mileage, rng));
            if (c.history.writeOff != WriteOff.None && rng.NextDouble() < 0.6 && !c.faults.Exists(f => f.Id == FaultId.PaintMismatch))
                c.faults.Add(new FaultInstance(FaultId.PaintMismatch, 0.5f));
            FaultModel.ApplyHealthCaps(c.components, c.faults);
            CarParts.Recalculate(c);
        }

        public static void ClearFaultsInGroup(CarInstance c, ComponentGroup group)
        {
            if (c.faults == null) return;
            c.faults.RemoveAll(f => f.Def != null && ComponentCatalog.Get(f.Def.Component)?.Group == group);
        }

        /// <summary>Multiplier for <see cref="CarValueCalculator.MarketValue"/>: faults, paperwork, history.</summary>
        public static float ValueMultiplier(CarInstance c, bool knownOnly)
        {
            if (c == null || c.documents == null || !c.documents.generated) return 1f;
            float m = 1f - FaultModel.ValueLoss(c.faults, knownOnly);
            return m * VehicleRecords.ValueMultiplier(c.documents, c.history, ModelYear(c), Today, knownOnly);
        }

        public static bool IsOwned(CarInstance c) => CarSalesServices.Inventory && CarSalesServices.Inventory.OwnedCars.Contains(c);

        public static IEnumerable<FaultInstance> KnownFaults(CarInstance c) => c.faults == null ? Enumerable.Empty<FaultInstance>() : c.faults.Where(f => f.known);

        public static bool MotValid(CarInstance c) => c.documents == null || !c.documents.generated || VehicleRecords.MotValid(c.documents, ModelYear(c), Today);

        public static string MotText(CarInstance c)
        {
            var d = c.documents;
            if (d == null || !d.generated) return "-";
            if (!VehicleRecords.NeedsMot(ModelYear(c), Today)) return "Not needed yet (under 3 years old)";
            int left = d.motExpiryDay - Today;
            return left < 0 ? $"EXPIRED {-left} day{(-left == 1 ? "" : "s")} ago" : $"Valid for {left} more day{(left == 1 ? "" : "s")}";
        }

        /// <summary>Estimated cost to fix a fault at the town garage with aftermarket parts.</summary>
        public static int RepairEstimate(CarInstance c, FaultInstance f)
        {
            var job = f.Def != null ? WorkshopCatalog.JobFor(f.Def.Component) : null;
            if (job == null) return 0;
            var def = CarSalesServices.Database ? CarSalesServices.Database.Get(c.carId) : null;
            int basePrice = def ? def.basePrice : 20000;
            return WorkshopCatalog.PartsPrice(job, PartTier.Aftermarket, Supplier.MotorFactor, basePrice) + WorkshopCatalog.LabourCost(job);
        }

        public static string ServiceText(DocumentSet d) =>
            d.service == ServiceHistory.Full ? $"Full service history ({d.serviceStamps} stamps)" :
            d.service == ServiceHistory.Partial ? $"Partial service history ({d.serviceStamps} stamps)" : "No service history";
    }
}
