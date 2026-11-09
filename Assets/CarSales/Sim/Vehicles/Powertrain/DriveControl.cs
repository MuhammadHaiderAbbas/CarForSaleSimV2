using System;

namespace CarSales.Sim
{
    public struct DriveCommand
    {
        public float Throttle;   // 0..1
        public float Brake;      // 0..1
        public bool WantReverse;
    }

    /// <summary>
    /// Turns the single W/S axis into throttle, brake and reverse, like an automatic car in most driving games:
    /// S brakes while rolling forward and reverses once (almost) stopped; W does the opposite while reversing.
    /// </summary>
    public static class DriveInput
    {
        public const float DirectionChangeSpeed = 1.0f;   // m/s

        public static DriveCommand Map(float axis, float forwardSpeed, bool inReverse)
        {
            axis = axis < -1f ? -1f : axis > 1f ? 1f : axis;
            var c = new DriveCommand { WantReverse = inReverse };
            if (!inReverse)
            {
                if (axis > 0f) c.Throttle = axis;
                else if (axis < 0f)
                {
                    if (forwardSpeed > DirectionChangeSpeed) c.Brake = -axis;
                    else { c.WantReverse = true; c.Throttle = -axis; }
                }
            }
            else
            {
                if (axis < 0f) c.Throttle = -axis;
                else if (axis > 0f)
                {
                    if (forwardSpeed < -DirectionChangeSpeed) c.Brake = axis;
                    else { c.WantReverse = false; c.Throttle = axis; }
                }
            }
            return c;
        }

        /// <summary>Manual gearbox: W is always throttle and S always brake; the gear (incl. R) is chosen by the driver.</summary>
        public static DriveCommand MapManual(float axis, bool inReverse)
        {
            axis = axis < -1f ? -1f : axis > 1f ? 1f : axis;
            return new DriveCommand { Throttle = axis > 0f ? axis : 0f, Brake = axis < 0f ? -axis : 0f, WantReverse = inReverse };
        }
    }

    /// <summary>Driver aids. Inputs are WheelCollider slip values (forward slip: + spinning, - locking).</summary>
    public static class DriveAssists
    {
        public const float AbsSlipThreshold = 0.35f;
        public const float TcsSlipThreshold = 0.3f;

        /// <summary>Brake multiplier for one wheel: releases the brake while the wheel is locking.</summary>
        public static float AbsFactor(bool enabled, float forwardSlip)
        {
            if (!enabled || forwardSlip > -AbsSlipThreshold) return 1f;
            float over = (-forwardSlip - AbsSlipThreshold) / 0.4f;
            return Math.Max(0.25f, 1f - over);
        }

        /// <summary>Torque multiplier for a driven wheel: cuts power while it spins.</summary>
        public static float TcsFactor(bool enabled, float forwardSlip)
        {
            if (!enabled || forwardSlip < TcsSlipThreshold) return 1f;
            float over = (forwardSlip - TcsSlipThreshold) / 0.5f;
            return Math.Max(0.15f, 1f - over);
        }

        /// <summary>
        /// Stability control: compares the actual yaw rate with what the steering asks for (simple bicycle model).
        /// Returns a signed brake request 0..1: positive = brake the left wheels, negative = brake the right wheels.
        /// </summary>
        public static float EscBrake(bool enabled, float yawRateRad, float speed, float steerAngleDeg, float wheelbase)
        {
            if (!enabled || speed < 8f || wheelbase <= 0.5f) return 0f;
            float target = speed / wheelbase * (float)Math.Tan(steerAngleDeg * Math.PI / 180.0);
            float excess = yawRateRad - target;          // + = turning right more than asked (Unity's yaw is clockwise)
            const float deadZone = 0.25f;
            if (Math.Abs(excess) < deadZone) return 0f;
            float amount = Math.Min(1f, (Math.Abs(excess) - deadZone) / 0.6f);
            return excess > 0f ? amount : -amount;
        }

        /// <summary>Steering angle limit that shrinks with speed so the car doesn't flip at motorway speeds.</summary>
        public static float SteerLimit(float maxAngle, float highSpeedFactor, float speed)
        {
            float t = Math.Min(1f, Math.Abs(speed) / 33.3f);   // 120 km/h
            return maxAngle * (1f + (highSpeedFactor - 1f) * t);
        }
    }

    public static class FuelModel
    {
        /// <summary>Tank / battery % used over a distance. Load 0..1 (throttle effort). Average driving ≈ the profile's rate.</summary>
        public static float PercentUsed(float meters, float load, float percentPerKm)
        {
            if (meters <= 0f) return 0f;
            load = load < 0f ? 0f : load > 1f ? 1f : load;
            return percentPerKm * (meters / 1000f) * (0.55f + 0.9f * load);
        }
    }
}
