using System;
using System.Collections.Generic;

namespace CarSales.Foundation
{
    /// <summary>Totals for a range of days.</summary>
    public class LedgerSummary
    {
        public int fromDay, toDay;
        public int income, expenses;              // operating (excludes loans), expenses positive
        public int financingIn, financingOut;     // loans taken / repaid
        public readonly int[] byCategory = new int[TxCategories.Count];   // signed

        public int Net => income - expenses;

        /// <summary>Non-zero categories, biggest movement first.</summary>
        public List<KeyValuePair<TxCategory, int>> Lines()
        {
            var list = new List<KeyValuePair<TxCategory, int>>();
            for (int i = 0; i < byCategory.Length; i++)
                if (byCategory[i] != 0) list.Add(new KeyValuePair<TxCategory, int>((TxCategory)i, byCategory[i]));
            list.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            return list;
        }
    }

    /// <summary>
    /// Append-only record of every money movement (spec rule R5). Cash = opening balance + sum of entries.
    /// Old entries are compacted into the opening balance; lifetime per-category totals are kept forever.
    /// </summary>
    public sealed class Ledger
    {
        LedgerState state;
        long balance;

        public Ledger(LedgerState saved = null) { Load(saved); }

        public LedgerState State => state;
        public int Balance => (int)balance;
        public IReadOnlyList<Transaction> Entries => state.entries;
        public int OpeningBalance => state.openingBalance;

        public void Load(LedgerState saved)
        {
            state = saved ?? new LedgerState();
            if (state.entries == null) state.entries = new List<Transaction>();
            state.entries.RemoveAll(e => e == null);
            state.lifetimeIn = Resize(state.lifetimeIn);
            state.lifetimeOut = Resize(state.lifetimeOut);
            balance = state.openingBalance;
            long maxId = 0;
            foreach (var e in state.entries) { balance += e.amount; if (e.id > maxId) maxId = e.id; }
            if (state.nextId <= maxId) state.nextId = maxId + 1;
        }

        static long[] Resize(long[] a)
        {
            if (a != null && a.Length >= TxCategories.Count) return a;
            var r = new long[TxCategories.Count];
            if (a != null) Array.Copy(a, r, a.Length);
            return r;
        }

        /// <summary>Starts a fresh ledger (new game).</summary>
        public void Reset(int openingBalance, int day)
        {
            Load(new LedgerState { openingBalance = openingBalance, openingDay = Math.Max(1, day) });
        }

        /// <summary>Records a money movement. Returns null for a zero amount.</summary>
        public Transaction Post(int amount, TxCategory category, int day, float hour, string memo = null, string refId = null)
        {
            if (amount == 0) return null;
            var t = new Transaction
            {
                id = state.nextId++,
                day = day,
                hour = hour,
                amount = amount,
                category = category,
                memo = memo ?? "",
                refId = refId ?? ""
            };
            state.entries.Add(t);
            balance += amount;
            int c = (int)category;
            if (c >= 0 && c < state.lifetimeIn.Length)
            {
                if (amount > 0) state.lifetimeIn[c] += amount;
                else state.lifetimeOut[c] += -amount;
            }
            EventBus.Publish(new TransactionPosted(t, Balance));
            return t;
        }

        /// <summary>
        /// Makes the ledger agree with the real cash value (e.g. after loading an old save).
        /// Returns the adjustment that was posted (0 if they already matched).
        /// </summary>
        public int Reconcile(int actualCash, int day, float hour)
        {
            int diff = actualCash - Balance;
            if (diff != 0) Post(diff, TxCategory.Adjustment, day, hour, "Balance correction");
            return diff;
        }

        public LedgerSummary Summarize(int fromDay, int toDay)
        {
            var s = new LedgerSummary { fromDay = fromDay, toDay = toDay };
            foreach (var e in state.entries)
            {
                if (e.day < fromDay || e.day > toDay) continue;
                int c = (int)e.category;
                if (c >= 0 && c < s.byCategory.Length) s.byCategory[c] += e.amount;
                if (TxCategories.IsFinancing(e.category))
                {
                    if (e.amount > 0) s.financingIn += e.amount; else s.financingOut += -e.amount;
                }
                else if (e.amount > 0) s.income += e.amount;
                else s.expenses += -e.amount;
            }
            return s;
        }

        /// <summary>Operating net (income - expenses, loans excluded) for each day from fromDay to toDay.</summary>
        public int[] DailyNet(int fromDay, int toDay)
        {
            if (toDay < fromDay) return new int[0];
            var result = new int[toDay - fromDay + 1];
            foreach (var e in state.entries)
            {
                if (e.day < fromDay || e.day > toDay || TxCategories.IsFinancing(e.category)) continue;
                result[e.day - fromDay] += e.amount;
            }
            return result;
        }

        /// <summary>Every kept entry that refers to <paramref name="refId"/> (e.g. one car), oldest first.</summary>
        public List<Transaction> ForRef(string refId)
        {
            var list = new List<Transaction>();
            if (string.IsNullOrEmpty(refId)) return list;
            foreach (var e in state.entries) if (e.refId == refId) list.Add(e);
            return list;
        }

        /// <summary>Signed sum of the entries for <paramref name="refId"/> (negative = it cost money overall).</summary>
        public int NetForRef(string refId)
        {
            int sum = 0;
            foreach (var e in ForRef(refId)) sum += e.amount;
            return sum;
        }

        /// <summary>Newest first.</summary>
        public List<Transaction> Recent(int count)
        {
            var list = new List<Transaction>(Math.Min(count, state.entries.Count));
            for (int i = state.entries.Count - 1; i >= 0 && list.Count < count; i--) list.Add(state.entries[i]);
            return list;
        }

        public long LifetimeIn(TxCategory c) => state.lifetimeIn[(int)c];
        public long LifetimeOut(TxCategory c) => state.lifetimeOut[(int)c];

        /// <summary>Folds entries older than <paramref name="keepDays"/> days into the opening balance.</summary>
        public int Compact(int currentDay, int keepDays)
        {
            int cutoff = currentDay - Math.Max(1, keepDays);
            int removed = 0;
            while (removed < state.entries.Count && state.entries[removed].day < cutoff)
            {
                state.openingBalance += state.entries[removed].amount;
                removed++;
            }
            if (removed > 0)
            {
                state.entries.RemoveRange(0, removed);
                state.openingDay = cutoff;
            }
            return removed;
        }
    }
}
