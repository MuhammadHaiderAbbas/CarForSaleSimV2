using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    /// <summary>Saved condition of one component (0 = destroyed, 100 = new).</summary>
    [Serializable]
    public class ComponentState
    {
        public int id;
        public float health = 100f;

        public ComponentState() { }
        public ComponentState(ComponentId id, float health) { this.id = (int)id; this.health = health; }
        public ComponentId Id => (ComponentId)id;
    }

    /// <summary>How the components change the way the car drives (all multipliers, 1 = perfect car).</summary>
    public struct DrivingEffects
    {
        public float Power;         // engine / motor health
        public float Brakes;        // brake torque
        public float FrontGrip, RearGrip;
        public float Damping;       // shock absorbers
        public float SteeringPull;  // -1..1, bad alignment pulls to one side
        public bool CanStart;       // a dead 12V battery or starter stops the car from starting

        public static DrivingEffects Perfect => new DrivingEffects
        {
            Power = 1f, Brakes = 1f, FrontGrip = 1f, RearGrip = 1f, Damping = 1f, SteeringPull = 0f, CanStart = true
        };
    }

    /// <summary>Operations on a car's component list. Plain functions so they can be unit-tested.</summary>
    public static class ComponentSet
    {
        static float Clamp(float v) => v < 0f ? 0f : v > 100f ? 100f : v;

        public static ComponentState Find(List<ComponentState> list, ComponentId id)
        {
            if (list == null) return null;
            int key = (int)id;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].id == key) return list[i];
            return null;
        }

        /// <summary>
        /// Makes sure <paramref name="list"/> has exactly the components this powertrain has.
        /// Missing ones are created from the matching group value in <paramref name="groupHealth"/> (6 values, may be null),
        /// with a little variation from <paramref name="noise"/> (returns -1..1; null = no variation). Returns true if anything changed.
        /// </summary>
        public static bool Ensure(List<ComponentState> list, PowertrainType powertrain, float[] groupHealth, Func<float> noise, float fallbackHealth = 75f)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            bool changed = list.RemoveAll(s => s == null || !ComponentCatalog.IsKnown(s.id) || !ComponentCatalog.Get(s.Id).FitsTo(powertrain)) > 0;

            var addedGroups = new HashSet<ComponentGroup>();
            foreach (var def in ComponentCatalog.For(powertrain))
            {
                if (Find(list, def.Id) != null) continue;
                float g = GroupValue(groupHealth, def.Group, fallbackHealth);
                float spread = noise != null ? noise() * 12f : 0f;
                list.Add(new ComponentState(def.Id, Clamp(g + spread)));
                addedGroups.Add(def.Group);
                changed = true;
            }

            // Shift the new values so each group's weighted average matches the old single value exactly.
            if (groupHealth != null)
                foreach (var g in addedGroups)
                    for (int pass = 0; pass < 4; pass++)
                    {
                        float diff = GroupValue(groupHealth, g, fallbackHealth) - GroupHealth(list, g, powertrain);
                        if (Math.Abs(diff) < 0.01f) break;
                        foreach (var d in ComponentCatalog.InGroup(g, powertrain))
                        {
                            var s = Find(list, d.Id);
                            s.health = Clamp(s.health + diff);
                        }
                    }
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return changed;
        }

        static float GroupValue(float[] groups, ComponentGroup g, float fallback) =>
            groups != null && (int)g < groups.Length ? Clamp(groups[(int)g]) : fallback;

        public static float Get(List<ComponentState> list, ComponentId id) => Find(list, id)?.health ?? 0f;

        public static bool Has(List<ComponentState> list, ComponentId id) => Find(list, id) != null;

        public static void Set(List<ComponentState> list, ComponentId id, float health)
        {
            var s = Find(list, id);
            if (s != null) s.health = Clamp(health);
        }

        /// <summary>Weighted average of the group's components (100 if the car has none in this group).</summary>
        public static float GroupHealth(List<ComponentState> list, ComponentGroup group, PowertrainType p)
        {
            float sum = 0f, weights = 0f;
            foreach (var d in ComponentCatalog.InGroup(group, p))
            {
                var s = Find(list, d.Id);
                if (s == null) continue;
                sum += s.health * d.Weight;
                weights += d.Weight;
            }
            return weights > 0f ? sum / weights : 100f;
        }

        public static void SetGroup(List<ComponentState> list, ComponentGroup group, PowertrainType p, float health)
        {
            foreach (var d in ComponentCatalog.InGroup(group, p)) Set(list, d.Id, health);
        }

        /// <summary>Crash damage in one zone. <paramref name="amount"/> is the condition points the worst-hit part loses.</summary>
        public static void Damage(List<ComponentState> list, ImpactZone zone, float amount)
        {
            if (amount <= 0f) return;
            foreach (var hit in ComponentCatalog.Zone(zone))
            {
                var s = Find(list, hit.Key);
                if (s != null) s.health = Clamp(s.health - amount * hit.Value);
            }
        }

        /// <summary>Normal wear from driving. <paramref name="harshness"/> 1 = normal, 2 = hard driving.</summary>
        public static void ApplyWear(List<ComponentState> list, float km, float harshness = 1f)
        {
            if (km <= 0f || list == null) return;
            foreach (var s in list)
            {
                var d = ComponentCatalog.Get(s.Id);
                if (d != null) s.health = Clamp(s.health - d.WearPerKm * km * Math.Max(0f, harshness));
            }
        }

        public static DrivingEffects Effects(List<ComponentState> list, PowertrainType p)
        {
            var e = DrivingEffects.Perfect;
            if (list == null || list.Count == 0) return e;
            float F(ComponentId id) => Has(list, id) ? Get(list, id) / 100f : 1f;
            float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : t > 1f ? 1f : t);

            // A worn engine still runs, but weaker: 0 health = 45% power.
            float engine = GroupHealth(list, ComponentGroup.Engine, p) / 100f;
            e.Power = Lerp(0.45f, 1f, engine);
            float brakes = (F(ComponentId.BrakePadsFront) * 0.35f + F(ComponentId.BrakePadsRear) * 0.2f +
                            F(ComponentId.BrakeDiscs) * 0.3f + F(ComponentId.BrakeFluid) * 0.15f);
            e.Brakes = Lerp(0.4f, 1f, brakes);
            e.FrontGrip = Lerp(0.72f, 1f, (F(ComponentId.TyreFrontLeft) + F(ComponentId.TyreFrontRight)) * 0.5f);
            e.RearGrip = Lerp(0.72f, 1f, (F(ComponentId.TyreRearLeft) + F(ComponentId.TyreRearRight)) * 0.5f);
            e.Damping = Lerp(0.35f, 1f, F(ComponentId.ShockAbsorbers));
            float align = F(ComponentId.Alignment);
            e.SteeringPull = align < 0.6f ? (0.6f - align) * 0.25f : 0f;
            e.CanStart = F(ComponentId.Battery12V) > 0.08f && (!Has(list, ComponentId.StarterMotor) || F(ComponentId.StarterMotor) > 0.08f);
            return e;
        }
    }
}
