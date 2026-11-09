using System;

namespace CarSales.Sim
{
    /// <summary>
    /// Engine + automatic gearbox (or electric motor). Each physics step it gets the throttle and how fast the
    /// driven wheels turn, and returns the total torque for the driven wheels. No Unity code: fully unit-tested.
    /// </summary>
    public sealed class Powertrain
    {
        public VehicleProfile Profile { get; }

        /// <summary>-1 = reverse, 0 = neutral (while shifting), 1..n = forward gear. Electric cars use 1 / -1.</summary>
        public int Gear { get; private set; } = 1;
        public float Rpm { get; private set; }
        /// <summary>0..1: how hard the engine works (for fuel use and engine sound).</summary>
        public float Load { get; private set; }
        public bool IsShifting => shiftTimer > 0f;
        public bool AtLimiter { get; private set; }

        /// <summary>0..1 multiplier from engine health (a worn engine makes less power).</summary>
        public float PowerFactor { get; set; } = 1f;
        /// <summary>False when the tank/battery is empty or the engine is broken.</summary>
        public bool EngineOn { get; set; } = true;
        /// <summary>Manual gearbox: the driver shifts with <see cref="ShiftUp"/> / <see cref="ShiftDown"/> (the clutch is automatic).</summary>
        public bool Manual { get; set; }

        float shiftTimer, lastWheelRpm;
        int pendingGear;

        public Powertrain(VehicleProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Rpm = profile.IsElectric ? 0f : profile.engine.idleRpm;
        }

        public string GearLabel
        {
            get
            {
                if (Gear < 0) return "R";
                if (Profile.IsElectric) return "D";
                if (Gear == 0 || IsShifting) return "N";
                return Gear.ToString();
            }
        }

        /// <summary>
        /// Advances the powertrain. <paramref name="drivenWheelRpm"/> is signed (positive = rolling forward);
        /// <paramref name="forwardSpeed"/> in m/s, signed. Returns the total torque (Nm) for the driven wheels:
        /// positive pushes forward, negative pushes backward (reverse gear or engine braking).
        /// </summary>
        public float Step(float dt, float throttle, bool wantReverse, float drivenWheelRpm, float forwardSpeed)
        {
            throttle = throttle < 0f ? 0f : throttle > 1f ? 1f : throttle;
            float speed = Math.Abs(forwardSpeed);

            // Automatic: direction changes only happen (almost) at a standstill. Manual: the driver selects R.
            if (!Manual && speed < 1.5f)
            {
                if (wantReverse && Gear >= 0) { Gear = -1; shiftTimer = 0f; }
                else if (!wantReverse && Gear < 0) { Gear = 1; shiftTimer = 0f; }
                else if (!wantReverse && Gear > 1 && speed < 0.5f) { Gear = 1; shiftTimer = 0f; }
            }

            return Profile.IsElectric
                ? StepElectric(throttle, drivenWheelRpm, forwardSpeed)
                : StepCombustion(dt, throttle, drivenWheelRpm, forwardSpeed);
        }

        float StepElectric(float throttle, float wheelRpm, float forwardSpeed)
        {
            var e = Profile.electric;
            float motorRpm = Math.Abs(wheelRpm) * e.ratio;
            Rpm = motorRpm;
            AtLimiter = motorRpm >= e.maxRpm;
            float torque = 0f;
            if (EngineOn && !AtLimiter && throttle > 0f)
            {
                float omega = motorRpm * 2f * (float)Math.PI / 60f;
                float available = e.maxTorqueNm;
                if (omega > 1f) available = Math.Min(available, e.maxPowerKw * 1000f / omega);
                torque = available * throttle * PowerFactor;
                Load = available > 0f ? torque / e.maxTorqueNm : 0f;
            }
            else Load = 0f;
            float sign = Gear < 0 ? -1f : 1f;
            float wheelTorque = torque * e.ratio * e.efficiency * sign;
            // Regenerative braking when lifting off (one-pedal feel).
            if (throttle < 0.05f && Gear > 0 && forwardSpeed > 2f) wheelTorque -= e.maxTorqueNm * e.regenFraction * e.ratio * 0.3f;
            return wheelTorque;
        }

        float StepCombustion(float dt, float throttle, float wheelRpm, float forwardSpeed)
        {
            var eng = Profile.engine;
            var box = Profile.gearbox;
            int gears = box.GearCount;
            if (gears == 0) return 0f;
            if (Gear > gears) Gear = gears;

            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
                if (shiftTimer <= 0f) { Gear = pendingGear; shiftTimer = 0f; }
            }

            float ratio = Gear < 0 ? box.reverseRatio : Gear > 0 ? box.ratios[Gear - 1] : 0f;
            float wheelBasedRpm = Math.Abs(wheelRpm) * ratio * box.finalDrive;

            // Clutch / torque converter slip lets the engine rev above wheel speed when pulling away.
            float launchRpm = eng.idleRpm + throttle * (Math.Min(eng.redlineRpm * 0.55f, eng.peakTorqueRpm * 1.1f) - eng.idleRpm);
            float rpm = IsShifting ? Math.Max(eng.idleRpm, Rpm * 0.97f) : Math.Max(wheelBasedRpm, Math.Max(launchRpm, eng.idleRpm));
            AtLimiter = rpm >= eng.redlineRpm;
            if (AtLimiter) rpm = eng.redlineRpm;
            Rpm = rpm;

            float engineTorque = 0f;
            if (EngineOn && !IsShifting && Gear != 0 && !AtLimiter)
                engineTorque = eng.TorqueAt(rpm) * throttle * Clamp01(PowerFactor);
            Load = EngineOn && eng.peakTorqueNm > 0f ? Clamp01(engineTorque / eng.peakTorqueNm) : 0f;

            float sign = Gear < 0 ? -1f : 1f;
            float wheelTorque = engineTorque * ratio * box.finalDrive * box.efficiency * sign;

            // Engine braking when lifting off in a forward gear.
            if (EngineOn && throttle < 0.05f && Gear > 0 && !IsShifting && forwardSpeed > 2f)
                wheelTorque -= eng.engineBrakeNm * (rpm / eng.redlineRpm) * ratio * box.finalDrive * box.efficiency;

            if (!Manual && Gear > 0 && !IsShifting) DecideShift(throttle, wheelBasedRpm, Math.Abs(wheelRpm));
            lastWheelRpm = Math.Abs(wheelRpm);
            return wheelTorque;
        }

        // ------------------------------------------------------------------ MANUAL
        /// <summary>Manual gearbox: one gear up (from R only at a standstill, into 1st). Returns false if not possible.</summary>
        public bool ShiftUp(float forwardSpeed)
        {
            if (IsShifting) return false;
            if (Gear < 0)
            {
                if (Math.Abs(forwardSpeed) > 1.5f) return false;
                Gear = 1;
                return true;
            }
            if (Profile.IsElectric || Gear >= Profile.gearbox.GearCount) return false;
            StartShift(Gear + 1);
            return true;
        }

        /// <summary>
        /// Manual gearbox: one gear down; from 1st into R at a standstill. Refuses a downshift that would over-rev
        /// the engine (money-shift protection). Returns false if not possible.
        /// </summary>
        public bool ShiftDown(float forwardSpeed)
        {
            if (IsShifting) return false;
            if (Gear <= 1 || Profile.IsElectric)
            {
                if (Gear < 0 || Math.Abs(forwardSpeed) > 1.5f) return false;
                Gear = -1;
                return true;
            }
            var box = Profile.gearbox;
            float rpmAfter = lastWheelRpm * box.ratios[Gear - 2] * box.finalDrive;
            if (rpmAfter > Profile.engine.redlineRpm * 0.98f) return false;
            StartShift(Gear - 1);
            return true;
        }

        // ------------------------------------------------------------------ AUTOMATIC
        void DecideShift(float throttle, float rpmNow, float absWheelRpm)
        {
            var eng = Profile.engine;
            var box = Profile.gearbox;
            float up = Lerp(eng.idleRpm + (box.upshiftRpm - eng.idleRpm) * 0.45f, box.upshiftRpm, throttle);
            if (rpmNow > up && Gear < box.GearCount) { StartShift(Gear + 1); return; }
            if (Gear <= 1) return;

            float rpmIfDown = absWheelRpm * box.ratios[Gear - 2] * box.finalDrive;
            float down = Lerp(eng.idleRpm * 1.4f, box.downshiftRpm, throttle);
            bool lugging = rpmNow < down && rpmIfDown < up * 0.9f;                       // never shift into a gear that would upshift again
            bool kickDown = throttle > 0.9f && rpmNow < eng.peakTorqueRpm && rpmIfDown < box.upshiftRpm * 0.85f;
            if (lugging || kickDown) StartShift(Gear - 1);
        }

        void StartShift(int gear)
        {
            pendingGear = gear;
            shiftTimer = Math.Max(0.01f, Profile.gearbox.shiftTime);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
}
