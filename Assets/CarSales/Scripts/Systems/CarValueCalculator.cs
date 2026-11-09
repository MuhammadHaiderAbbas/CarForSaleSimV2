using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CarSales
{
    /// <summary>All economy formulas in one place. Tweak numbers here to balance the game.</summary>
    public static class CarValueCalculator
    {
        public const int BaseCleanCost = 150;
        public const float RepairThreshold = 60f;  // below this condition = NeedsRepair

        static readonly Color[] Paints =
        {
            Color.white, new Color(0.08f, 0.08f, 0.08f), new Color(0.6f, 0.6f, 0.62f),
            new Color(0.7f, 0.05f, 0.05f), new Color(0.05f, 0.15f, 0.5f),
            new Color(0.1f, 0.35f, 0.15f), new Color(0.85f, 0.75f, 0.55f), new Color(0.95f, 0.55f, 0.1f)
        };

        /// <summary>Market value. useReported = value based on the seller's claimed condition (marketplace).</summary>
        public static int MarketValue(CarInstance car, bool useReported = false)
        {
            var db = CarSalesServices.Database;
            var def = db ? db.Get(car.carId) : null;
            if (def == null) return 0;
            float cond = useReported && car.reportedCondition > 0f ? car.reportedCondition : car.condition;
            float conditionMult = Mathf.Lerp(0.35f, 1f, cond / 100f);
            float cleanMult     = Mathf.Lerp(0.90f, 1f, car.cleanliness / 100f);
            float mileageMult   = Mathf.Clamp(1f - car.mileage / 300000f, 0.5f, 1f);
            // Phase 5: faults, paperwork and history. A buyer who hasn't checked (useReported) only sees what's known.
            float records = CarRecords.ValueMultiplier(car, knownOnly: useReported);
            // Phase 7: today's market (price index, season, demand, supply).
            if (CarSalesServices.Market) records *= CarSalesServices.Market.Multiplier(def);
            // Phase 8: dear fuel makes economical cars more wanted.
            if (CarSalesServices.World) records *= CarSales.Sim.WorldSim.EconomyDemand(CarSalesServices.World.FuelIndex, IsEconomical(def));
            return Mathf.RoundToInt(def.basePrice * conditionMult * cleanMult * mileageMult * def.popularity * records);
        }

        public static bool IsEconomical(CarDefinition def)
        {
            string f = (def.fuelType ?? "").ToLowerInvariant();
            return f.Contains("electric") || f.Contains("hybrid") || def.category == CarCategory.Hatchback;
        }

        public const int InspectionFee = CarSales.Sim.InspectionEngine.FullInspectionCost;   // full workshop inspection
        public const float FullServiceDiscount = 0.9f;
        public const float QuickSellRate = 0.75f;

        /// <summary>Cost to bring one part back to 100%.</summary>
        public static int PartRepairCost(CarInstance car, CarPart part)
        {
            var def = CarSalesServices.Database.Get(car.carId);
            if (def == null) return 0;
            float mult = CarSalesServices.Upgrades ? CarSalesServices.Upgrades.RepairCostMultiplier : 1f;
            return Mathf.RoundToInt((100f - CarParts.Get(car, part)) * CarParts.Weight(part) * def.basePrice * 0.004f * mult);
        }

        /// <summary>Cost to repair everything one part at a time.</summary>
        public static int RepairCost(CarInstance car)
        {
            int sum = 0;
            foreach (var p in CarParts.All) sum += PartRepairCost(car, p);
            return sum;
        }

        public static int FullServiceCost(CarInstance car) => Mathf.RoundToInt(RepairCost(car) * FullServiceDiscount);

        public static int InspectionCost() =>
            CarSalesServices.Upgrades && CarSalesServices.Upgrades.AccurateInspections ? 0 : InspectionFee;

        public static bool IsConditionKnown(CarInstance car) =>
            car.inspected || (CarSalesServices.Upgrades && CarSalesServices.Upgrades.AccurateInspections);

        public static int QuickSellPrice(CarInstance car) => Mathf.RoundToInt(MarketValue(car) * QuickSellRate);

        public static int CleanCost() =>
            CarSalesServices.Upgrades ? CarSalesServices.Upgrades.CleanCost : BaseCleanCost;

        public static CarState DefaultState(CarInstance car) =>
            car.condition < RepairThreshold ? CarState.NeedsRepair : CarState.Ready;

        /// <summary>Condition text for the marketplace: the seller's claim, or the truth with the Mechanic upgrade.</summary>
        public static string ConditionLabel(CarInstance car)
        {
            return IsConditionKnown(car)
                ? $"{CarParts.Label(car.condition)} ({car.condition:0}%)"
                : $"~{car.reportedCondition:0}% (seller's claim)";
        }

        public static CarInstance GenerateRandom(CarDefinition def)
        {
            float condition = Random.Range(20f, 90f);
            // Sellers usually exaggerate a bit - sometimes a lot.
            float lie = Random.value < 0.3f ? Random.Range(10f, 25f) : Random.Range(-5f, 8f);
            var car = new CarInstance
            {
                instanceId = Guid.NewGuid().ToString(),
                carId = def.carId,
                condition = condition,
                reportedCondition = Mathf.Clamp(condition + lie, 5f, 100f),
                cleanliness = Random.Range(10f, 80f),
                mileage = Random.Range(10000, 250000),
                paintColor = Paints[Random.Range(0, Paints.Length)],
                state = CarState.InTransit,
                parkingSpotIndex = -1
            };
            CarParts.Randomize(car, condition);
            car.reportedCondition = Mathf.Clamp(car.condition + lie, 5f, 100f);
            return car;
        }
    }
}
