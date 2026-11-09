using System;

namespace CarSales.Sim
{
    public enum PowertrainType { Petrol, Diesel, Hybrid, Electric }
    public enum Drivetrain { FWD, RWD, AWD }

    /// <summary>Handling classes. Each has a preset physics profile (see <see cref="VehicleProfiles"/>).</summary>
    public enum VehicleClass { CityHatch, FamilyHatch, Sedan, SportsSedan, SUV, Pickup, Van, Sports, Luxury, ElectricHatch }

    public static class PowertrainTypes
    {
        /// <summary>Reads the free-text fuel type of a car definition ("Petrol", "Diesel", "Hybrid", "Electric", "EV"...).</summary>
        public static PowertrainType Parse(string fuelType)
        {
            string f = (fuelType ?? "").Trim().ToLowerInvariant();
            if (f.Contains("electric") || f == "ev" || f.Contains("bev")) return PowertrainType.Electric;
            if (f.Contains("hybrid") || f.Contains("phev")) return PowertrainType.Hybrid;
            if (f.Contains("diesel")) return PowertrainType.Diesel;
            return PowertrainType.Petrol;
        }

        public static bool HasCombustionEngine(PowertrainType p) => p != PowertrainType.Electric;
    }

    /// <summary>Piecewise-linear torque curve: torque factor (0-1 of peak) at each rpm point.</summary>
    [Serializable]
    public class TorqueCurve
    {
        public float[] rpm = new float[0];
        public float[] factor = new float[0];

        public float FactorAt(float r)
        {
            int n = Math.Min(rpm.Length, factor.Length);
            if (n == 0) return 1f;
            if (r <= rpm[0]) return factor[0];
            for (int i = 1; i < n; i++)
            {
                if (r <= rpm[i])
                {
                    float t = (r - rpm[i - 1]) / Math.Max(1f, rpm[i] - rpm[i - 1]);
                    return factor[i - 1] + (factor[i] - factor[i - 1]) * t;
                }
            }
            return factor[n - 1];
        }

        /// <summary>A plausible curve: rises from idle to the peak, holds it (turbo plateau), falls towards the redline.</summary>
        public static TorqueCurve Make(float idle, float peakRpm, float plateauEndRpm, float redline)
        {
            peakRpm = Clamp(peakRpm, idle + 100f, redline - 200f);
            plateauEndRpm = Clamp(plateauEndRpm, peakRpm, redline - 100f);
            float mid = idle + (peakRpm - idle) * 0.5f;
            return new TorqueCurve
            {
                rpm = new[] { idle, mid, peakRpm, plateauEndRpm, (plateauEndRpm + redline) * 0.5f, redline },
                factor = new[] { 0.58f, 0.86f, 1f, 1f, 0.9f, 0.76f }
            };
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    [Serializable]
    public class EngineSpec
    {
        public float idleRpm = 850f;
        public float redlineRpm = 6500f;
        public float peakTorqueNm = 150f;
        public float peakTorqueRpm = 3500f;
        public float plateauEndRpm = 3500f;
        public float engineBrakeNm = 25f;
        [NonSerialized] TorqueCurve curve;

        public TorqueCurve Curve => curve ?? (curve = TorqueCurve.Make(idleRpm, peakTorqueRpm, plateauEndRpm, redlineRpm));
        public float TorqueAt(float rpm) => rpm >= redlineRpm ? 0f : peakTorqueNm * Curve.FactorAt(rpm);
        /// <summary>Call after changing the numbers at runtime so the curve is rebuilt.</summary>
        public void Invalidate() => curve = null;
        public EngineSpec Copy() { var c = (EngineSpec)MemberwiseClone(); c.curve = null; return c; }
    }

    [Serializable]
    public class GearboxSpec
    {
        public float[] ratios = { 3.5f, 2.0f, 1.35f, 1.0f, 0.8f };
        public float reverseRatio = 3.4f;
        public float finalDrive = 4.0f;
        public float upshiftRpm = 6000f;
        public float downshiftRpm = 2200f;
        public float shiftTime = 0.25f;
        public float efficiency = 0.9f;

        public int GearCount => ratios == null ? 0 : ratios.Length;
        public GearboxSpec Copy() { var c = (GearboxSpec)MemberwiseClone(); c.ratios = (float[])ratios.Clone(); return c; }
    }

    [Serializable]
    public class ElectricSpec
    {
        public float maxTorqueNm = 300f;
        public float maxPowerKw = 150f;
        public float maxRpm = 12000f;
        public float ratio = 9f;
        public float regenFraction = 0.25f;     // share of max torque used for regen braking when off throttle
        public float efficiency = 0.92f;
        public ElectricSpec Copy() => (ElectricSpec)MemberwiseClone();
    }

    [Serializable]
    public class ChassisSpec
    {
        public float massKg = 1300f;
        public float centerOfMassHeight = 0.45f;  // metres above the ground
        public float wheelRadius = 0f;            // 0 = measured from the model
        public float suspensionDistance = 0.2f;
        public float springScale = 1f;
        public float damperScale = 1f;
        public float forwardGrip = 1f;
        public float sidewaysGrip = 1.05f;
        public float maxSteerAngle = 35f;
        public float highSpeedSteerFactor = 0.35f; // share of steering left at 120 km/h
        public float antiRollForce = 6000f;
        public float brakeStrength = 1.0f;        // ~1 = brakes can hold 1 g
        public float frontBrakeBias = 0.65f;
        public float handbrakeTorque = 2500f;
        public float dragArea = 0.68f;            // Cd x frontal area, m^2
        public float rollingResistance = 0.013f;
        public float downforce = 1.2f;
        public float topSpeedLimitKmh = 0f;       // 0 = no limiter
        public ChassisSpec Copy() => (ChassisSpec)MemberwiseClone();
    }

    /// <summary>Everything the physics needs to drive one kind of car. Plain data; presets in <see cref="VehicleProfiles"/>.</summary>
    [Serializable]
    public class VehicleProfile
    {
        public string name = "Custom";
        public VehicleClass vehicleClass = VehicleClass.Sedan;
        public PowertrainType powertrain = PowertrainType.Petrol;
        public Drivetrain drivetrain = Drivetrain.FWD;
        public EngineSpec engine = new EngineSpec();
        public GearboxSpec gearbox = new GearboxSpec();
        public ElectricSpec electric = new ElectricSpec();
        public ChassisSpec chassis = new ChassisSpec();
        /// <summary>Fuel (or battery) % used per km at average load. 4 = a full tank lasts ~25 km in the game world.</summary>
        public float energyPercentPerKm = 4f;

        public bool IsElectric => powertrain == PowertrainType.Electric;

        /// <summary>The profile's wheel radius, or <paramref name="fallback"/> (measured from the model) when it doesn't set one.</summary>
        public float WheelRadiusOr(float fallback) => chassis.wheelRadius > 0.05f ? chassis.wheelRadius : fallback;

        /// <summary>Deep copy (so a car can tweak its profile without changing the preset).</summary>
        public VehicleProfile Copy()
        {
            var c = (VehicleProfile)MemberwiseClone();
            c.engine = engine.Copy();
            c.gearbox = gearbox.Copy();
            c.electric = electric.Copy();
            c.chassis = chassis.Copy();
            return c;
        }
    }
}
