using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum AuctionType { Dealer = 0, Fleet = 1, Salvage = 2, Repossession = 3, OnlineTimed = 4 }
    public enum LotStatus { Upcoming = 0, Live = 1, Sold = 2, Provisional = 3, Unsold = 4, Withdrawn = 5 }
    public enum BidderArchetype { PrivateBuyer, SmallTrader, FranchiseDealer, Exporter, Breaker, Mechanic, DealerGroup }

    public sealed class VenueDef
    {
        public string Id, Name;
        public bool Physical;
        public bool GuaranteesTitle;     // dealer/fleet sales clear outstanding finance; salvage ones don't
        public float ReportReliability;  // chance each detectable fault is written in the condition report
    }

    /// <summary>Written by the auction house. Grade 5 = excellent. Can leave things out (see reliability).</summary>
    [Serializable]
    public class ConditionReport
    {
        public int grade = 3;
        public List<string> damage = new List<string>();
        public List<string> faults = new List<string>();
        public int claimedMileage;
        public bool mileageVerified = true;
        public int keys = 2;
        public bool starts = true, drives = true;
        public bool v5cPresent = true;
        public string notes = "";
    }

    [Serializable]
    public class AuctionLot
    {
        public string id;
        public int lotNo;
        public string eventId;
        public string carId;          // CarInstance.instanceId (cars are kept by the runtime)
        public int startPrice;
        public int reserve;           // hidden
        public int estimate;          // the house's guide price
        public ConditionReport report = new ConditionReport();
        public int status;            // LotStatus
        public int currentBid;
        public string leader = "";    // bidder id, PlayerId, or "" (no bids)
        public int playerMax;         // proxy bid (0 = none)
        public bool watched;
        public float endHour;         // online lots: hour of the event day the lot closes (soft close moves it)
        public int hammer;
        public string buyer = "";
        public LotStatus Status { get => (LotStatus)status; set => status = (int)value; }
    }

    [Serializable]
    public class AuctionEvent
    {
        public string id;
        public string venueId;
        public int type;              // AuctionType
        public int day;
        public float startHour;
        public bool online;
        public bool finished;
        public int nextLot;           // physical sales: index of the lot on the rostrum
        public List<string> lots = new List<string>();
        public AuctionType Type => (AuctionType)type;
    }

    [Serializable]
    public class AuctionBidder
    {
        public string id, name;
        public int archetype;
        public int budget;
        public float margin, optimism, risk;
        public int maxLotsPerDay = 2;
        public int wonDay, wonToday;
        public BidderArchetype Archetype => (BidderArchetype)archetype;
    }

    [Serializable]
    public class BuyerAccount
    {
        public string venueId;
        public int buyerNo;
        public int deposit;
        public bool registered;
    }

    [Serializable]
    public class AuctionInvoice
    {
        public string id, lotId, carId, venueId;
        public int lotNo;
        public int hammer, premium, internetFee, vat, lateFees, storageFees;
        public int issuedDay, dueDay, freeStorageUntil;
        public bool paid, collected, cancelled, titleGuaranteed;
        public int Fees => premium + internetFee;
        public int Total => hammer + premium + internetFee + vat + lateFees;
    }

    /// <summary>Fees, increments, reports, AI bidders, proxy bidding, invoices (spec §6.9, §8.3). Pure; tests in AuctionTests.</summary>
    public static class AuctionRules
    {
        public const string PlayerId = "player";
        public const int Deposit = 500;
        public const int InternetFee = 20;
        public const float VatRate = 0.2f;
        public const int PaymentDays = 2, CancelAfterDays = 5;
        public const int LateFeePerDay = 50;
        public const int FreeStorageDays = 3, StoragePerDay = 15;
        public const float CallSeconds = 4f;          // live: going once / twice / sold
        public const float SoftCloseHours = 2f / 60f; // online: a late bid adds 2 minutes
        public const int Transport = 150;

        public static readonly VenueDef[] Venues =
        {
            new VenueDef { Id = "hall", Name = "Auction Center", Physical = true, GuaranteesTitle = true, ReportReliability = 0.85f },
            new VenueDef { Id = "online", Name = "TradeNet Online", Physical = false, GuaranteesTitle = true, ReportReliability = 0.75f },
            new VenueDef { Id = "salvage", Name = "SalvageBid", Physical = false, GuaranteesTitle = false, ReportReliability = 0.5f },
        };

        public static VenueDef Venue(string id) => Array.Find(Venues, v => v.Id == id);

        public static string TypeName(AuctionType t) =>
            t == AuctionType.Dealer ? "Dealer sale" : t == AuctionType.Fleet ? "Fleet & lease sale" : t == AuctionType.Salvage ? "Salvage sale" :
            t == AuctionType.Repossession ? "Repossession sale" : "Online timed sale";

        // ------------------------------------------------------------------ MONEY
        public static int Increment(int price) => price < 1000 ? 50 : price < 5000 ? 100 : price < 10000 ? 250 : 500;

        public static int NextBid(AuctionLot lot) => string.IsNullOrEmpty(lot.leader) ? lot.startPrice : lot.currentBid + Increment(lot.currentBid);

        public static int BuyerPremium(int hammer) =>
            hammer < 1000 ? 150 : hammer < 3000 ? 250 : hammer < 5000 ? 350 : hammer < 10000 ? 450 : hammer < 20000 ? 600 : 750 + (int)Math.Round((hammer - 20000) * 0.01);

        public static int Vat(int fees) => (int)Math.Round(fees * VatRate);

        public static AuctionInvoice Invoice(AuctionLot lot, VenueDef venue, int day, string id)
        {
            var inv = new AuctionInvoice
            {
                id = id, lotId = lot.id, carId = lot.carId, venueId = venue.Id, lotNo = lot.lotNo,
                hammer = lot.hammer, premium = BuyerPremium(lot.hammer), internetFee = venue.Physical ? 0 : InternetFee,
                issuedDay = day, dueDay = day + PaymentDays, freeStorageUntil = day + FreeStorageDays, titleGuaranteed = venue.GuaranteesTitle,
            };
            inv.vat = Vat(inv.Fees);
            return inv;
        }

        /// <summary>Late fees for an unpaid invoice on <paramref name="day"/>; true when the sale is cancelled (deposit lost).</summary>
        public static bool ApplyLateFees(AuctionInvoice inv, int day)
        {
            if (inv.paid || inv.cancelled) return false;
            int late = Math.Max(0, day - inv.dueDay);
            inv.lateFees = late * LateFeePerDay;
            if (day - inv.issuedDay >= CancelAfterDays) { inv.cancelled = true; return true; }
            return false;
        }

        /// <summary>Storage owed for one more night at the venue (0 during the free days).</summary>
        public static int StorageForNight(AuctionInvoice inv, int day) =>
            inv.paid && !inv.collected && !inv.cancelled && day > inv.freeStorageUntil ? StoragePerDay : 0;

        // ------------------------------------------------------------------ REPORTS
        public static int Grade(float condition) => condition >= 85f ? 5 : condition >= 70f ? 4 : condition >= 50f ? 3 : condition >= 30f ? 2 : 1;

        /// <summary>
        /// The house's report: each fault is listed with chance = reliability; body damage below 60% is noted.
        /// <paramref name="faultNames"/>, <paramref name="damagedPanels"/>: the truth. Keys may be over-reported at salvage sales.
        /// </summary>
        public static ConditionReport Report(float condition, IList<string> faultNames, IList<string> damagedPanels, int mileage, bool clocked,
                                             int keys, bool starts, bool v5cPresent, VenueDef venue, Random rng)
        {
            var r = new ConditionReport
            {
                grade = Grade(condition), claimedMileage = mileage, mileageVerified = !clocked && rng.NextDouble() > 0.1,
                starts = starts, drives = starts && condition > 20f, v5cPresent = v5cPresent, keys = keys,
            };
            foreach (var f in faultNames) if (rng.NextDouble() < venue.ReportReliability) r.faults.Add(f);
            foreach (var d in damagedPanels) if (rng.NextDouble() < Math.Min(1f, venue.ReportReliability + 0.1f)) r.damage.Add(d);
            if (!venue.GuaranteesTitle && keys > 0 && rng.NextDouble() < 0.15) r.keys = keys + 1;   // "keys: 1" that isn't there
            if (!starts) r.notes = "Non-runner. Sold as seen.";
            else if (!r.mileageVerified) r.notes = "Mileage unverified.";
            return r;
        }

        // ------------------------------------------------------------------ AI BIDDERS (§8.3)
        static readonly string[] FirstNames = { "Dan", "Priya", "Mike", "Joe", "Kenji", "Rosa", "Vlad", "Amir", "Sophie", "Tom", "Leila", "Gareth", "Ines", "Olu" };
        static readonly string[] Firms = { "Motors", "Trade Cars", "Autos", "Car Sales", "Exports", "Breakers", "Garage", "Group" };

        public static List<AuctionBidder> GenerateBidders(int count, Random rng)
        {
            var list = new List<AuctionBidder>();
            var types = (BidderArchetype[])Enum.GetValues(typeof(BidderArchetype));
            for (int i = 0; i < count; i++)
            {
                var a = types[rng.Next(types.Length)];
                string first = FirstNames[rng.Next(FirstNames.Length)];
                var b = new AuctionBidder
                {
                    id = "ai" + i, archetype = (int)a,
                    name = a == BidderArchetype.PrivateBuyer ? first : $"{first}'s {Firms[rng.Next(Firms.Length)]}",
                    budget = a == BidderArchetype.DealerGroup ? 120000 : a == BidderArchetype.PrivateBuyer ? 15000 : 50000,
                    margin = a == BidderArchetype.PrivateBuyer ? 0.02f : a == BidderArchetype.DealerGroup ? 0.08f : a == BidderArchetype.Breaker ? 0.3f : 0.15f,
                    optimism = a == BidderArchetype.PrivateBuyer ? 1.08f : a == BidderArchetype.Mechanic ? 1.02f : 0.97f + (float)rng.NextDouble() * 0.06f,
                    risk = a == BidderArchetype.PrivateBuyer ? 0.2f : a == BidderArchetype.Breaker ? 0.1f : 0.5f + (float)rng.NextDouble() * 0.4f,
                    maxLotsPerDay = a == BidderArchetype.DealerGroup ? 5 : a == BidderArchetype.PrivateBuyer ? 1 : 2,
                };
                list.Add(b);
            }
            return list;
        }

        /// <summary>What a lot looks like to a bidder (from the report, never the truth).</summary>
        public struct LotView
        {
            public int retailIfAsReported;  // value of the car if the report is right
            public int reportedRepairs;     // repair estimate for the faults/damage in the report
            public int grade;
            public bool starts, mileageVerified, japaneseOr4x4, salvage;
            public int mileage;
            public int faultCount, damageCount;
        }

        /// <summary>
        /// §8.3: maxBid = (resale × optimism × (1 - margin) - repairs × (1 + risk × uncertainty) - fees) × noise(0.92..1.08).
        /// Archetypes: franchise only buys grade 4+, exporters like Japanese/4x4 and don't mind miles, breakers pay a
        /// salvage floor, mechanics shrug off mechanical faults but avoid bodywork, private buyers overpay a little.
        /// </summary>
        public static int MaxBid(AuctionBidder b, LotView v, Random rng)
        {
            var a = b.Archetype;
            if (a == BidderArchetype.FranchiseDealer && v.grade < 4) return 0;
            if (a == BidderArchetype.PrivateBuyer && (!v.starts || v.salvage)) return 0;
            float resale = v.retailIfAsReported * b.optimism;
            if (a == BidderArchetype.Exporter) resale *= v.japaneseOr4x4 ? 1.12f : 0.9f;
            if (!v.mileageVerified) resale *= 0.9f;
            if (!v.starts) resale *= 0.75f;
            if (v.mileage > 150000 && a != BidderArchetype.Exporter) resale *= 0.92f;
            float uncertainty = v.salvage ? 0.6f : v.grade <= 2 ? 0.4f : 0.15f;
            float repairs = v.reportedRepairs;
            if (a == BidderArchetype.Mechanic) repairs *= v.damageCount > 0 ? 1.4f : 0.6f;
            repairs *= 1f + b.risk * uncertainty;
            float fees = BuyerPremium((int)(resale * 0.6f)) + Transport;
            float max = (resale * (1f - b.margin) - repairs - fees) * (0.92f + (float)rng.NextDouble() * 0.16f);
            if (a == BidderArchetype.Breaker) max = Math.Max(max, v.retailIfAsReported * 0.22f);
            max = Math.Min(max, b.budget);
            return Math.Max(0, (int)(max / 50f) * 50);
        }

        // ------------------------------------------------------------------ BIDDING
        /// <summary>
        /// Proxy (online / absentee) bidding: everyone bids up to their maximum. The highest maximum wins at the
        /// second-highest maximum plus one increment (never more than its own maximum). Ties go to the earlier bidder
        /// (lower index). Returns (price, winnerIndex); winnerIndex -1 = no bid reached the start price.
        /// </summary>
        public static (int price, int winner) ResolveProxy(IList<int> maxima, int startPrice)
        {
            int best = -1, second = -1;
            for (int i = 0; i < maxima.Count; i++)
            {
                if (maxima[i] < startPrice) continue;
                if (best < 0 || maxima[i] > maxima[best]) { second = best; best = i; }
                else if (second < 0 || maxima[i] > maxima[second]) second = i;
            }
            if (best < 0) return (0, -1);
            if (second < 0) return (startPrice, best);
            int price = Math.Min(maxima[best], maxima[second] + Increment(maxima[second]));
            if (maxima[best] == maxima[second]) price = maxima[best];
            return (Math.Max(startPrice, price), best);
        }

        /// <summary>Soft close: a bid that changes the leader in the last 2 minutes moves the end 2 minutes later.</summary>
        public static float SoftClose(float endHour, float bidHour) =>
            bidHour > endHour - SoftCloseHours && bidHour <= endHour ? bidHour + SoftCloseHours : endHour;

        /// <summary>Reserve not met: chance the seller accepts the provisional bid by the end of the day.</summary>
        public static float SellerAcceptChance(int bid, int reserve)
        {
            if (reserve <= 0 || bid >= reserve) return 1f;
            float r = bid / (float)reserve;
            return r >= 0.95f ? 0.7f : r >= 0.9f ? 0.4f : r >= 0.8f ? 0.1f : 0f;
        }

        /// <summary>
        /// One AI step of a live lot: returns the index of a bidder (not the leader) willing to pay the next bid, or -1.
        /// <paramref name="eagerness"/> (0-1): chance someone reacts at this moment.
        /// </summary>
        public static int LiveAiBid(AuctionLot lot, IList<int> maxima, IList<string> ids, float eagerness, Random rng)
        {
            int next = NextBid(lot);
            var willing = new List<int>();
            for (int i = 0; i < maxima.Count; i++) if (maxima[i] >= next && ids[i] != lot.leader) willing.Add(i);
            if (willing.Count == 0 || rng.NextDouble() >= eagerness) return -1;
            return willing[rng.Next(willing.Count)];
        }

        /// <summary>Hammer outcome: Sold, Provisional (reserve not met) or Unsold (no bids).</summary>
        public static LotStatus Hammer(AuctionLot lot)
        {
            if (string.IsNullOrEmpty(lot.leader)) return LotStatus.Unsold;
            lot.hammer = lot.currentBid;
            lot.buyer = lot.leader;
            return lot.currentBid >= lot.reserve ? LotStatus.Sold : LotStatus.Provisional;
        }
    }
}
