namespace CarSales.Foundation
{
    // Events published through EventBus. Keep them small and immutable.
    // New events are added as systems move onto the V5 architecture (see docs/V5_MASTER_SPEC.md §6.1).

    /// <summary>Cash changed. Delta is signed (+ income, - expense); 0 when the value was set (new game / load).</summary>
    public readonly struct MoneyChanged
    {
        public readonly int Money, Delta;
        public MoneyChanged(int money, int delta) { Money = money; Delta = delta; }
    }

    public readonly struct TransactionPosted
    {
        public readonly Transaction Transaction;
        public readonly int BalanceAfter;
        public TransactionPosted(Transaction t, int balanceAfter) { Transaction = t; BalanceAfter = balanceAfter; }
    }

    /// <summary>The player slept: <see cref="Day"/> is the day that just ended (accounting already done).</summary>
    public readonly struct DayEnded
    {
        public readonly int Day;
        public readonly bool PassedOut;
        public DayEnded(int day, bool passedOut) { Day = day; PassedOut = passedOut; }
    }

    public readonly struct DayStarted
    {
        public readonly int Day;
        public DayStarted(int day) { Day = day; }
    }

    /// <summary>Fired after the last day of a week ended (wages etc. hook in here later).</summary>
    public readonly struct WeekEnded
    {
        public readonly int Week;
        public WeekEnded(int week) { Week = week; }
    }

    /// <summary>Fired after the last day of a quarter ended (VAT returns etc. hook in here later).</summary>
    public readonly struct QuarterEnded
    {
        public readonly int Quarter;
        public QuarterEnded(int quarter) { Quarter = quarter; }
    }

    public readonly struct GameSaved
    {
        public readonly int Slot;
        public GameSaved(int slot) { Slot = slot; }
    }

    public readonly struct GameLoaded
    {
        public readonly int Slot;
        public readonly bool FromBackup;
        public GameLoaded(int slot, bool fromBackup) { Slot = slot; FromBackup = fromBackup; }
    }
}
