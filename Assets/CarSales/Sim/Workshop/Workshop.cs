using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum PartTier { Used = 0, Aftermarket = 1, OEM = 2 }
    public enum Supplier { MotorFactor = 0, Online = 1, BreakersYard = 2, Counter = 3 }   // Counter = in person at Parts Direct
    public enum Station { Mechanical, Tyres, Body, Detailing, Diagnostic }

    public sealed class PartDef
    {
        public string Id, Name;
        public int BasePrice;        // OEM price for an average car, in game dollars
        public bool UsedAvailable;   // filters, fluids, pads... are never sold used
    }

    public sealed class RepairJobDef
    {
        public string Id, Name;
        public ComponentId[] Components;
        public (string part, int count)[] Parts;
        public float Hours;
        public Station Station;
    }

    [Serializable] public class PartStock { public string part; public int tier; public int count; }
    [Serializable] public class PartOrder { public string part; public int tier; public int count; public int arrivesDay; public int supplier; }

    /// <summary>A repair in progress at the garage (labour hours left in game time).</summary>
    [Serializable]
    public class WorkshopJob
    {
        public string job;
        public string carId;
        public int tier;
        public float hoursLeft;
        public float hoursTotal;
        public int labourCost;
        public int startedDay;
    }

    /// <summary>Spare parts in storage plus orders on their way.</summary>
    [Serializable]
    public class PartsInventory
    {
        public List<PartStock> stock = new List<PartStock>();
        public List<PartOrder> orders = new List<PartOrder>();

        PartStock Find(string part, PartTier tier) => stock.Find(s => s.part == part && s.tier == (int)tier);

        public int Count(string part, PartTier tier) => Find(part, tier)?.count ?? 0;

        public void Add(string part, PartTier tier, int count)
        {
            if (count <= 0) return;
            var s = Find(part, tier);
            if (s == null) stock.Add(new PartStock { part = part, tier = (int)tier, count = count });
            else s.count += count;
        }

        public bool Take(string part, PartTier tier, int count)
        {
            var s = Find(part, tier);
            if (s == null || s.count < count) return false;
            s.count -= count;
            if (s.count == 0) stock.Remove(s);
            return true;
        }

        /// <summary>Has every part a job needs in this tier.</summary>
        public bool HasPartsFor(RepairJobDef job, PartTier tier)
        {
            foreach (var (part, count) in job.Parts) if (Count(part, tier) < count) return false;
            return true;
        }

        public bool TakePartsFor(RepairJobDef job, PartTier tier)
        {
            if (!HasPartsFor(job, tier)) return false;
            foreach (var (part, count) in job.Parts) Take(part, tier, count);
            return true;
        }

        public int Pending(string part, PartTier tier)
        {
            int n = 0;
            foreach (var o in orders) if (o.part == part && o.tier == (int)tier) n += o.count;
            return n;
        }

        /// <summary>Moves orders that have arrived into stock. Returns the delivered orders.</summary>
        public List<PartOrder> Receive(int day)
        {
            var arrived = orders.FindAll(o => o.arrivesDay <= day);
            foreach (var o in arrived) Add(o.part, (PartTier)o.tier, o.count);
            orders.RemoveAll(o => o.arrivesDay <= day);
            return arrived;
        }
    }

    /// <summary>Parts, suppliers and repair jobs (spec §6.6), plus the maths. Pure functions (tests in WorkshopTests).</summary>
    public static class WorkshopCatalog
    {
        public const int LabourRate = 65;   // per hour at the town garage

        public static readonly IReadOnlyList<PartDef> Parts;
        public static readonly IReadOnlyList<RepairJobDef> Jobs;
        static readonly Dictionary<string, PartDef> partById = new Dictionary<string, PartDef>();
        static readonly Dictionary<string, RepairJobDef> jobById = new Dictionary<string, RepairJobDef>();

        static WorkshopCatalog()
        {
            var parts = new List<PartDef>();
            void P(string id, string name, int price, bool used = true) => parts.Add(new PartDef { Id = id, Name = name, BasePrice = price, UsedAvailable = used });
            P("oil_kit", "Oil & filter kit", 45, false);
            P("timing_kit", "Timing belt kit & water pump", 190);
            P("plugs", "Spark / glow plugs & coil", 80);
            P("head_set", "Head gasket set", 160, false);
            P("engine_kit", "Engine rebuild kit", 1400);
            P("radiator", "Radiator & hoses", 210);
            P("turbo", "Turbocharger", 650);
            P("gearbox_kit", "Gearbox bearing kit", 520);
            P("clutch_kit", "Clutch kit", 330);
            P("exhaust", "Exhaust & catalyst", 420);
            P("ev_module", "Traction battery module", 1800);
            P("ev_motor", "Motor bearing set", 450);
            P("inverter", "Inverter", 900);
            P("charge_port", "Charge port & on-board charger", 380);
            P("battery12", "12V battery", 120, false);
            P("alternator", "Alternator", 260);
            P("starter", "Starter motor", 210);
            P("sensors", "Sensor set (lambda, MAF)", 150);
            P("bulbs", "Bulb & lamp set", 35, false);
            P("head_unit", "Infotainment head unit", 300);
            P("loom", "Wiring loom section", 240);
            P("pads_f", "Front brake pads", 60, false);
            P("pads_r", "Rear brake pads", 50, false);
            P("discs", "Brake disc pair", 130);
            P("brake_fluid", "Brake fluid", 20, false);
            P("shock", "Shock absorber", 110);
            P("spring", "Coil spring", 70);
            P("arm", "Wishbone with bushes", 95);
            P("rack", "Steering rack", 480);
            P("tyre", "Tyre", 95);
            P("body_kit", "Filler, primer & paint", 180, false);
            P("panel", "Body panel", 240);
            P("sill", "Sill repair section", 90, false);
            P("windscreen", "Windscreen", 280);
            P("trim_kit", "Interior trim & cleaning kit", 160);
            Parts = parts;
            foreach (var p in parts) partById[p.Id] = p;

            var jobs = new List<RepairJobDef>();
            void J(string id, string name, float hours, Station st, ComponentId[] comps, params (string, int)[] ps)
                => jobs.Add(new RepairJobDef { Id = id, Name = name, Hours = hours, Station = st, Components = comps, Parts = ps });
            ComponentId[] C(params ComponentId[] c) => c;
            const Station M = Station.Mechanical;
            J("oil_service", "Oil & filter service", 0.8f, M, C(ComponentId.OilAndFilter), ("oil_kit", 1));
            J("timing_belt", "Timing belt & water pump", 3f, M, C(ComponentId.TimingBelt), ("timing_kit", 1));
            J("ignition", "Plugs & coil pack", 1f, M, C(ComponentId.Ignition), ("plugs", 1));
            J("head_gasket", "Head gasket replacement", 8f, M, C(ComponentId.CylinderHead), ("head_set", 1));
            J("engine_rebuild", "Engine rebuild", 16f, M, C(ComponentId.EngineInternals), ("engine_kit", 1));
            J("cooling", "Radiator & hoses", 2f, M, C(ComponentId.CoolingSystem), ("radiator", 1));
            J("turbo", "Turbo replacement", 4f, M, C(ComponentId.IntakeAndTurbo), ("turbo", 1));
            J("gearbox", "Gearbox bearings", 8f, M, C(ComponentId.Gearbox), ("gearbox_kit", 1));
            J("clutch", "Clutch replacement", 5f, M, C(ComponentId.Clutch), ("clutch_kit", 1));
            J("exhaust", "Exhaust & catalyst", 1.5f, M, C(ComponentId.Exhaust), ("exhaust", 1));
            J("ev_battery", "Battery module swap", 6f, Station.Diagnostic, C(ComponentId.TractionBattery), ("ev_module", 1));
            J("ev_motor", "Motor bearings", 4f, M, C(ComponentId.ElectricMotor), ("ev_motor", 1));
            J("inverter", "Inverter replacement", 3f, Station.Diagnostic, C(ComponentId.Inverter), ("inverter", 1));
            J("charge_port", "Charge port & charger", 1f, Station.Diagnostic, C(ComponentId.ChargingSystem), ("charge_port", 1));
            J("battery12", "12V battery", 0.3f, M, C(ComponentId.Battery12V), ("battery12", 1));
            J("alternator", "Alternator", 1.5f, M, C(ComponentId.Alternator), ("alternator", 1));
            J("starter", "Starter motor", 1.5f, M, C(ComponentId.StarterMotor), ("starter", 1));
            J("sensors", "Sensors & ECU reset", 1f, Station.Diagnostic, C(ComponentId.EcuAndSensors), ("sensors", 1));
            J("lights", "Bulbs & lamps", 0.3f, M, C(ComponentId.Lights), ("bulbs", 1));
            J("infotainment", "Infotainment unit", 1f, Station.Diagnostic, C(ComponentId.Infotainment), ("head_unit", 1));
            J("wiring", "Wiring repair", 3f, Station.Diagnostic, C(ComponentId.Wiring), ("loom", 1));
            J("pads_front", "Front brake pads", 0.8f, M, C(ComponentId.BrakePadsFront), ("pads_f", 1));
            J("pads_rear", "Rear brake pads", 0.8f, M, C(ComponentId.BrakePadsRear), ("pads_r", 1));
            J("discs", "Brake discs", 1.2f, M, C(ComponentId.BrakeDiscs), ("discs", 1));
            J("brake_fluid", "Brake fluid change", 0.6f, M, C(ComponentId.BrakeFluid), ("brake_fluid", 1));
            J("shocks", "Shock absorbers (pair)", 2.5f, M, C(ComponentId.ShockAbsorbers), ("shock", 2));
            J("springs", "Coil springs (pair)", 2f, M, C(ComponentId.Springs), ("spring", 2));
            J("bushes", "Suspension arms & bushes", 2f, M, C(ComponentId.BushesAndArms), ("arm", 2));
            J("steering_rack", "Steering rack", 3f, M, C(ComponentId.SteeringRack), ("rack", 1));
            J("alignment", "Wheel alignment", 0.7f, Station.Tyres, C(ComponentId.Alignment));
            J("tyres", "Four tyres", 1f, Station.Tyres, C(ComponentId.TyreFrontLeft, ComponentId.TyreFrontRight, ComponentId.TyreRearLeft, ComponentId.TyreRearRight), ("tyre", 4));
            J("bodywork", "Bodywork & paint", 6f, Station.Body,
              C(ComponentId.BumperFront, ComponentId.BumperRear, ComponentId.Bonnet, ComponentId.BootLid, ComponentId.DoorsLeft, ComponentId.DoorsRight, ComponentId.WingsFront, ComponentId.Roof, ComponentId.Paint),
              ("body_kit", 1), ("panel", 1));
            J("sill", "Rust & sill repair", 4f, Station.Body, C(ComponentId.QuarterPanelsRear), ("sill", 2));
            J("windscreen", "Windscreen", 1.5f, Station.Body, C(ComponentId.Glass), ("windscreen", 1));
            J("interior", "Interior retrim & deep clean", 4f, Station.Detailing, C(ComponentId.Seats, ComponentId.Dashboard, ComponentId.Carpets, ComponentId.Headliner), ("trim_kit", 1));
            Jobs = jobs;
            foreach (var j in jobs) jobById[j.Id] = j;
        }

        public static PartDef Part(string id) => id != null && partById.TryGetValue(id, out var p) ? p : null;
        public static RepairJobDef Job(string id) => id != null && jobById.TryGetValue(id, out var j) ? j : null;

        public static RepairJobDef JobFor(ComponentId c)
        {
            foreach (var j in Jobs) if (Array.IndexOf(j.Components, c) >= 0) return j;
            return null;
        }

        /// <summary>Bigger, pricier cars cost more to fix: price factor from the car's base price.</summary>
        public static float CarClassFactor(int carBasePrice) => Math.Max(0.7f, Math.Min(2.5f, carBasePrice / 20000f));

        public static float TierFactor(PartTier t) => t == PartTier.Used ? 0.4f : t == PartTier.Aftermarket ? 0.7f : 1f;

        public static float SupplierFactor(Supplier s) => s == Supplier.Online ? 0.85f : s == Supplier.Counter ? 0.9f : 1f;

        public static int LeadDays(Supplier s) => s == Supplier.Online ? 1 : 0;

        public static bool Sells(Supplier s, PartDef p, PartTier t) =>
            s == Supplier.BreakersYard ? t == PartTier.Used && p.UsedAvailable : t != PartTier.Used;

        /// <summary>The breaker's yard has a random selection that changes every day.</summary>
        public static bool BreakerHasToday(string partId, int day)
        {
            var p = Part(partId);
            if (p == null || !p.UsedAvailable) return false;
            int h = VehicleRecords.Seed(partId + "#" + day);
            return h % 100 < 60;
        }

        public static int PartPrice(string partId, PartTier tier, Supplier supplier, int carBasePrice)
        {
            var p = Part(partId);
            if (p == null) return 0;
            return (int)Math.Round(p.BasePrice * TierFactor(tier) * SupplierFactor(supplier) * CarClassFactor(carBasePrice));
        }

        public static int PartsPrice(RepairJobDef job, PartTier tier, Supplier supplier, int carBasePrice)
        {
            int sum = 0;
            foreach (var (part, count) in job.Parts) sum += PartPrice(part, tier, supplier, carBasePrice) * count;
            return sum;
        }

        public static int LabourCost(RepairJobDef job) => (int)Math.Round(job.Hours * LabourRate);

        /// <summary>Health a part ends up with after fitting: used parts vary, aftermarket is good, OEM is perfect.</summary>
        public static float ResultHealth(PartTier tier, Random rng) =>
            tier == PartTier.OEM ? 100f : tier == PartTier.Aftermarket ? 85f + (float)rng.NextDouble() * 7f : 50f + (float)rng.NextDouble() * 30f;

        /// <summary>DIY fail chance at a skill of 0-1 (a botched job leaves the part 25 points worse).</summary>
        public static float DiyFailChance(float skill) => 0.15f * (1f - Math.Max(0f, Math.Min(1f, skill)));

        /// <summary>Fits the job's parts: sets the components' health and clears faults on them. Returns faults fixed.</summary>
        public static int Apply(RepairJobDef job, List<ComponentState> components, List<FaultInstance> faults, PartTier tier, Random rng, bool botched = false)
        {
            foreach (var id in job.Components)
            {
                var c = ComponentSet.Find(components, id);
                if (c == null) continue;
                float h = ResultHealth(tier, rng);
                if (botched) h = Math.Max(10f, h - 25f);
                c.health = Math.Max(c.health, h);
            }
            return FaultModel.ClearOn(faults, job.Components);
        }

        /// <summary>Jobs worth doing on this car: components below <paramref name="threshold"/> or with a known fault.</summary>
        public static List<RepairJobDef> Suggested(List<ComponentState> components, List<FaultInstance> faults, float threshold = 70f)
        {
            var list = new List<RepairJobDef>();
            foreach (var job in Jobs)
            {
                bool want = false;
                foreach (var id in job.Components)
                {
                    var c = ComponentSet.Find(components, id);
                    if (c == null) continue;
                    if (c.health < threshold) want = true;
                    if (faults != null) foreach (var f in faults) if (f.known && f.Def != null && f.Def.Component == id) want = true;
                }
                if (want) list.Add(job);
            }
            return list;
        }
    }
}
