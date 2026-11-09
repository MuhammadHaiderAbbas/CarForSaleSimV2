using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class BoardTests
    {
        [Test]
        public void Card_MovesFromWaiting_ToReady_WhenPartsArrive()
        {
            var inv = new PartsInventory();
            var card = new BoardCard { id = "a", job = "pads_front", carId = "car", tier = (int)PartTier.OEM };
            Assert.AreEqual(BoardColumn.WaitingForParts, BoardLogic.ColumnOf(card, inv, false));
            inv.Add("pads_f", PartTier.OEM, 1);
            Assert.AreEqual(BoardColumn.Ready, BoardLogic.ColumnOf(card, inv, false));
            Assert.AreEqual(BoardColumn.InProgress, BoardLogic.ColumnOf(card, inv, true));
            card.done = true;
            Assert.AreEqual(BoardColumn.Done, BoardLogic.ColumnOf(card, inv, false));
        }

        [Test]
        public void Mechanic_OnlyWorksDuringWorkingHours()
        {
            Assert.AreEqual(0f, BoardLogic.WorkingHours(5f, 7f));
            Assert.AreEqual(2f, BoardLogic.WorkingHours(6f, 10f), 0.001f);
            Assert.AreEqual(10f, BoardLogic.WorkingHours(0f, 24f), 0.001f);
            Assert.AreEqual(0f, BoardLogic.WorkingHours(19f, 23f));
        }

        [Test]
        public void OldDoneCards_AreCleared()
        {
            var cards = new List<BoardCard>
            {
                new BoardCard { id = "1", done = true, doneDay = 3 },
                new BoardCard { id = "2", done = true, doneDay = 4 },
                new BoardCard { id = "3" },
            };
            Assert.AreEqual(1, BoardLogic.ClearOldDone(cards, 4));
            CollectionAssert.AreEqual(new[] { "2", "3" }, cards.Select(c => c.id).ToArray());
        }

        [Test]
        public void CounterPrice_IsTenPercentOff_AndSameDay()
        {
            int factor = WorkshopCatalog.PartPrice("turbo", PartTier.OEM, Supplier.MotorFactor, 20000);
            int counter = WorkshopCatalog.PartPrice("turbo", PartTier.OEM, Supplier.Counter, 20000);
            Assert.AreEqual((int)System.Math.Round(factor * 0.9), counter);
            Assert.AreEqual(0, WorkshopCatalog.LeadDays(Supplier.Counter));
            Assert.IsFalse(WorkshopCatalog.Sells(Supplier.Counter, WorkshopCatalog.Part("turbo"), PartTier.Used));
        }
    }

    public class HandsOnTests
    {
        [Test]
        public void EveryJob_HasUndoFitTighten_Steps()
        {
            foreach (var job in WorkshopCatalog.Jobs)
            {
                var steps = HandsOn.Steps(job);
                int bolts = HandsOn.BoltsFor(job);
                Assert.AreEqual(bolts * 2 + 1, steps.Count, job.Id);
                Assert.IsTrue(steps[0].Label.StartsWith("Undo"), job.Id);
                Assert.IsTrue(steps[bolts].Label.Contains(job.Name), job.Id);
                Assert.IsTrue(steps.Last().Label.StartsWith("Tighten"), job.Id);
            }
        }

        [Test]
        public void Areas_MakeSense()
        {
            Assert.AreEqual(CarArea.FrontWheels, HandsOn.AreaOf(WorkshopCatalog.Job("pads_front")));
            Assert.AreEqual(CarArea.RearWheels, HandsOn.AreaOf(WorkshopCatalog.Job("pads_rear")));
            Assert.AreEqual(CarArea.Bonnet, HandsOn.AreaOf(WorkshopCatalog.Job("timing_belt")));
            Assert.AreEqual(CarArea.Underside, HandsOn.AreaOf(WorkshopCatalog.Job("exhaust")));
            Assert.AreEqual(CarArea.Interior, HandsOn.AreaOf(WorkshopCatalog.Job("interior")));
            Assert.AreEqual(CarArea.Side, HandsOn.AreaOf(WorkshopCatalog.Job("bodywork")));
        }

        [Test]
        public void InspectionPoints_DependOnScannerAndLift()
        {
            var basic = HandsOnInspection.Points(false, false);
            Assert.IsFalse(basic.Any(p => p.Method == InspectMethod.Scanner || p.Method == InspectMethod.Lift));
            var full = HandsOnInspection.Points(true, true);
            Assert.IsTrue(full.Any(p => p.Method == InspectMethod.Scanner));
            Assert.IsTrue(full.Any(p => p.Method == InspectMethod.Lift));
            var done = new HashSet<int> { 0, 1, 2 };
            Assert.IsFalse(HandsOnInspection.MethodComplete(basic, done, InspectMethod.Visual));
            done.Add(3);
            Assert.IsTrue(HandsOnInspection.MethodComplete(basic, done, InspectMethod.Visual));
        }
    }
}
