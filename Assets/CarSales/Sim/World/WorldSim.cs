using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum Weather { Clear = 0, Cloudy = 1, Overcast = 2, Rain = 3, HeavyRain = 4, Fog = 5 }
    public enum WorldEventType { PoliceCheck = 0, Breakdown = 1, RoadWorks = 2, OpenDay = 3, TheftAttempt = 4 }

    [Serializable]
    public class WorldState
    {
        public int version = 1;
        public int weather;                 // Weather
        public int weatherHour = -1;        // last hour the weather was rolled (day*24+hour)
        public float fuelIndex = 1f;        // multiplies fuel prices
        public List<float> fuelHistory = new List<float>();
        public int roadWorksUntil = -1;     // day
        public int openDay = -1;            // day of your open day
        public List<string> eventLog = new List<string>();
        public Weather Weather => (Weather)weather;
    }

    /// <summary>World simulation (spec §6.13): weather, calendar, fuel prices, random events. Pure; tests in WorldTests.</summary>
    public static class WorldSim
    {
        // ------------------------------------------------------------------ WEATHER
        /// <summary>Hourly Markov transitions (rows: from, columns: to). Each row sums to 1.</summary>
        static readonly float[][] Transitions =
        {
            //            Clear  Cloudy Overc  Rain   Heavy  Fog
            new[] { 0.80f, 0.15f, 0.02f, 0.01f, 0.00f, 0.02f },   // Clear
            new[] { 0.15f, 0.65f, 0.13f, 0.05f, 0.00f, 0.02f },   // Cloudy
            new[] { 0.02f, 0.18f, 0.55f, 0.20f, 0.03f, 0.02f },   // Overcast
            new[] { 0.00f, 0.08f, 0.25f, 0.55f, 0.12f, 0.00f },   // Rain
            new[] { 0.00f, 0.02f, 0.15f, 0.43f, 0.40f, 0.00f },   // Heavy rain
            new[] { 0.15f, 0.25f, 0.10f, 0.00f, 0.00f, 0.50f },   // Fog
        };

        public static float TransitionChance(Weather from, Weather to) => Transitions[(int)from][(int)to];

        /// <summary>
        /// Next hour's weather. Winter (quarter 1/4) is wetter, summer (quarter 3) drier; fog clears after 11:00.
        /// </summary>
        public static Weather Next(Weather current, int quarterInYear, int hourOfDay, Random rng)
        {
            var row = (float[])Transitions[(int)current].Clone();
            float wet = quarterInYear == 1 || quarterInYear == 4 ? 1.3f : quarterInYear == 3 ? 0.6f : 1f;
            row[(int)Weather.Rain] *= wet;
            row[(int)Weather.HeavyRain] *= wet;
            if (hourOfDay >= 11 && hourOfDay < 20) row[(int)Weather.Fog] *= 0.1f;
            float total = 0f;
            foreach (var v in row) total += v;
            double r = rng.NextDouble() * total;
            for (int i = 0; i < row.Length; i++) { r -= row[i]; if (r <= 0) return (Weather)i; }
            return current;
        }

        public static bool IsWet(Weather w) => w == Weather.Rain || w == Weather.HeavyRain;

        /// <summary>Tyre grip: 100% dry, 85% rain, 75% heavy rain.</summary>
        public static float Grip(Weather w) => w == Weather.HeavyRain ? 0.75f : w == Weather.Rain ? 0.85f : 1f;

        /// <summary>Customers who turn up: -30% in rain, -45% in heavy rain, -10% in fog.</summary>
        public static float Footfall(Weather w) => w == Weather.HeavyRain ? 0.55f : w == Weather.Rain ? 0.7f : w == Weather.Fog ? 0.9f : 1f;

        /// <summary>Car wash demand: low while it rains, high the day after.</summary>
        public static float WashDemand(Weather w) => IsWet(w) ? 0.5f : 1f;

        public static float SunLight(Weather w) => w == Weather.Clear ? 1f : w == Weather.Cloudy ? 0.8f : w == Weather.Overcast ? 0.55f : w == Weather.Fog ? 0.5f : 0.4f;

        public static float FogDensity(Weather w) => w == Weather.Fog ? 0.025f : w == Weather.HeavyRain ? 0.008f : w == Weather.Rain ? 0.004f : 0f;

        public static string Name(Weather w) => w == Weather.HeavyRain ? "Heavy rain" : w.ToString();

        // ------------------------------------------------------------------ CALENDAR
        /// <summary>Bank holidays: day-of-year (1-364 in a 4×91-day year) of New Year, Easter-ish, May, summer, Christmas, Boxing Day.</summary>
        static readonly int[] BankHolidays = { 1, 95, 122, 238, 359, 360 };

        public static int DayOfYear(int day) => (day - 1) % (Foundation.GameCalendar.DaysPerQuarter * Foundation.GameCalendar.QuartersPerYear) + 1;
        public static bool IsBankHoliday(int day) => Array.IndexOf(BankHolidays, DayOfYear(day)) >= 0;

        /// <summary>Footfall by day: Saturdays and bank holidays are busy, Sunday quieter.</summary>
        public static float DayFootfall(int day)
        {
            if (IsBankHoliday(day)) return 1.4f;
            var w = Foundation.GameCalendar.WeekdayOf(day);
            return w == Foundation.Weekday.Saturday ? 1.3f : w == Foundation.Weekday.Sunday ? 0.8f : 1f;
        }

        /// <summary>Sunday trading: shops open an hour later and close two hours earlier.</summary>
        public static (float open, float close) Hours(int day, float open, float close) =>
            Foundation.GameCalendar.WeekdayOf(day) == Foundation.Weekday.Sunday ? (open + 1f, close - 2f) : (open, close);

        // ------------------------------------------------------------------ FUEL
        /// <summary>Fuel price index: daily random walk ±2%, pulled back towards 1, between 0.75 and 1.4.</summary>
        public static float StepFuel(float index, Random rng)
        {
            double g = Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Sin(2.0 * Math.PI * rng.NextDouble());
            float v = index + (1f - index) * 0.04f + (float)g * 0.02f;
            return Math.Max(0.75f, Math.Min(1.4f, v));
        }

        /// <summary>Expensive fuel makes electric and hybrid cars more wanted (and big petrol cars less).</summary>
        public static float EconomyDemand(float fuelIndex, bool economical) => economical ? 1f + (fuelIndex - 1f) * 0.3f : 1f - (fuelIndex - 1f) * 0.15f;

        // ------------------------------------------------------------------ EVENTS
        /// <summary>Chance per day of each random event.</summary>
        public static float EventChance(WorldEventType t) =>
            t == WorldEventType.PoliceCheck ? 0.12f : t == WorldEventType.Breakdown ? 0.15f : t == WorldEventType.RoadWorks ? 0.05f :
            t == WorldEventType.OpenDay ? 0f : 0.04f;

        public const int PoliceFine = 100, OpenDayCost = 600;

        /// <summary>A police check fines you if the car you're driving has no valid MOT.</summary>
        public static int PoliceFineFor(bool motValid) => motValid ? 0 : PoliceFine;

        /// <summary>Open day: a busy day (+60% customers) for a fee, booked for a later day.</summary>
        public const float OpenDayFootfall = 1.6f;

        /// <summary>Road works slow customers getting to you (-20%).</summary>
        public const float RoadWorksFootfall = 0.8f;
    }

    // ------------------------------------------------------------------ TRAFFIC
    public enum LightState { Green, Amber, Red }

    /// <summary>Traffic rules for the lane graph (spec §6.13). Pure; tests in WorldTests.</summary>
    public static class TrafficRules
    {
        public const float SafeGap = 7f;           // metres to the car ahead at walking pace
        public const float TimeGap = 1.4f;         // seconds of headway at speed
        public const float Accel = 2.5f, Brake = 6f;
        public const float CycleSeconds = 30f, AmberSeconds = 3f;

        /// <summary>Gap-keeping speed: free speed limit, or slow down to keep a safe distance; stop before a red light.</summary>
        public static float TargetSpeed(float limit, float gapToCarAhead, float speedAhead, float distanceToStopLine, bool mustStop)
        {
            float target = limit;
            if (gapToCarAhead >= 0f)
            {
                float desired = SafeGap + speedAhead * TimeGap;
                if (gapToCarAhead < desired) target = Math.Min(target, Math.Max(0f, speedAhead * (gapToCarAhead / desired)));
                if (gapToCarAhead < SafeGap * 0.6f) target = 0f;
            }
            if (mustStop && distanceToStopLine >= 0f)
                target = Math.Min(target, distanceToStopLine < 2f ? 0f : (float)Math.Sqrt(2f * Brake * 0.5f * (distanceToStopLine - 2f)));
            return Math.Max(0f, target);
        }

        public static float Approach(float speed, float target, float dt) =>
            speed < target ? Math.Min(target, speed + Accel * dt) : Math.Max(target, speed - Brake * dt);

        /// <summary>Two-phase junction: phase A (east-west) green for half the cycle, then phase B (north-south).</summary>
        public static LightState Light(float time, bool phaseA)
        {
            float t = time % CycleSeconds;
            float half = CycleSeconds / 2f;
            float local = phaseA ? t : (t + half) % CycleSeconds;
            if (local < half - AmberSeconds) return LightState.Green;
            if (local < half) return LightState.Amber;
            return LightState.Red;
        }

        /// <summary>Cars on the road by hour: rush hours busiest, night nearly empty (0-1 of the maximum).</summary>
        public static float Density(float hour)
        {
            float h = hour % 24f;
            if (h < 5f) return 0.08f;
            if (h < 7f) return 0.3f;
            if (h < 9.5f) return 1f;
            if (h < 16f) return 0.6f;
            if (h < 19f) return 1f;
            if (h < 22f) return 0.45f;
            return 0.15f;
        }

        public const int MaxTrafficCars = 14, MaxPedestrians = 18;
    }
}
