using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    /// <summary>The six condition groups shown since v3 (same order as the runtime's CarPart enum).</summary>
    public enum ComponentGroup { Engine = 0, Brakes = 1, Tires = 2, Body = 3, Interior = 4, Electronics = 5 }

    /// <summary>
    /// Every individual part of a car. Stored in saves as numbers: only ADD new ids, never renumber.
    /// </summary>
    public enum ComponentId
    {
        // Engine & drivetrain (combustion)
        EngineInternals = 0, CylinderHead = 1, TimingBelt = 2, OilAndFilter = 3, CoolingSystem = 4, Ignition = 5,
        IntakeAndTurbo = 6, Gearbox = 7, Clutch = 8, Exhaust = 9,
        // Electric drivetrain
        TractionBattery = 20, ElectricMotor = 21, Inverter = 22, ChargingSystem = 23,
        // Electronics
        Battery12V = 30, Alternator = 31, StarterMotor = 32, EcuAndSensors = 33, Lights = 34, Infotainment = 35, Wiring = 36,
        // Brakes, suspension & steering
        BrakePadsFront = 40, BrakePadsRear = 41, BrakeDiscs = 42, BrakeFluid = 43, ShockAbsorbers = 44, Springs = 45,
        BushesAndArms = 46, SteeringRack = 47, Alignment = 48,
        // Tyres
        TyreFrontLeft = 60, TyreFrontRight = 61, TyreRearLeft = 62, TyreRearRight = 63,
        // Body
        BumperFront = 70, BumperRear = 71, Bonnet = 72, BootLid = 73, DoorsLeft = 74, DoorsRight = 75, WingsFront = 76,
        QuarterPanelsRear = 77, Roof = 78, Paint = 79, Glass = 80,
        // Interior
        Seats = 90, Dashboard = 91, Carpets = 92, Headliner = 93,
    }

    public enum ComponentFit { All, CombustionOnly, ElectricOnly }

    public enum ImpactZone { Front, Rear, Left, Right, Roof, Underside }

    public sealed class ComponentDef
    {
        public ComponentId Id;
        public ComponentGroup Group;
        public string Name;
        public float Weight;          // share of its group's condition
        public ComponentFit Fit;
        public float WearPerKm;       // condition points lost per km driven (normal driving)

        public bool FitsTo(PowertrainType p) =>
            Fit == ComponentFit.All ||
            (Fit == ComponentFit.CombustionOnly && PowertrainTypes.HasCombustionEngine(p)) ||
            (Fit == ComponentFit.ElectricOnly && !PowertrainTypes.HasCombustionEngine(p));
    }

    /// <summary>The list of all components, their group weights, wear rates and which crash zones hit them.</summary>
    public static class ComponentCatalog
    {
        public static readonly IReadOnlyList<ComponentDef> All;
        static readonly Dictionary<ComponentId, ComponentDef> byId = new Dictionary<ComponentId, ComponentDef>();
        static readonly Dictionary<ImpactZone, KeyValuePair<ComponentId, float>[]> zones;

        static ComponentCatalog()
        {
            var list = new List<ComponentDef>();
            void Add(ComponentId id, ComponentGroup g, string name, float weight, ComponentFit fit = ComponentFit.All, float wear = 0.01f)
                => list.Add(new ComponentDef { Id = id, Group = g, Name = name, Weight = weight, Fit = fit, WearPerKm = wear });

            const ComponentFit Ice = ComponentFit.CombustionOnly, Ev = ComponentFit.ElectricOnly;
            // Engine group. Combustion weights add up to 1; electric weights add up to 1 (with the shared gearbox).
            Add(ComponentId.EngineInternals, ComponentGroup.Engine, "Engine internals", 0.30f, Ice, 0.004f);
            Add(ComponentId.CylinderHead, ComponentGroup.Engine, "Cylinder head & gasket", 0.12f, Ice, 0.002f);
            Add(ComponentId.TimingBelt, ComponentGroup.Engine, "Timing belt", 0.08f, Ice, 0.012f);
            Add(ComponentId.OilAndFilter, ComponentGroup.Engine, "Oil & filter", 0.08f, Ice, 0.08f);
            Add(ComponentId.CoolingSystem, ComponentGroup.Engine, "Cooling system", 0.10f, ComponentFit.All, 0.004f);
            Add(ComponentId.Ignition, ComponentGroup.Engine, "Ignition / glow plugs", 0.08f, Ice, 0.01f);
            Add(ComponentId.IntakeAndTurbo, ComponentGroup.Engine, "Intake & turbo", 0.06f, Ice, 0.004f);
            Add(ComponentId.Gearbox, ComponentGroup.Engine, "Gearbox", 0.10f, ComponentFit.All, 0.003f);
            Add(ComponentId.Clutch, ComponentGroup.Engine, "Clutch / torque converter", 0.05f, Ice, 0.015f);
            Add(ComponentId.Exhaust, ComponentGroup.Engine, "Exhaust & catalyst", 0.03f, Ice, 0.006f);
            Add(ComponentId.TractionBattery, ComponentGroup.Engine, "Traction battery", 0.45f, Ev, 0.002f);
            Add(ComponentId.ElectricMotor, ComponentGroup.Engine, "Electric motor", 0.20f, Ev, 0.001f);
            Add(ComponentId.Inverter, ComponentGroup.Engine, "Inverter", 0.10f, Ev, 0.001f);
            Add(ComponentId.ChargingSystem, ComponentGroup.Engine, "Charging port & charger", 0.05f, Ev, 0.002f);
            // (Electric engine group: battery .45 + motor .20 + inverter .10 + charging .05 + cooling .10 + gearbox .10 = 1)

            Add(ComponentId.Battery12V, ComponentGroup.Electronics, "12V battery", 0.25f, ComponentFit.All, 0.01f);
            Add(ComponentId.Alternator, ComponentGroup.Electronics, "Alternator", 0.15f, Ice, 0.004f);
            Add(ComponentId.StarterMotor, ComponentGroup.Electronics, "Starter motor", 0.10f, Ice, 0.004f);
            Add(ComponentId.EcuAndSensors, ComponentGroup.Electronics, "ECU & sensors", 0.20f, ComponentFit.All, 0.002f);
            Add(ComponentId.Lights, ComponentGroup.Electronics, "Lights", 0.15f, ComponentFit.All, 0.004f);
            Add(ComponentId.Infotainment, ComponentGroup.Electronics, "Infotainment & dash", 0.10f, ComponentFit.All, 0.001f);
            Add(ComponentId.Wiring, ComponentGroup.Electronics, "Wiring", 0.05f, ComponentFit.All, 0.001f);

            Add(ComponentId.BrakePadsFront, ComponentGroup.Brakes, "Front brake pads", 0.15f, ComponentFit.All, 0.05f);
            Add(ComponentId.BrakePadsRear, ComponentGroup.Brakes, "Rear brake pads", 0.10f, ComponentFit.All, 0.03f);
            Add(ComponentId.BrakeDiscs, ComponentGroup.Brakes, "Brake discs", 0.15f, ComponentFit.All, 0.02f);
            Add(ComponentId.BrakeFluid, ComponentGroup.Brakes, "Brake fluid & lines", 0.10f, ComponentFit.All, 0.01f);
            Add(ComponentId.ShockAbsorbers, ComponentGroup.Brakes, "Shock absorbers", 0.15f, ComponentFit.All, 0.01f);
            Add(ComponentId.Springs, ComponentGroup.Brakes, "Springs", 0.10f, ComponentFit.All, 0.004f);
            Add(ComponentId.BushesAndArms, ComponentGroup.Brakes, "Bushes & arms", 0.10f, ComponentFit.All, 0.008f);
            Add(ComponentId.SteeringRack, ComponentGroup.Brakes, "Steering rack", 0.10f, ComponentFit.All, 0.003f);
            Add(ComponentId.Alignment, ComponentGroup.Brakes, "Wheel alignment", 0.05f, ComponentFit.All, 0.02f);

            Add(ComponentId.TyreFrontLeft, ComponentGroup.Tires, "Front left tyre", 0.25f, ComponentFit.All, 0.06f);
            Add(ComponentId.TyreFrontRight, ComponentGroup.Tires, "Front right tyre", 0.25f, ComponentFit.All, 0.06f);
            Add(ComponentId.TyreRearLeft, ComponentGroup.Tires, "Rear left tyre", 0.25f, ComponentFit.All, 0.045f);
            Add(ComponentId.TyreRearRight, ComponentGroup.Tires, "Rear right tyre", 0.25f, ComponentFit.All, 0.045f);

            Add(ComponentId.BumperFront, ComponentGroup.Body, "Front bumper", 0.12f, ComponentFit.All, 0.001f);
            Add(ComponentId.BumperRear, ComponentGroup.Body, "Rear bumper", 0.10f, ComponentFit.All, 0.001f);
            Add(ComponentId.Bonnet, ComponentGroup.Body, "Bonnet", 0.10f, ComponentFit.All, 0.0005f);
            Add(ComponentId.BootLid, ComponentGroup.Body, "Boot lid", 0.08f, ComponentFit.All, 0.0005f);
            Add(ComponentId.DoorsLeft, ComponentGroup.Body, "Left doors", 0.12f, ComponentFit.All, 0.0005f);
            Add(ComponentId.DoorsRight, ComponentGroup.Body, "Right doors", 0.12f, ComponentFit.All, 0.0005f);
            Add(ComponentId.WingsFront, ComponentGroup.Body, "Front wings", 0.10f, ComponentFit.All, 0.0005f);
            Add(ComponentId.QuarterPanelsRear, ComponentGroup.Body, "Rear quarter panels", 0.08f, ComponentFit.All, 0.0005f);
            Add(ComponentId.Roof, ComponentGroup.Body, "Roof", 0.05f, ComponentFit.All, 0.0003f);
            Add(ComponentId.Paint, ComponentGroup.Body, "Paint", 0.08f, ComponentFit.All, 0.003f);
            Add(ComponentId.Glass, ComponentGroup.Body, "Windscreen & glass", 0.05f, ComponentFit.All, 0.001f);

            Add(ComponentId.Seats, ComponentGroup.Interior, "Seats", 0.35f, ComponentFit.All, 0.004f);
            Add(ComponentId.Dashboard, ComponentGroup.Interior, "Dashboard & trim", 0.25f, ComponentFit.All, 0.002f);
            Add(ComponentId.Carpets, ComponentGroup.Interior, "Carpets & mats", 0.20f, ComponentFit.All, 0.004f);
            Add(ComponentId.Headliner, ComponentGroup.Interior, "Headliner", 0.20f, ComponentFit.All, 0.001f);

            All = list;
            foreach (var d in list) byId[d.Id] = d;

            KeyValuePair<ComponentId, float> Z(ComponentId id, float share) => new KeyValuePair<ComponentId, float>(id, share);
            zones = new Dictionary<ImpactZone, KeyValuePair<ComponentId, float>[]>
            {
                [ImpactZone.Front] = new[] { Z(ComponentId.BumperFront, 1f), Z(ComponentId.Bonnet, 0.7f), Z(ComponentId.WingsFront, 0.5f),
                                             Z(ComponentId.Lights, 0.5f), Z(ComponentId.CoolingSystem, 0.45f), Z(ComponentId.Paint, 0.3f),
                                             Z(ComponentId.Alignment, 0.3f), Z(ComponentId.Glass, 0.15f) },
                [ImpactZone.Rear] = new[] { Z(ComponentId.BumperRear, 1f), Z(ComponentId.BootLid, 0.7f), Z(ComponentId.QuarterPanelsRear, 0.5f),
                                            Z(ComponentId.Lights, 0.35f), Z(ComponentId.Exhaust, 0.3f), Z(ComponentId.Paint, 0.3f) },
                [ImpactZone.Left] = new[] { Z(ComponentId.DoorsLeft, 1f), Z(ComponentId.WingsFront, 0.35f), Z(ComponentId.QuarterPanelsRear, 0.35f),
                                            Z(ComponentId.Paint, 0.35f), Z(ComponentId.Glass, 0.2f), Z(ComponentId.BushesAndArms, 0.2f),
                                            Z(ComponentId.TyreFrontLeft, 0.15f), Z(ComponentId.TyreRearLeft, 0.15f) },
                [ImpactZone.Right] = new[] { Z(ComponentId.DoorsRight, 1f), Z(ComponentId.WingsFront, 0.35f), Z(ComponentId.QuarterPanelsRear, 0.35f),
                                             Z(ComponentId.Paint, 0.35f), Z(ComponentId.Glass, 0.2f), Z(ComponentId.BushesAndArms, 0.2f),
                                             Z(ComponentId.TyreFrontRight, 0.15f), Z(ComponentId.TyreRearRight, 0.15f) },
                [ImpactZone.Roof] = new[] { Z(ComponentId.Roof, 1f), Z(ComponentId.Glass, 0.6f), Z(ComponentId.Paint, 0.4f), Z(ComponentId.Headliner, 0.3f) },
                [ImpactZone.Underside] = new[] { Z(ComponentId.Exhaust, 0.8f), Z(ComponentId.BushesAndArms, 0.6f), Z(ComponentId.ShockAbsorbers, 0.4f),
                                                 Z(ComponentId.Alignment, 0.6f), Z(ComponentId.OilAndFilter, 0.3f), Z(ComponentId.TractionBattery, 0.3f) },
            };
        }

        public static ComponentDef Get(ComponentId id) => byId.TryGetValue(id, out var d) ? d : null;
        public static bool IsKnown(int id) => byId.ContainsKey((ComponentId)id);

        public static IEnumerable<ComponentDef> For(PowertrainType p)
        {
            foreach (var d in All) if (d.FitsTo(p)) yield return d;
        }

        public static IEnumerable<ComponentDef> InGroup(ComponentGroup g, PowertrainType p)
        {
            foreach (var d in All) if (d.Group == g && d.FitsTo(p)) yield return d;
        }

        /// <summary>Components hit by an impact in <paramref name="zone"/> and how much of the damage each takes.</summary>
        public static IReadOnlyList<KeyValuePair<ComponentId, float>> Zone(ImpactZone zone) => zones[zone];

        /// <summary>
        /// Which side of the car a contact point (in the car's local space, metres, origin at ground centre) is on.
        /// </summary>
        public static ImpactZone ZoneFromLocalPoint(float x, float y, float z, float halfWidth, float halfLength, float height)
        {
            if (height > 0f && y > height * 0.85f) return ImpactZone.Roof;
            if (y < 0.12f) return ImpactZone.Underside;
            float nx = halfWidth > 0f ? Math.Abs(x) / halfWidth : 0f;
            float nz = halfLength > 0f ? Math.Abs(z) / halfLength : 0f;
            if (nz >= nx) return z >= 0f ? ImpactZone.Front : ImpactZone.Rear;
            return x >= 0f ? ImpactZone.Right : ImpactZone.Left;
        }

        public static string ZoneName(ImpactZone z)
        {
            switch (z)
            {
                case ImpactZone.Front: return "front";
                case ImpactZone.Rear: return "rear";
                case ImpactZone.Left: return "left side";
                case ImpactZone.Right: return "right side";
                case ImpactZone.Roof: return "roof";
                default: return "underside";
            }
        }
    }
}
