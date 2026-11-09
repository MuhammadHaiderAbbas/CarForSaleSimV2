using System;

namespace CarSales.Sim
{
    public struct PerformanceResult
    {
        public float ZeroTo100Seconds;   // -1 if it never got there
        public float TopSpeedKmh;
        public int TopGear;
        public float StoppingDistance100; // metres from 100 km/h at full braking
    }

    /// <summary>
    /// Straight-line simulation of a profile (traction-limited, aero drag, rolling resistance) to check that a car
    /// performs like its class. Used by the tests and the Vehicle Studio window.
    /// </summary>
    public static class PerformanceEstimator
    {
        const float G = 9.81f, AirDensity = 1.225f;
        const float RotatingMassFactor = 1.06f;   // wheels, driveshafts and flywheel add ~6% effective mass

        public static PerformanceResult Run(VehicleProfile p, float maxSeconds = 90f, float dt = 0.01f)
        {
            var result = new PerformanceResult { ZeroTo100Seconds = -1f };
            var pt = new Powertrain(p);
            var ch = p.chassis;
            float r = p.WheelRadiusOr(0.32f);
            float mass = Math.Max(300f, ch.massKg);
            float drivenShare = p.drivetrain == Drivetrain.AWD ? 1f : p.drivetrain == Drivetrain.RWD ? 0.56f : 0.54f;
            float tractionLimit = 1.05f * ch.forwardGrip * mass * G * drivenShare;

            float v = 0f, t = 0f, lastV = 0f, stall = 0f;
            while (t < maxSeconds)
            {
                float wheelRpm = v / r * 60f / (2f * (float)Math.PI);
                bool limited = ch.topSpeedLimitKmh > 0f && v * 3.6f >= ch.topSpeedLimitKmh;
                float torque = pt.Step(dt, limited ? 0.05f : 1f, false, wheelRpm, v);
                if (limited) torque = Math.Max(0f, torque);
                float drive = Math.Min(torque / r, tractionLimit);
                float drag = 0.5f * AirDensity * ch.dragArea * v * v;
                float roll = ch.rollingResistance * mass * G;
                float a = (drive - drag - roll) / (mass * RotatingMassFactor);
                v = Math.Max(0f, v + a * dt);
                t += dt;
                if (result.ZeroTo100Seconds < 0f && v >= 100f / 3.6f) result.ZeroTo100Seconds = t;
                if (v * 3.6f > result.TopSpeedKmh) { result.TopSpeedKmh = v * 3.6f; result.TopGear = pt.Gear; }
                stall = v - lastV < 0.0005f ? stall + dt : 0f;          // stop when it can't accelerate any more
                lastV = v;
                if (stall > 3f) break;
            }

            // Braking from 100 km/h: brakes hold ~brakeStrength g, limited by tyre grip.
            float decel = Math.Min(ch.brakeStrength, 1.05f * ch.forwardGrip) * G;
            float v0 = 100f / 3.6f;
            result.StoppingDistance100 = v0 * v0 / (2f * Math.Max(0.1f, decel));
            return result;
        }
    }
}
