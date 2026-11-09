namespace CarSales.Foundation
{
    public enum Weekday { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

    /// <summary>
    /// Game calendar. Day 1 is a Monday. A week has 7 days, a quarter 13 weeks (91 days), a year 4 quarters.
    /// </summary>
    public static class GameCalendar
    {
        public const int DaysPerWeek = 7;
        public const int WeeksPerQuarter = 13;
        public const int DaysPerQuarter = DaysPerWeek * WeeksPerQuarter;
        public const int QuartersPerYear = 4;

        static readonly string[] ShortNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        static int Clamp(int day) => day < 1 ? 1 : day;

        public static Weekday WeekdayOf(int day) => (Weekday)((Clamp(day) - 1) % DaysPerWeek);
        public static string WeekdayShort(int day) => ShortNames[(int)WeekdayOf(day)];
        public static bool IsWeekend(int day) { var w = WeekdayOf(day); return w == Weekday.Saturday || w == Weekday.Sunday; }

        /// <summary>1-based week number.</summary>
        public static int WeekOf(int day) => (Clamp(day) - 1) / DaysPerWeek + 1;
        /// <summary>1-based quarter number since the start of the game (Q1, Q2, ... Q5 is year 2's first quarter).</summary>
        public static int QuarterOf(int day) => (Clamp(day) - 1) / DaysPerQuarter + 1;
        public static int YearOf(int day) => (QuarterOf(day) - 1) / QuartersPerYear + 1;
        /// <summary>1-4 within the year.</summary>
        public static int QuarterInYear(int day) => (QuarterOf(day) - 1) % QuartersPerYear + 1;

        public static bool IsLastDayOfWeek(int day) => Clamp(day) % DaysPerWeek == 0;
        public static bool IsLastDayOfQuarter(int day) => Clamp(day) % DaysPerQuarter == 0;

        /// <summary>"Mon, Week 1"</summary>
        public static string Describe(int day) => $"{WeekdayShort(day)}, Week {WeekOf(day)}";
    }
}
