using System;
using System.Collections.Generic;
using CarSales.Sim;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CarSales
{
    public enum CarPart { Engine, Brakes, Tires, Body, Interior, Electronics }

    /// <summary>
    /// Per-part condition. A car's overall condition = weighted average of its parts.
    /// Weights add up to 1, so repairing every part costs the same as the old single "repair" did.
    /// </summary>
    public static class CarParts
    {
        public static readonly CarPart[] All = (CarPart[])Enum.GetValues(typeof(CarPart));
        static readonly float[] Weights = { 0.30f, 0.15f, 0.10f, 0.20f, 0.15f, 0.10f };

        public static float Weight(CarPart p) => Weights[(int)p];

        public static string DisplayName(CarPart p)
        {
            switch (p)
            {
                case CarPart.Engine: return "Engine";
                case CarPart.Brakes: return "Brakes";
                case CarPart.Tires: return "Tires";
                case CarPart.Body: return "Body";
                case CarPart.Interior: return "Interior";
                default: return "Electronics";
            }
        }

        public static string RepairName(CarPart p)
        {
            switch (p)
            {
                case CarPart.Engine: return "Engine Repair";
                case CarPart.Brakes: return "Brake Service";
                case CarPart.Tires: return "New Tires";
                case CarPart.Body: return "Body Repair";
                case CarPart.Interior: return "Interior Refurbish";
                default: return "Electronics Repair";
            }
        }

        public static string Glyph(CarPart p)
        {
            switch (p)
            {
                case CarPart.Engine: return "E";
                case CarPart.Brakes: return "B";
                case CarPart.Tires: return "T";
                case CarPart.Body: return "Bo";
                case CarPart.Interior: return "In";
                default: return "El";
            }
        }

        // ------------------------------------------------------------------ COMPONENTS (v5)
        // The 6 parts above are now GROUPS of ~45 individual components (CarSales.Sim.ComponentCatalog).
        // c.components is the truth; c.parts[] is a cache of the group values so older code keeps working.
        // Change condition only through these methods so the cache and c.condition stay correct.

        static readonly int[] componentCount = new int[Enum.GetValues(typeof(PowertrainType)).Length];

        static int ExpectedComponents(PowertrainType p)
        {
            int i = (int)p;
            if (componentCount[i] == 0) foreach (var _ in ComponentCatalog.For(p)) componentCount[i]++;
            return componentCount[i];
        }

        public static PowertrainType PowertrainOf(CarInstance c)
        {
            var db = CarSalesServices.Database;
            var def = db && c != null ? db.Get(c.carId) : null;
            return PowertrainTypes.Parse(def ? def.fuelType : null);
        }

        /// <summary>Makes sure the car has its components (old saves: built from the 6 part values) and the cache is valid.</summary>
        public static void EnsureParts(CarInstance c)
        {
            if (c.components == null) c.components = new List<ComponentState>();
            var pt = PowertrainOf(c);
            bool partsOk = c.parts != null && c.parts.Length == All.Length;
            if (partsOk && c.components.Count == ExpectedComponents(pt)) { CarRecords.Ensure(c); return; }
            if (!partsOk)
            {
                c.parts = new float[All.Length];
                for (int i = 0; i < c.parts.Length; i++) c.parts[i] = c.condition;
            }
            bool fresh = c.components.Count == 0;
            ComponentSet.Ensure(c.components, pt, c.parts, () => Random.Range(-1f, 1f), c.condition);
            if (fresh) AvoidDeadOnArrival(c);
            Recalculate(c);
            CarRecords.Ensure(c);
        }

        /// <summary>Newly generated or converted cars always start; dead batteries come from wear and crashes later.</summary>
        static void AvoidDeadOnArrival(CarInstance c)
        {
            foreach (var id in new[] { ComponentId.Battery12V, ComponentId.StarterMotor })
                if (ComponentSet.Has(c.components, id) && ComponentSet.Get(c.components, id) < 25f) ComponentSet.Set(c.components, id, 25f + Random.Range(0f, 20f));
        }

        public static float Get(CarInstance c, CarPart p) { EnsureParts(c); return c.parts[(int)p]; }

        public static void Set(CarInstance c, CarPart p, float value)
        {
            EnsureParts(c);
            ComponentSet.SetGroup(c.components, (ComponentGroup)p, PowertrainOf(c), Mathf.Clamp(value, 0f, 100f));
            if (value >= 99f) CarRecords.ClearFaultsInGroup(c, (ComponentGroup)p);   // a full repair fixes the faults too
            else FaultModel.ApplyHealthCaps(c.components, c.faults);
            Recalculate(c);
        }

        public static float Get(CarInstance c, ComponentId id) { EnsureParts(c); return ComponentSet.Get(c.components, id); }

        public static void Set(CarInstance c, ComponentId id, float value)
        {
            EnsureParts(c);
            ComponentSet.Set(c.components, id, value);
            Recalculate(c);
        }

        /// <summary>Refreshes the group cache and the overall condition from the components.</summary>
        public static void Recalculate(CarInstance c)
        {
            if (c.components == null) c.components = new List<ComponentState>();
            if (c.parts == null || c.parts.Length != All.Length) c.parts = new float[All.Length];
            if (c.components.Count > 0)
            {
                var pt = PowertrainOf(c);
                for (int i = 0; i < c.parts.Length; i++) c.parts[i] = ComponentSet.GroupHealth(c.components, (ComponentGroup)i, pt);
            }
            float sum = 0f;
            for (int i = 0; i < c.parts.Length; i++) sum += c.parts[i] * Weights[i];
            c.condition = Mathf.Clamp(sum, 0f, 100f);
        }

        /// <summary>Creates varied component conditions whose weighted average is close to `overall`.</summary>
        public static void Randomize(CarInstance c, float overall)
        {
            c.parts = new float[All.Length];
            for (int i = 0; i < c.parts.Length; i++) c.parts[i] = Mathf.Clamp(overall + Random.Range(-28f, 22f), 5f, 100f);
            for (int pass = 0; pass < 3; pass++)
            {
                float sum = 0f;
                for (int i = 0; i < c.parts.Length; i++) sum += c.parts[i] * Weights[i];
                float diff = overall - sum;
                for (int i = 0; i < c.parts.Length; i++) c.parts[i] = Mathf.Clamp(c.parts[i] + diff, 5f, 100f);
            }
            c.components = new List<ComponentState>();
            ComponentSet.Ensure(c.components, PowertrainOf(c), c.parts, () => Random.Range(-1f, 1f), overall);
            AvoidDeadOnArrival(c);
            Recalculate(c);
            CarRecords.Ensure(c);
        }

        /// <summary>Crash damage without a known direction (treated as a front impact).</summary>
        public static void Damage(CarInstance c, float amount) => Damage(c, ImpactZone.Front, amount * 2.5f);

        /// <summary>Crash damage to one side of the car. `amount` = points lost by the worst-hit part.</summary>
        public static void Damage(CarInstance c, ImpactZone zone, float amount)
        {
            EnsureParts(c);
            ComponentSet.Damage(c.components, zone, amount);
            Recalculate(c);
        }

        /// <summary>Normal wear from driving `km` kilometres (harshness 1 = gentle, 2 = hard).</summary>
        public static void Wear(CarInstance c, float km, float harshness = 1f)
        {
            if (km <= 0f) return;
            EnsureParts(c);
            ComponentSet.ApplyWear(c.components, km, harshness);
            Recalculate(c);
        }

        public static void RepairAll(CarInstance c)
        {
            EnsureParts(c);
            foreach (var s in c.components) s.health = 100f;
            if (c.faults != null) c.faults.Clear();
            Recalculate(c);
        }

        /// <summary>How the car's condition changes the way it drives.</summary>
        public static DrivingEffects Effects(CarInstance c)
        {
            EnsureParts(c);
            return ComponentSet.Effects(c.components, PowertrainOf(c));
        }

        public static string Label(float v) => v >= 85f ? "Excellent" : v >= 65f ? "Good" : v >= 40f ? "Fair" : "Poor";
        public static Color LabelColor(float v) => v >= 65f ? UIFactory.Good : v >= 40f ? UIFactory.Warn : UIFactory.Bad;
    }
}
