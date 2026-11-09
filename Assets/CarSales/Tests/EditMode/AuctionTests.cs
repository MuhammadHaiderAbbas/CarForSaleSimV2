using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class AuctionTests
    {
        static AuctionRules.LotView View(int retail, int repairs = 0, int grade = 4, bool starts = true) =>
            new AuctionRules.LotView { retailIfAsReported = retail, reportedRepairs = repairs, grade = grade, starts = starts, mileageVerified = true, mileage = 80000 };

        [Test]
        public void Increments_FollowTheTable()
        {
            Assert.AreEqual(50, AuctionRules.Increment(999));
            Assert.AreEqual(100, AuctionRules.Increment(1000));
            Assert.AreEqual(250, AuctionRules.Increment(5000));
            Assert.AreEqual(500, AuctionRules.Increment(10000));
        }

        [Test]
        public void Invoice_AddsPremium_InternetFee_AndVatOnFeesOnly()
        {
            var lot = new AuctionLot { id = "l", carId = "c", hammer = 4000, lotNo = 3 };
            var inv = AuctionRules.Invoice(lot, AuctionRules.Venue("online"), 10, "i");
            Assert.AreEqual(350, inv.premium);
            Assert.AreEqual(20, inv.internetFee);
            Assert.AreEqual(74, inv.vat);   // 20% of 370
            Assert.AreEqual(4000 + 370 + 74, inv.Total);
            Assert.AreEqual(12, inv.dueDay);
            Assert.AreEqual(0, AuctionRules.Invoice(lot, AuctionRules.Venue("hall"), 10, "j").internetFee);
        }

        [Test]
        public void LatePayment_AddsFees_ThenCancels()
        {
            var inv = new AuctionInvoice { issuedDay = 10, dueDay = 12 };
            Assert.IsFalse(AuctionRules.ApplyLateFees(inv, 12));
            Assert.AreEqual(0, inv.lateFees);
            Assert.IsFalse(AuctionRules.ApplyLateFees(inv, 14));
            Assert.AreEqual(100, inv.lateFees);
            Assert.IsTrue(AuctionRules.ApplyLateFees(inv, 15));
            Assert.IsTrue(inv.cancelled);
        }

        [Test]
        public void Storage_IsFreeForThreeDays()
        {
            var inv = new AuctionInvoice { paid = true, freeStorageUntil = 13 };
            Assert.AreEqual(0, AuctionRules.StorageForNight(inv, 13));
            Assert.AreEqual(15, AuctionRules.StorageForNight(inv, 14));
            inv.collected = true;
            Assert.AreEqual(0, AuctionRules.StorageForNight(inv, 14));
        }

        [Test]
        public void Proxy_WinnerPaysSecondHighestPlusIncrement()
        {
            var r = AuctionRules.ResolveProxy(new[] { 3000, 4200, 3900 }, 1000);
            Assert.AreEqual(1, r.winner);
            Assert.AreEqual(4000, r.price);   // 3900 + 100
            Assert.AreEqual((1000, 0), AuctionRules.ResolveProxy(new[] { 5000 }, 1000));
            Assert.AreEqual(-1, AuctionRules.ResolveProxy(new[] { 500, 900 }, 1000).winner);
            var tie = AuctionRules.ResolveProxy(new[] { 3000, 3000 }, 1000);
            Assert.AreEqual((3000, 0), tie, "the earlier bidder wins a tie");
            Assert.AreEqual(4100, AuctionRules.ResolveProxy(new[] { 4100, 4050 }, 1000).price, "never above the winner's max");
        }

        [Test]
        public void SoftClose_OnlyExtendsLateBids()
        {
            Assert.AreEqual(15f, AuctionRules.SoftClose(15f, 14.5f));
            float late = 15f - 1f / 60f;
            Assert.AreEqual(late + 2f / 60f, AuctionRules.SoftClose(15f, late), 1e-5);
        }

        [Test]
        public void AiMaxBid_StaysBelowResale_AndFallsWithRepairs()
        {
            var rng = new Random(1);
            var bidders = AuctionRules.GenerateBidders(30, rng).Where(b => b.Archetype == BidderArchetype.SmallTrader || b.Archetype == BidderArchetype.DealerGroup).ToList();
            Assert.IsNotEmpty(bidders);
            foreach (var b in bidders)
            {
                int clean = AuctionRules.MaxBid(b, View(10000), new Random(5));
                int broken = AuctionRules.MaxBid(b, View(10000, 3000), new Random(5));
                Assert.Less(clean, 10000);
                Assert.Greater(clean, 5000);
                Assert.Less(broken, clean);
            }
        }

        [Test]
        public void Archetypes_HaveTheirHabits()
        {
            var franchise = new AuctionBidder { archetype = (int)BidderArchetype.FranchiseDealer, budget = 50000, margin = 0.15f, optimism = 1f, risk = 0.5f };
            Assert.AreEqual(0, AuctionRules.MaxBid(franchise, View(10000, 0, 3), new Random(1)));
            Assert.Greater(AuctionRules.MaxBid(franchise, View(10000, 0, 5), new Random(1)), 0);
            var breaker = new AuctionBidder { archetype = (int)BidderArchetype.Breaker, budget = 50000, margin = 0.3f, optimism = 1f, risk = 0.1f };
            Assert.GreaterOrEqual(AuctionRules.MaxBid(breaker, View(10000, 20000, 1, false), new Random(1)), 2150, "salvage floor");
            var priv = new AuctionBidder { archetype = (int)BidderArchetype.PrivateBuyer, budget = 15000, margin = 0.02f, optimism = 1.08f, risk = 0.2f };
            Assert.AreEqual(0, AuctionRules.MaxBid(priv, View(10000, 0, 4, false), new Random(1)), "private buyers avoid non-runners");
        }

        [Test]
        public void Report_ReliabilityDecidesWhatIsListed()
        {
            var faults = Enumerable.Range(0, 20).Select(i => "Fault " + i).ToList();
            int dealer = 0, salvage = 0;
            for (int i = 0; i < 50; i++)
            {
                dealer += AuctionRules.Report(60f, faults, new string[0], 80000, false, 2, true, true, AuctionRules.Venue("hall"), new Random(i)).faults.Count;
                salvage += AuctionRules.Report(60f, faults, new string[0], 80000, false, 2, true, true, AuctionRules.Venue("salvage"), new Random(i)).faults.Count;
            }
            Assert.Greater(dealer, salvage * 1.4f);
            var r = AuctionRules.Report(10f, faults, new string[0], 80000, true, 1, false, true, AuctionRules.Venue("hall"), new Random(3));
            Assert.AreEqual(1, r.grade);
            Assert.IsFalse(r.starts);
            Assert.IsFalse(r.mileageVerified);
        }

        [Test]
        public void Hammer_UnderReserve_IsProvisional()
        {
            var lot = new AuctionLot { startPrice = 1000, reserve = 5000, currentBid = 4800, leader = AuctionRules.PlayerId };
            Assert.AreEqual(LotStatus.Provisional, AuctionRules.Hammer(lot));
            Assert.AreEqual(0.7f, AuctionRules.SellerAcceptChance(4800, 5000), 1e-5);
            Assert.AreEqual(0f, AuctionRules.SellerAcceptChance(3000, 5000));
            lot.currentBid = 5200;
            Assert.AreEqual(LotStatus.Sold, AuctionRules.Hammer(lot));
            Assert.AreEqual(LotStatus.Unsold, AuctionRules.Hammer(new AuctionLot()));
        }

        [Test]
        public void LiveAi_NeverOutbidsItself()
        {
            var lot = new AuctionLot { startPrice = 1000, currentBid = 2000, leader = "ai0" };
            int i = AuctionRules.LiveAiBid(lot, new[] { 9000, 1500 }, new[] { "ai0", "ai1" }, 1f, new Random(1));
            Assert.AreEqual(-1, i, "ai0 leads and ai1's max is below the next bid");
            lot.leader = "ai1";
            Assert.AreEqual(0, AuctionRules.LiveAiBid(lot, new[] { 9000, 1500 }, new[] { "ai0", "ai1" }, 1f, new Random(1)));
            Assert.AreEqual(2100, AuctionRules.NextBid(lot));
        }
    }
}
