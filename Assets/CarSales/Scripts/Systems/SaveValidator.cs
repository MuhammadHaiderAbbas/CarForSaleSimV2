using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Repairs impossible values in loaded save data (missing lists, duplicate car ids, out-of-range numbers)
    /// so a damaged or hand-edited save still loads into a playable state. Returns the number of fixes.
    /// </summary>
    public static class SaveValidator
    {
        public static int Validate(SaveData d)
        {
            int fixes = 0;
            if (d.ownedCars == null) { d.ownedCars = new List<CarInstance>(); fixes++; }
            if (d.marketListings == null) { d.marketListings = new List<CarInstance>(); fixes++; }
            if (d.lotListings == null) { d.lotListings = new List<CarInstance>(); fixes++; }
            if (d.messages == null) { d.messages = new List<GameMessage>(); fixes++; }
            if (d.ownedProperties == null) { d.ownedProperties = new List<string>(); fixes++; }
            if (d.upgradeLevels == null) { d.upgradeLevels = new int[0]; fixes++; }
            if (d.stats == null) { d.stats = new GameStats(); fixes++; }

            fixes += RemoveNulls(d.ownedCars) + RemoveNulls(d.marketListings) + RemoveNulls(d.lotListings);
            d.messages.RemoveAll(m => m == null);

            if (d.day < 1) { d.day = 1; fixes++; }
            if (d.loan < 0) { d.loan = 0; fixes++; }
            if (d.daysInDebt < 0) { d.daysInDebt = 0; fixes++; }
            if (float.IsNaN(d.reputation) || d.reputation < 0f || d.reputation > 100f) { d.reputation = Mathf.Clamp(Safe(d.reputation, 50f), 0f, 100f); fixes++; }
            if (float.IsNaN(d.energy) || d.energy < 0f || d.energy > 100f) { d.energy = Mathf.Clamp(Safe(d.energy, 100f), 0f, 100f); fixes++; }
            if (float.IsNaN(d.hour) || d.hour > 30f) { d.hour = -1f; fixes++; }       // -1 = wake-up time
            for (int i = 0; i < d.upgradeLevels.Length; i++) if (d.upgradeLevels[i] < 0) { d.upgradeLevels[i] = 0; fixes++; }

            var ids = new HashSet<string>();
            foreach (var c in d.ownedCars) fixes += FixCar(c, ids);
            foreach (var c in d.marketListings) fixes += FixCar(c, ids);
            foreach (var c in d.lotListings) fixes += FixCar(c, ids);
            foreach (var m in d.messages) if (m.offerCar != null && !string.IsNullOrEmpty(m.offerCar.carId)) fixes += FixCar(m.offerCar, ids);
            return fixes;
        }

        static int RemoveNulls(List<CarInstance> list) => list.RemoveAll(c => c == null || string.IsNullOrEmpty(c.carId));

        static float Safe(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

        static int FixCar(CarInstance c, HashSet<string> ids)
        {
            int fixes = 0;
            if (string.IsNullOrEmpty(c.instanceId) || !ids.Add(c.instanceId))
            {
                c.instanceId = Guid.NewGuid().ToString();
                ids.Add(c.instanceId);
                fixes++;
            }
            fixes += Clamp(ref c.fuel, 0f, 100f, 50f);
            fixes += Clamp(ref c.condition, 0f, 100f, 50f);
            fixes += Clamp(ref c.reportedCondition, 0f, 100f, c.condition);
            fixes += Clamp(ref c.cleanliness, 0f, 100f, 50f);
            if (c.mileage < 0) { c.mileage = 0; fixes++; }
            if (c.purchasePrice < 0) { c.purchasePrice = 0; fixes++; }
            if (c.repairSpent < 0) { c.repairSpent = 0; fixes++; }
            if (c.listedPrice < 0) { c.listedPrice = 0; fixes++; }
            if (c.parts != null)
                for (int i = 0; i < c.parts.Length; i++) fixes += Clamp(ref c.parts[i], 0f, 100f, 50f);
            if (c.components != null)
            {
                fixes += c.components.RemoveAll(x => x == null);
                foreach (var comp in c.components) fixes += Clamp(ref comp.health, 0f, 100f, 50f);
            }
            CarParts.EnsureParts(c);
            return fixes;
        }

        static int Clamp(ref float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { v = fallback; return 1; }
            if (v < min) { v = min; return 1; }
            if (v > max) { v = max; return 1; }
            return 0;
        }
    }
}
