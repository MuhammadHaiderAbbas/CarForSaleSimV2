using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class FaultTests
    {
        static List<ComponentState> Car(float health, PowertrainType p = PowertrainType.Petrol)
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, p, null, null, health);
            return list;
        }

        [Test]
        public void Catalogue_Has40Faults_WithUniqueIds_OnRealComponents()
        {
            Assert.AreEqual(40, FaultCatalog.All.Count);
            Assert.AreEqual(40, FaultCatalog.All.Select(f => f.Id).Distinct().Count());
            foreach (var f in FaultCatalog.All)
            {
                Assert.IsTrue(ComponentCatalog.IsKnown((int)f.Component), f.Name);
                Assert.AreNotEqual(InspectMethod.None, f.FoundBy, f.Name);
                Assert.IsNotNull(WorkshopCatalog.JobFor(f.Component), "no repair job fixes " + f.Name);
            }
        }

        [Test]
        public void WornCars_GetMoreFaults_ThanGoodOnes()
        {
            int worn = 0, good = 0;
            for (int i = 0; i < 200; i++)
            {
                worn += FaultModel.Generate(Car(30f), 180000, new Random(i)).Count;
                good += FaultModel.Generate(Car(95f), 20000, new Random(i)).Count;
            }
            Assert.Greater(worn, good * 3);
        }

        [Test]
        public void ElectricCars_NeverGetCombustionFaults()
        {
            for (int i = 0; i < 100; i++)
                foreach (var f in FaultModel.Generate(Car(25f, PowertrainType.Electric), 150000, new Random(i)))
                    Assert.AreNotEqual(FaultId.ClutchSlipping, f.Id);
        }

        [Test]
        public void Fault_CapsComponentHealth_AndProgresses()
        {
            var comps = Car(100f);
            var faults = new List<FaultInstance> { new FaultInstance(FaultId.ClutchSlipping, 0.5f) };
            FaultModel.ApplyHealthCaps(comps, faults);
            float capped = ComponentSet.Get(comps, ComponentId.Clutch);
            Assert.Less(capped, 70f);
            var severe = FaultModel.Progress(comps, faults, 10);
            Assert.AreEqual(1, severe.Count, "0.5 + 10 x 0.02 crosses 0.6");
            Assert.Less(ComponentSet.Get(comps, ComponentId.Clutch), capped);
        }

        [Test]
        public void ValueLoss_KnownOnly_IgnoresHiddenFaults()
        {
            var faults = new List<FaultInstance> { new FaultInstance(FaultId.HeadGasketLeak, 0.8f) };
            Assert.AreEqual(0f, FaultModel.ValueLoss(faults, knownOnly: true));
            Assert.Greater(FaultModel.ValueLoss(faults, knownOnly: false), 0.1f);
            faults[0].known = true;
            Assert.Greater(FaultModel.ValueLoss(faults, knownOnly: true), 0.1f);
        }
    }

    public class InspectionTests
    {
        static List<ComponentState> Car()
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Petrol, null, null, 80f);
            return list;
        }

        [Test]
        public void EachMethod_OnlyFindsItsOwnFaults()
        {
            var faults = new List<FaultInstance>
            {
                new FaultInstance(FaultId.CatalystFailing, 0.7f),   // scanner only
                new FaultInstance(FaultId.SillCorrosion, 0.7f),     // lift only
                new FaultInstance(FaultId.SmokerOdour, 0.7f),       // interior only
            };
            var k = new VehicleKnowledge();
            var r = InspectionEngine.Run(InspectMethod.Scanner, Car(), faults, new HistoryRecord(), k, 0.5f, new Random(1));
            CollectionAssert.AreEqual(new[] { FaultId.CatalystFailing }, r.found.Select(f => f.Id).ToArray());
            Assert.IsTrue(k.Done(InspectMethod.Scanner));
            Assert.IsFalse(faults[1].known);
            InspectionEngine.Run(InspectMethod.Lift, Car(), faults, new HistoryRecord(), k, 0.5f, new Random(1));
            Assert.IsTrue(faults[1].known);
            Assert.IsFalse(faults[2].known);
        }

        [Test]
        public void RoadTest_RarelyNoticesMildFaults()
        {
            Assert.Less(InspectionEngine.NoticeChance(InspectMethod.RoadTest, 0.1f, 1f), 0.2f);
            Assert.Greater(InspectionEngine.NoticeChance(InspectMethod.RoadTest, 0.6f, 0f), 0.6f);
        }

        [Test]
        public void FullInspection_FindsEveryPhysicalFault()
        {
            var faults = FaultCatalog.All.Where(f => f.Component != ComponentId.TractionBattery && f.Component != ComponentId.ElectricMotor
                                                   && f.Component != ComponentId.Inverter && f.Component != ComponentId.ChargingSystem)
                                         .Select(f => new FaultInstance(f.Id, 0.9f)).ToList();
            InspectionEngine.Full(Car(), faults, new HistoryRecord(), new VehicleKnowledge(), new Random(3));
            Assert.IsTrue(faults.All(f => f.known), string.Join(", ", faults.Where(f => !f.known).Select(f => f.Def.Name)));
        }

        [Test]
        public void HistoryCheck_ExposesCatN_AndClocking()
        {
            var docs = new DocumentSet { generated = true, motExpiryDay = 400 };
            docs.motHistory.Add(new MotRecord { year = 2023, mileage = 140000, passed = true });
            var h = new HistoryRecord { generated = true, writeOff = WriteOff.CatN, clocked = true };
            Assert.AreEqual(1f, VehicleRecords.ValueMultiplier(docs, h, 2015, 1, knownOnly: true), 0.001f);
            var lines = InspectionEngine.HistoryCheck(docs, h, 90000, 5, new VehicleKnowledge());
            Assert.IsTrue(lines.Any(l => l.Contains("Category N")));
            Assert.IsTrue(lines.Any(l => l.Contains("MILEAGE ANOMALY")));
            Assert.Less(VehicleRecords.ValueMultiplier(docs, h, 2015, 5, knownOnly: true), 0.75f);
        }

        [Test]
        public void InteriorCheck_CanSuspectClocking()
        {
            int suspected = 0;
            for (int i = 0; i < 50; i++)
            {
                var h = new HistoryRecord { generated = true, clocked = true };
                InspectionEngine.Run(InspectMethod.Interior, Car(), new List<FaultInstance>(), h, new VehicleKnowledge(), 1f, new Random(i));
                if (h.clockingSuspected) suspected++;
            }
            Assert.Greater(suspected, 25);
        }
    }

    public class DocumentTests
    {
        [Test]
        public void Vin_HasValidCheckDigit_AndNoForbiddenLetters()
        {
            var rng = new Random(7);
            for (int i = 0; i < 100; i++)
            {
                string vin = Vin.Generate(rng);
                Assert.AreEqual(17, vin.Length);
                Assert.IsTrue(Vin.IsValid(vin), vin);
                Assert.IsFalse(vin.IndexOfAny(new[] { 'I', 'O', 'Q' }) >= 0, vin);
            }
            Assert.IsTrue(Vin.IsValid("1M8GDM9AXKP042788"), "textbook example");
            Assert.IsFalse(Vin.IsValid("1M8GDM9A1KP042788"));
        }

        [Test]
        public void UkPlate_EncodesTheYear()
        {
            Assert.AreEqual(19, UkPlate.AgeIdentifier(2019, false));
            Assert.AreEqual(69, UkPlate.AgeIdentifier(2019, true));
            var rng = new Random(2);
            for (int i = 0; i < 30; i++)
            {
                string plate = UkPlate.Generate(2017, rng);
                Assert.AreEqual(8, plate.Length, plate);
                Assert.AreEqual(2017, UkPlate.YearOf(plate), plate);
            }
        }

        [Test]
        public void Generate_GivesOldCarsMotHistory_AndYoungCarsNone()
        {
            var d = new DocumentSet(); var h = new HistoryRecord();
            VehicleRecords.Generate(d, h, 2012, 120000, 1, new Random(4));
            Assert.Greater(d.motHistory.Count, 5);
            Assert.AreNotEqual(-1, d.motExpiryDay);
            var d2 = new DocumentSet(); var h2 = new HistoryRecord();
            VehicleRecords.Generate(d2, h2, VehicleRecords.GameStartYear - 1, 15000, 1, new Random(4));
            Assert.AreEqual(0, d2.motHistory.Count);
            Assert.AreEqual(-1, d2.motExpiryDay);
            Assert.IsTrue(VehicleRecords.MotValid(d2, VehicleRecords.GameStartYear - 1, 1));
        }

        [Test]
        public void ClockedCars_ShowAMileageAnomaly_InTheirMotHistory()
        {
            int clocked = 0;
            for (int i = 0; i < 400; i++)
            {
                var d = new DocumentSet(); var h = new HistoryRecord();
                VehicleRecords.Generate(d, h, 2010, 90000, 1, new Random(i));
                if (!h.clocked) continue;
                clocked++;
                Assert.IsTrue(VehicleRecords.MileageAnomaly(d, 90000));
            }
            Assert.Greater(clocked, 5);
        }

        [Test]
        public void MissingLogbook_AndExpiredMot_LowerTheValue()
        {
            var d = new DocumentSet { generated = true, v5cPresent = false, motExpiryDay = 3, keys = 2, service = ServiceHistory.Partial };
            float m = VehicleRecords.ValueMultiplier(d, null, 2010, 10, knownOnly: false);
            Assert.AreEqual(0.75f * 0.9f, m, 0.001f);
        }

        [Test]
        public void Mot_FailsBaldTyres_AdvisesWornOnes_AndRevealsFaults()
        {
            var comps = new List<ComponentState>();
            ComponentSet.Ensure(comps, PowertrainType.Petrol, null, null, 90f);
            ComponentSet.Set(comps, ComponentId.TyreFrontLeft, 10f);
            ComponentSet.Set(comps, ComponentId.TyreRearRight, 30f);
            var faults = new List<FaultInstance> { new FaultInstance(FaultId.WornBushes, 0.4f) };
            var rec = MotTest.Run(comps, faults, 100000, 5, 2025);
            Assert.IsFalse(rec.passed);
            Assert.IsTrue(rec.fails.Any(f => f.Contains("front left")));
            Assert.IsTrue(rec.advisories.Any(a => a.Contains("rear right")));
            Assert.IsTrue(faults[0].known, "an advisory reveals the fault");

            ComponentSet.Set(comps, ComponentId.TyreFrontLeft, 90f);
            Assert.IsTrue(MotTest.Run(comps, new List<FaultInstance>(), 100000, 5, 2025).passed);
        }
    }

    public class WorkshopTests
    {
        [Test]
        public void EveryJobPart_Exists_AndEveryComponentHasAJob()
        {
            foreach (var j in WorkshopCatalog.Jobs)
                foreach (var (part, _) in j.Parts) Assert.IsNotNull(WorkshopCatalog.Part(part), j.Id + " -> " + part);
            foreach (var c in ComponentCatalog.All)
                Assert.IsNotNull(WorkshopCatalog.JobFor(c.Id), "no job for " + c.Name);
        }

        [Test]
        public void Prices_FollowTierSupplierAndCarClass()
        {
            int oem = WorkshopCatalog.PartPrice("clutch_kit", PartTier.OEM, Supplier.MotorFactor, 20000);
            Assert.AreEqual(330, oem);
            Assert.Less(WorkshopCatalog.PartPrice("clutch_kit", PartTier.Aftermarket, Supplier.MotorFactor, 20000), oem);
            Assert.Less(WorkshopCatalog.PartPrice("clutch_kit", PartTier.OEM, Supplier.Online, 20000), oem);
            Assert.Greater(WorkshopCatalog.PartPrice("clutch_kit", PartTier.OEM, Supplier.MotorFactor, 60000), oem);
            Assert.IsFalse(WorkshopCatalog.Sells(Supplier.BreakersYard, WorkshopCatalog.Part("oil_kit"), PartTier.Used));
            Assert.IsTrue(WorkshopCatalog.Sells(Supplier.BreakersYard, WorkshopCatalog.Part("turbo"), PartTier.Used));
            Assert.IsFalse(WorkshopCatalog.Sells(Supplier.MotorFactor, WorkshopCatalog.Part("turbo"), PartTier.Used));
        }

        [Test]
        public void Inventory_TakesParts_AndReceivesOrders()
        {
            var inv = new PartsInventory();
            var job = WorkshopCatalog.Job("shocks");
            inv.Add("shock", PartTier.OEM, 1);
            Assert.IsFalse(inv.HasPartsFor(job, PartTier.OEM));
            inv.orders.Add(new PartOrder { part = "shock", tier = (int)PartTier.OEM, count = 1, arrivesDay = 4 });
            Assert.AreEqual(0, inv.Receive(3).Count);
            Assert.AreEqual(1, inv.Receive(4).Count);
            Assert.IsTrue(inv.TakePartsFor(job, PartTier.OEM));
            Assert.AreEqual(0, inv.Count("shock", PartTier.OEM));
            Assert.AreEqual(0, inv.stock.Count);
        }

        [Test]
        public void Repair_RestoresHealth_AndClearsFaults()
        {
            var comps = new List<ComponentState>();
            ComponentSet.Ensure(comps, PowertrainType.Petrol, null, null, 40f);
            var faults = new List<FaultInstance> { new FaultInstance(FaultId.ClutchSlipping, 0.7f), new FaultInstance(FaultId.BulbOut, 1f) };
            int fixedCount = WorkshopCatalog.Apply(WorkshopCatalog.Job("clutch"), comps, faults, PartTier.OEM, new Random(1));
            Assert.AreEqual(1, fixedCount);
            Assert.AreEqual(100f, ComponentSet.Get(comps, ComponentId.Clutch));
            Assert.AreEqual(FaultId.BulbOut, faults.Single().Id);
            float used = WorkshopCatalog.ResultHealth(PartTier.Used, new Random(5));
            Assert.That(used, Is.InRange(50f, 80f));
        }

        [Test]
        public void Suggested_ListsWornAndKnownFaultyComponents()
        {
            var comps = new List<ComponentState>();
            ComponentSet.Ensure(comps, PowertrainType.Petrol, null, null, 95f);
            ComponentSet.Set(comps, ComponentId.BrakePadsFront, 30f);
            var faults = new List<FaultInstance> { new FaultInstance(FaultId.SmokerOdour, 0.5f) { known = true }, new FaultInstance(FaultId.TurboWhistle, 0.5f) };
            var ids = WorkshopCatalog.Suggested(comps, faults).Select(j => j.Id).ToList();
            CollectionAssert.Contains(ids, "pads_front");
            CollectionAssert.Contains(ids, "interior");
            CollectionAssert.DoesNotContain(ids, "turbo");
        }
    }
}
