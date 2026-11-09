using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    /// <summary>Ways of looking at a car. A fault lists the methods that can find it (spec §6.3.3 / §6.5).</summary>
    [Flags]
    public enum InspectMethod
    {
        None = 0, Visual = 1, Interior = 2, UnderBonnet = 4, Scanner = 8, RoadTest = 16, Lift = 32, History = 64,
        AllPhysical = Visual | Interior | UnderBonnet | Scanner | RoadTest | Lift,
    }

    public enum MotEffect { None, Advisory, FailWhenSevere, Fail }

    /// <summary>Stored in saves as numbers: only ADD new ids, never renumber.</summary>
    public enum FaultId
    {
        MisfireCylinder2 = 1, HeadGasketLeak = 2, TimingBeltOverdue = 3, OilLeak = 4, OilServiceOverdue = 5,
        EngineKnock = 6, BlueSmoke = 7, CoolantLeak = 8, Overheating = 9, TurboWhistle = 10, BoostLeak = 11,
        GearboxWhine = 12, ClutchSlipping = 13, ExhaustBlowing = 14, CatalystFailing = 15,
        TractionBatteryDegraded = 20, ChargePortFault = 21, InverterFault = 22,
        Battery12VWeak = 30, AlternatorFailing = 31, StarterSticking = 32, EngineLightOn = 33, BulbOut = 34,
        InfotainmentDead = 35, ElectricalGremlins = 36,
        FrontPadsWorn = 40, RearPadsWorn = 41, DiscsScored = 42, BrakeFluidOld = 43, ShockLeaking = 44,
        BrokenSpring = 45, WornBushes = 46, SteeringRackPlay = 47, PullsToOneSide = 48,
        TyreDamaged = 60,
        SillCorrosion = 70, WindscreenChip = 71, PaintMismatch = 72,
        DampCarpets = 90, SmokerOdour = 91,
    }

    public sealed class FaultDef
    {
        public FaultId Id;
        public ComponentId Component;
        public string Name;
        public string Symptom;        // what you notice
        public string Dtc;            // OBD code, or null
        public InspectMethod FoundBy;
        public MotEffect Mot;
        public float ValueImpact;     // share of the car's value lost at severity 1
        public float ProgressPerDay;  // severity gained per day
        public float Chance;          // relative chance to appear on a worn component
        public float HealthAtFull;    // component health while the fault is at severity 1

        public bool FailsMot(float severity) => Mot == MotEffect.Fail || (Mot == MotEffect.FailWhenSevere && severity >= 0.6f);
        public bool AdvisesMot(float severity) => !FailsMot(severity) && Mot != MotEffect.None;
    }

    /// <summary>A fault on one specific car. <see cref="known"/> = the player has found it.</summary>
    [Serializable]
    public class FaultInstance
    {
        public int id;
        public float severity;      // 0-1
        public bool known;
        public int foundBy;         // InspectMethod that revealed it

        public FaultInstance() { }
        public FaultInstance(FaultId id, float severity) { this.id = (int)id; this.severity = severity; }
        public FaultId Id => (FaultId)id;
        public FaultDef Def => FaultCatalog.Get(Id);
    }

    /// <summary>The first 40 faults.</summary>
    public static class FaultCatalog
    {
        public static readonly IReadOnlyList<FaultDef> All;
        static readonly Dictionary<FaultId, FaultDef> byId = new Dictionary<FaultId, FaultDef>();

        static FaultCatalog()
        {
            var list = new List<FaultDef>();
            void F(FaultId id, ComponentId c, string name, string symptom, InspectMethod by, float impact, float chance = 1f,
                   MotEffect mot = MotEffect.None, string dtc = null, float progress = 0.01f, float healthAtFull = 35f)
                => list.Add(new FaultDef { Id = id, Component = c, Name = name, Symptom = symptom, FoundBy = by, ValueImpact = impact, Chance = chance,
                                           Mot = mot, Dtc = dtc, ProgressPerDay = progress, HealthAtFull = healthAtFull });
            const InspectMethod V = InspectMethod.Visual, I = InspectMethod.Interior, U = InspectMethod.UnderBonnet,
                                S = InspectMethod.Scanner, R = InspectMethod.RoadTest, L = InspectMethod.Lift;

            // Engine & drivetrain
            F(FaultId.MisfireCylinder2, ComponentId.Ignition, "Misfire cylinder 2", "Rough idle, engine shakes at lights", S | R, 0.04f, 1f, MotEffect.None, "P0302");
            F(FaultId.HeadGasketLeak, ComponentId.CylinderHead, "Head gasket leak", "Mayonnaise under the oil cap, white smoke", U | R, 0.16f, 0.5f, MotEffect.None, null, 0.02f, 25f);
            F(FaultId.TimingBeltOverdue, ComponentId.TimingBelt, "Timing belt overdue", "No record of a belt change, cracked belt", U, 0.05f, 1.2f, MotEffect.None, null, 0.015f, 30f);
            F(FaultId.OilLeak, ComponentId.OilAndFilter, "Oil leak", "Oil drips under the engine", V | L, 0.02f, 1.2f, MotEffect.Advisory);
            F(FaultId.OilServiceOverdue, ComponentId.OilAndFilter, "Oil service overdue", "Service light on, black sludgy oil", I | U, 0.015f, 1.5f, MotEffect.None, null, 0.02f, 45f);
            F(FaultId.EngineKnock, ComponentId.EngineInternals, "Engine knock", "Deep knocking under load", R | U, 0.2f, 0.35f, MotEffect.None, null, 0.02f, 20f);
            F(FaultId.BlueSmoke, ComponentId.EngineInternals, "Burning oil", "Blue smoke from the exhaust", V | R, 0.1f, 0.5f, MotEffect.FailWhenSevere);
            F(FaultId.CoolantLeak, ComponentId.CoolingSystem, "Coolant leak", "Low coolant, sweet smell, crusty hoses", U | L, 0.03f, 1f, MotEffect.None, null, 0.02f);
            F(FaultId.Overheating, ComponentId.CoolingSystem, "Overheats", "Temperature gauge climbs in traffic", R | S, 0.06f, 0.6f, MotEffect.None, "P0217");
            F(FaultId.TurboWhistle, ComponentId.IntakeAndTurbo, "Turbo bearing worn", "Siren-like whistle when accelerating", R | U, 0.07f, 0.6f);
            F(FaultId.BoostLeak, ComponentId.IntakeAndTurbo, "Boost leak", "Flat acceleration, hissing", S | R, 0.03f, 0.7f, MotEffect.None, "P0299");
            F(FaultId.GearboxWhine, ComponentId.Gearbox, "Gearbox bearing whine", "Whine that rises with speed", R, 0.09f, 0.6f, MotEffect.None, null, 0.01f, 30f);
            F(FaultId.ClutchSlipping, ComponentId.Clutch, "Clutch slipping", "Revs flare in high gear under hard throttle", R, 0.05f, 1f, MotEffect.None, null, 0.02f, 25f);
            F(FaultId.ExhaustBlowing, ComponentId.Exhaust, "Exhaust blowing", "Ticking/blowing noise, louder than it should be", R | L, 0.02f, 1f, MotEffect.FailWhenSevere);
            F(FaultId.CatalystFailing, ComponentId.Exhaust, "Catalytic converter failing", "Smells of rotten eggs; emissions high", S, 0.05f, 0.6f, MotEffect.Fail, "P0420");
            // Electric drivetrain
            F(FaultId.TractionBatteryDegraded, ComponentId.TractionBattery, "Battery degraded", "Range well below new", S, 0.15f, 0.8f, MotEffect.None, null, 0.004f, 50f);
            F(FaultId.ChargePortFault, ComponentId.ChargingSystem, "Charge port fault", "Charging stops randomly, bent pins", V | S, 0.02f, 0.7f);
            F(FaultId.InverterFault, ComponentId.Inverter, "Inverter fault", "Power cuts out, warning on the dash", S | I, 0.08f, 0.4f, MotEffect.None, "P0A78");
            // Electronics
            F(FaultId.Battery12VWeak, ComponentId.Battery12V, "12V battery weak", "Slow to crank, won't start in the cold", S | R, 0.01f, 1.4f, MotEffect.None, null, 0.02f, 30f);
            F(FaultId.AlternatorFailing, ComponentId.Alternator, "Alternator failing", "Battery light flickers", S | I, 0.03f, 0.6f, MotEffect.None, "P0562");
            F(FaultId.StarterSticking, ComponentId.StarterMotor, "Starter motor sticking", "Click, no crank - sometimes", R, 0.015f, 0.5f);
            F(FaultId.EngineLightOn, ComponentId.EcuAndSensors, "Engine warning light", "Check-engine light on (lean mixture)", I | S, 0.03f, 1f, MotEffect.Fail, "P0171");
            F(FaultId.BulbOut, ComponentId.Lights, "Bulb out", "A headlight or brake light doesn't work", V, 0.003f, 1.5f, MotEffect.Fail, null, 0f, 60f);
            F(FaultId.InfotainmentDead, ComponentId.Infotainment, "Infotainment dead", "Screen stays black", I, 0.015f, 0.6f, MotEffect.None, null, 0f, 30f);
            F(FaultId.ElectricalGremlins, ComponentId.Wiring, "Electrical faults", "Windows and locks act up", I | S, 0.04f, 0.5f);
            // Brakes, suspension, steering
            F(FaultId.FrontPadsWorn, ComponentId.BrakePadsFront, "Front pads worn", "Squeal when braking, thin pads", L | R, 0.008f, 1.8f, MotEffect.FailWhenSevere, null, 0.02f, 15f);
            F(FaultId.RearPadsWorn, ComponentId.BrakePadsRear, "Rear pads worn", "Thin rear pads", L, 0.006f, 1.4f, MotEffect.FailWhenSevere, null, 0.02f, 15f);
            F(FaultId.DiscsScored, ComponentId.BrakeDiscs, "Brake discs scored", "Steering wheel shakes when braking", L | R, 0.015f, 1f, MotEffect.Advisory);
            F(FaultId.BrakeFluidOld, ComponentId.BrakeFluid, "Brake fluid old", "Dark fluid, soft pedal", U, 0.005f, 1.2f, MotEffect.Advisory);
            F(FaultId.ShockLeaking, ComponentId.ShockAbsorbers, "Shock absorber leaking", "Bouncy ride, oily shock", L | R, 0.025f, 0.9f, MotEffect.Fail);
            F(FaultId.BrokenSpring, ComponentId.Springs, "Broken coil spring", "One corner sits low, clonks", L | V, 0.025f, 0.5f, MotEffect.Fail, null, 0f);
            F(FaultId.WornBushes, ComponentId.BushesAndArms, "Worn bushes", "Clunks over bumps", L | R, 0.02f, 1f, MotEffect.Advisory);
            F(FaultId.SteeringRackPlay, ComponentId.SteeringRack, "Steering rack play", "Vague steering, knock through the wheel", R | L, 0.04f, 0.5f, MotEffect.Fail);
            F(FaultId.PullsToOneSide, ComponentId.Alignment, "Pulls to one side", "Car drifts when you let go of the wheel", R, 0.008f, 1.2f, MotEffect.None, null, 0.005f, 40f);
            // Tyres
            F(FaultId.TyreDamaged, ComponentId.TyreFrontLeft, "Front tyre damaged", "Bulge or cut in the sidewall", V | L, 0.006f, 0.8f, MotEffect.Fail, null, 0f, 20f);
            // Body
            F(FaultId.SillCorrosion, ComponentId.QuarterPanelsRear, "Corroded sill", "Rust holes along the sill", L, 0.08f, 0.6f, MotEffect.Fail, null, 0.005f, 30f);
            F(FaultId.WindscreenChip, ComponentId.Glass, "Windscreen chip", "Chip in the driver's view", V, 0.01f, 1f, MotEffect.FailWhenSevere, null, 0.01f, 50f);
            F(FaultId.PaintMismatch, ComponentId.Paint, "Paint mismatch", "Panels don't quite match: earlier accident repair", V, 0.03f, 0.6f, MotEffect.None, null, 0f, 60f);
            // Interior
            F(FaultId.DampCarpets, ComponentId.Carpets, "Damp carpets", "Wet footwell, musty smell: a leaking seal", I, 0.03f, 0.7f, MotEffect.None, null, 0.01f);
            F(FaultId.SmokerOdour, ComponentId.Headliner, "Smoker's car", "Smells of smoke, yellow headliner", I, 0.04f, 0.6f, MotEffect.None, null, 0f, 40f);

            All = list;
            foreach (var f in list) byId[f.Id] = f;
        }

        public static FaultDef Get(FaultId id) => byId.TryGetValue(id, out var f) ? f : null;

        public static IEnumerable<FaultDef> OnComponent(ComponentId c)
        {
            foreach (var f in All) if (f.Component == c) yield return f;
        }
    }

    /// <summary>Creating, worsening and pricing faults. Pure functions (tests in FaultTests).</summary>
    public static class FaultModel
    {
        /// <summary>
        /// Rolls faults for a car from its component wear: a worn component is far more likely to have one.
        /// Only components the car has (petrol vs electric) can get faults.
        /// </summary>
        public static List<FaultInstance> Generate(List<ComponentState> components, int mileage, Random rng)
        {
            var result = new List<FaultInstance>();
            float age = Math.Min(1f, mileage / 200000f);
            foreach (var def in FaultCatalog.All)
            {
                var c = ComponentSet.Find(components, def.Component);
                if (c == null) continue;
                float wear = Math.Max(0f, Math.Min(1f, (100f - c.health) / 70f));
                float p = def.Chance * (0.02f + 0.30f * wear * wear + 0.04f * age);
                if (rng.NextDouble() >= p) continue;
                float severity = Clamp01(0.15f + wear * 0.6f + (float)rng.NextDouble() * 0.3f);
                result.Add(new FaultInstance(def.Id, severity));
            }
            ApplyHealthCaps(components, result);
            return result;
        }

        /// <summary>A component with a fault can't be healthier than the fault allows.</summary>
        public static void ApplyHealthCaps(List<ComponentState> components, List<FaultInstance> faults)
        {
            foreach (var f in faults)
            {
                var def = f.Def;
                if (def == null) continue;
                var c = ComponentSet.Find(components, def.Component);
                if (c == null) continue;
                float cap = 100f + (def.HealthAtFull - 100f) * f.severity;
                if (c.health > cap) c.health = cap;
            }
        }

        /// <summary>Faults get worse over time. Returns the faults that crossed into "severe" (≥ 0.6) today.</summary>
        public static List<FaultInstance> Progress(List<ComponentState> components, List<FaultInstance> faults, int days)
        {
            var worse = new List<FaultInstance>();
            if (faults == null || days <= 0) return worse;
            foreach (var f in faults)
            {
                var def = f.Def;
                if (def == null || def.ProgressPerDay <= 0f) continue;
                float before = f.severity;
                f.severity = Clamp01(f.severity + def.ProgressPerDay * days);
                if (before < 0.6f && f.severity >= 0.6f) worse.Add(f);
            }
            ApplyHealthCaps(components, faults);
            return worse;
        }

        /// <summary>Share of value lost to faults (capped at 60%). knownOnly = what a buyer who hasn't looked closer sees.</summary>
        public static float ValueLoss(List<FaultInstance> faults, bool knownOnly)
        {
            if (faults == null) return 0f;
            float loss = 0f;
            foreach (var f in faults)
            {
                if (knownOnly && !f.known) continue;
                var def = f.Def;
                if (def != null) loss += def.ValueImpact * (0.4f + 0.6f * f.severity);
            }
            return Math.Min(0.6f, loss);
        }

        /// <summary>Removes faults on the given components (after a repair). Returns how many were fixed.</summary>
        public static int ClearOn(List<FaultInstance> faults, ICollection<ComponentId> components)
        {
            if (faults == null) return 0;
            return faults.RemoveAll(f => f.Def != null && components.Contains(f.Def.Component));
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
