using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class PowertrainTests
    {
        static float WheelRpm(float speedMs, float radius) => speedMs / radius * 60f / (2f * (float)Math.PI);

        [Test]
        public void TorqueCurve_PeaksAtPeakRpm_AndCutsAtRedline()
        {
            var e = new EngineSpec { idleRpm = 800, peakTorqueRpm = 3000, plateauEndRpm = 4000, redlineRpm = 6000, peakTorqueNm = 200 };
            Assert.AreEqual(200f, e.TorqueAt(3500), 0.01f);
            Assert.Less(e.TorqueAt(900), 150f);
            Assert.Less(e.TorqueAt(5900), 200f);
            Assert.AreEqual(0f, e.TorqueAt(6000));
        }

        [Test]
        public void Gearbox_UpshiftsUnderFullThrottle()
        {
            var p = VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Petrol);
            var pt = new Powertrain(p);
            float r = p.WheelRadiusOr(0.32f);
            // Accelerate at full throttle while the speed rises steadily.
            int maxGear = 1;
            for (float v = 0f; v < 45f; v += 0.05f)
            {
                pt.Step(0.02f, 1f, false, WheelRpm(v, r), v);
                maxGear = Math.Max(maxGear, pt.Gear);
                Assert.LessOrEqual(pt.Rpm, p.engine.redlineRpm + 1f);
            }
            Assert.GreaterOrEqual(maxGear, 4, "should be in a high gear at 160 km/h");
        }

        [Test]
        public void Gearbox_DoesNotHunt_AtLightThrottleCruise()
        {
            var p = VehicleProfiles.For(VehicleClass.FamilyHatch, PowertrainType.Petrol);
            var pt = new Powertrain(p);
            float r = p.WheelRadiusOr(0.31f);
            for (float v = 0f; v < 14f; v += 0.05f) pt.Step(0.02f, 0.3f, false, WheelRpm(v, r), v);
            for (int i = 0; i < 20; i++) pt.Step(0.02f, 0.3f, false, WheelRpm(14f, r), 14f);   // let a shift finish
            int gear = pt.Gear;
            int changes = 0;
            for (int i = 0; i < 500; i++)   // 10 s steady cruise at 50 km/h
            {
                pt.Step(0.02f, 0.3f, false, WheelRpm(14f, r), 14f);
                if (pt.Gear != gear && pt.Gear != 0) { changes++; gear = pt.Gear; }
            }
            Assert.LessOrEqual(changes, 1);
        }

        [Test]
        public void Reverse_OnlyEngagesWhenNearlyStopped_AndPushesBackwards()
        {
            var p = VehicleProfiles.For(VehicleClass.CityHatch, PowertrainType.Petrol);
            var pt = new Powertrain(p);
            pt.Step(0.02f, 0.5f, true, 600f, 10f);          // rolling forward at 36 km/h
            Assert.AreNotEqual(-1, pt.Gear);
            float torque = pt.Step(0.02f, 0.8f, true, 0f, 0f);
            Assert.AreEqual(-1, pt.Gear);
            Assert.Less(torque, 0f);
            Assert.AreEqual("R", pt.GearLabel);
        }

        [Test]
        public void EngineOff_GivesNoDriveTorque()
        {
            var pt = new Powertrain(VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Diesel)) { EngineOn = false };
            Assert.LessOrEqual(pt.Step(0.02f, 1f, false, 0f, 0f), 0f);
        }

        [Test]
        public void WornEngine_MakesLessTorque()
        {
            var p = VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Petrol);
            var healthy = new Powertrain(p);
            var worn = new Powertrain(p) { PowerFactor = 0.5f };
            float a = healthy.Step(0.02f, 1f, false, 300f, 2f);
            float b = worn.Step(0.02f, 1f, false, 300f, 2f);
            Assert.AreEqual(a * 0.5f, b, Math.Abs(a) * 0.01f);
        }

        [Test]
        public void Electric_HasFullTorqueFromStandstill_AndRegenWhenLiftingOff()
        {
            var p = VehicleProfiles.For(VehicleClass.FamilyHatch, PowertrainType.Electric);
            var pt = new Powertrain(p);
            float launch = pt.Step(0.02f, 1f, false, 0f, 0f);
            Assert.AreEqual(p.electric.maxTorqueNm * p.electric.ratio * p.electric.efficiency, launch, 1f);
            Assert.Less(pt.Step(0.02f, 0f, false, 800f, 20f), 0f, "regen");
            Assert.AreEqual("D", pt.GearLabel);
        }

        static IEnumerable<TestCaseData> Classes()
        {
            // class, powertrain, 0-100 range (s), top speed range (km/h)
            yield return new TestCaseData(VehicleClass.CityHatch, PowertrainType.Petrol, 10f, 15f, 160f, 195f);
            yield return new TestCaseData(VehicleClass.FamilyHatch, PowertrainType.Petrol, 7f, 10f, 190f, 230f);
            yield return new TestCaseData(VehicleClass.Sedan, PowertrainType.Diesel, 8f, 11f, 195f, 230f);
            yield return new TestCaseData(VehicleClass.SportsSedan, PowertrainType.Petrol, 5f, 7f, 245f, 255f);
            yield return new TestCaseData(VehicleClass.SUV, PowertrainType.Diesel, 8f, 11f, 185f, 215f);
            yield return new TestCaseData(VehicleClass.Pickup, PowertrainType.Diesel, 9.5f, 13f, 170f, 200f);
            yield return new TestCaseData(VehicleClass.Van, PowertrainType.Diesel, 11f, 15f, 155f, 185f);
            yield return new TestCaseData(VehicleClass.Sports, PowertrainType.Petrol, 4f, 6.5f, 280f, 292f);
            yield return new TestCaseData(VehicleClass.Luxury, PowertrainType.Hybrid, 5f, 7f, 245f, 255f);
            yield return new TestCaseData(VehicleClass.ElectricHatch, PowertrainType.Electric, 5.5f, 9f, 140f, 180f);
        }

        [TestCaseSource(nameof(Classes))]
        public void Presets_PerformLikeTheirClass(VehicleClass c, PowertrainType pt, float min0100, float max0100, float minTop, float maxTop)
        {
            var r = PerformanceEstimator.Run(VehicleProfiles.For(c, pt));
            Assert.That(r.ZeroTo100Seconds, Is.InRange(min0100, max0100), "0-100 km/h");
            Assert.That(r.TopSpeedKmh, Is.InRange(minTop, maxTop), "top speed");
            Assert.That(r.StoppingDistance100, Is.InRange(30f, 50f), "braking from 100 km/h");
        }

        [Test]
        public void EveryClassAndPowertrain_HasAUsableProfile()
        {
            foreach (VehicleClass c in Enum.GetValues(typeof(VehicleClass)))
            foreach (PowertrainType pt in Enum.GetValues(typeof(PowertrainType)))
            {
                var p = VehicleProfiles.For(c, pt);
                Assert.Greater(p.chassis.massKg, 500f, p.name);
                Assert.Greater(PerformanceEstimator.Run(p).TopSpeedKmh, 100f, p.name);
                if (pt == PowertrainType.Electric) Assert.IsTrue(p.IsElectric, p.name);
            }
        }

        [Test]
        public void Copy_IsIndependent()
        {
            var a = VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Petrol);
            var b = a.Copy();
            b.gearbox.ratios[0] = 99f;
            b.chassis.massKg = 1f;
            Assert.AreNotEqual(99f, a.gearbox.ratios[0]);
            Assert.AreNotEqual(1f, a.chassis.massKg);
        }

        [TestCase("Petrol", PowertrainType.Petrol)]
        [TestCase("diesel", PowertrainType.Diesel)]
        [TestCase("Plug-in Hybrid", PowertrainType.Hybrid)]
        [TestCase("Electric", PowertrainType.Electric)]
        [TestCase("EV", PowertrainType.Electric)]
        [TestCase(null, PowertrainType.Petrol)]
        public void PowertrainType_Parse(string text, PowertrainType expected) => Assert.AreEqual(expected, PowertrainTypes.Parse(text));
    }

    public class DriveControlTests
    {
        [Test]
        public void S_BrakesWhileRollingForward_ThenReverses()
        {
            var moving = DriveInput.Map(-1f, 10f, false);
            Assert.AreEqual(1f, moving.Brake);
            Assert.IsFalse(moving.WantReverse);
            var stopped = DriveInput.Map(-1f, 0.2f, false);
            Assert.IsTrue(stopped.WantReverse);
            Assert.AreEqual(1f, stopped.Throttle);
        }

        [Test]
        public void W_BrakesWhileReversing_ThenGoesForward()
        {
            var reversing = DriveInput.Map(1f, -5f, true);
            Assert.AreEqual(1f, reversing.Brake);
            Assert.IsTrue(reversing.WantReverse);
            var stopped = DriveInput.Map(1f, -0.1f, true);
            Assert.IsFalse(stopped.WantReverse);
            Assert.AreEqual(1f, stopped.Throttle);
        }

        [Test]
        public void Abs_ReleasesLockingWheel_OnlyWhenEnabled()
        {
            Assert.AreEqual(1f, DriveAssists.AbsFactor(true, -0.1f));
            Assert.Less(DriveAssists.AbsFactor(true, -0.6f), 1f);
            Assert.AreEqual(1f, DriveAssists.AbsFactor(false, -0.9f));
        }

        [Test]
        public void Tcs_CutsSpinningWheel_OnlyWhenEnabled()
        {
            Assert.AreEqual(1f, DriveAssists.TcsFactor(true, 0.1f));
            Assert.Less(DriveAssists.TcsFactor(true, 0.7f), 1f);
            Assert.AreEqual(1f, DriveAssists.TcsFactor(false, 1f));
        }

        [Test]
        public void Esc_BrakesAgainstOversteer()
        {
            // Driving straight (no steering) but the car is spinning clockwise -> brake the left side to straighten it.
            Assert.Greater(DriveAssists.EscBrake(true, 1.2f, 20f, 0f, 2.6f), 0f);
            Assert.Less(DriveAssists.EscBrake(true, -1.2f, 20f, 0f, 2.6f), 0f);
            Assert.AreEqual(0f, DriveAssists.EscBrake(true, 0.1f, 20f, 0f, 2.6f));
            Assert.AreEqual(0f, DriveAssists.EscBrake(false, 2f, 20f, 0f, 2.6f));
            Assert.AreEqual(0f, DriveAssists.EscBrake(true, 2f, 3f, 0f, 2.6f), "inactive at walking pace");
        }

        [Test]
        public void SteerLimit_ShrinksWithSpeed()
        {
            Assert.AreEqual(35f, DriveAssists.SteerLimit(35f, 0.35f, 0f), 0.01f);
            Assert.AreEqual(35f * 0.35f, DriveAssists.SteerLimit(35f, 0.35f, 40f), 0.01f);
        }

        [Test]
        public void Fuel_HardDrivingUsesMore()
        {
            float gentle = FuelModel.PercentUsed(1000f, 0.1f, 4f);
            float hard = FuelModel.PercentUsed(1000f, 1f, 4f);
            Assert.Greater(hard, gentle * 2f);
            Assert.AreEqual(4f, FuelModel.PercentUsed(1000f, 0.5f, 4f), 0.1f, "average load ~ the profile's rate");
            Assert.AreEqual(0f, FuelModel.PercentUsed(-5f, 1f, 4f));
        }
    }

    public class ComponentTests
    {
        static readonly float[] Groups = { 80f, 60f, 40f, 90f, 70f, 50f };

        [Test]
        public void GroupWeights_AddUpToOne()
        {
            foreach (var pt in new[] { PowertrainType.Petrol, PowertrainType.Electric })
                foreach (ComponentGroup g in Enum.GetValues(typeof(ComponentGroup)))
                {
                    if (pt == PowertrainType.Electric && g == ComponentGroup.Electronics) continue;   // EVs have no alternator/starter: normalised
                    float sum = ComponentCatalog.InGroup(g, pt).Sum(d => d.Weight);
                    Assert.AreEqual(1f, sum, 0.001f, $"{g} ({pt})");
                }
        }

        [Test]
        public void ComponentIds_AreUnique()
        {
            var ids = ComponentCatalog.All.Select(d => (int)d.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }

        [Test]
        public void Ensure_FromOldSixParts_KeepsEachGroupValue()
        {
            var rng = new Random(7);
            var list = new List<ComponentState>();
            Assert.IsTrue(ComponentSet.Ensure(list, PowertrainType.Petrol, Groups, () => (float)(rng.NextDouble() * 2 - 1)));
            foreach (ComponentGroup g in Enum.GetValues(typeof(ComponentGroup)))
                Assert.AreEqual(Groups[(int)g], ComponentSet.GroupHealth(list, g, PowertrainType.Petrol), 0.05f, g.ToString());
            Assert.IsFalse(ComponentSet.Ensure(list, PowertrainType.Petrol, Groups, null), "second call changes nothing");
        }

        [Test]
        public void Ensure_GivesElectricCarsABatteryInsteadOfAnEngine()
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Electric, Groups, null);
            Assert.IsTrue(ComponentSet.Has(list, ComponentId.TractionBattery));
            Assert.IsFalse(ComponentSet.Has(list, ComponentId.EngineInternals));
            Assert.IsFalse(ComponentSet.Has(list, ComponentId.OilAndFilter));
            Assert.IsTrue(ComponentSet.Has(list, ComponentId.TyreFrontLeft));
        }

        [Test]
        public void Ensure_RemovesUnknownOrWrongComponents()
        {
            var list = new List<ComponentState> { new ComponentState { id = 9999, health = 50 }, new ComponentState(ComponentId.TractionBattery, 50), null };
            ComponentSet.Ensure(list, PowertrainType.Diesel, Groups, null);
            Assert.IsFalse(list.Any(s => s == null || s.id == 9999));
            Assert.IsFalse(ComponentSet.Has(list, ComponentId.TractionBattery));
        }

        [Test]
        public void SetGroup_RepairsEveryPartOfTheGroup()
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Petrol, Groups, null);
            ComponentSet.SetGroup(list, ComponentGroup.Brakes, PowertrainType.Petrol, 100f);
            Assert.AreEqual(100f, ComponentSet.GroupHealth(list, ComponentGroup.Brakes, PowertrainType.Petrol), 0.001f);
            Assert.AreEqual(100f, ComponentSet.Get(list, ComponentId.ShockAbsorbers));
            Assert.AreEqual(Groups[(int)ComponentGroup.Body], ComponentSet.GroupHealth(list, ComponentGroup.Body, PowertrainType.Petrol), 0.05f);
        }

        [Test]
        public void FrontCrash_DamagesFrontParts_NotTheBoot()
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Petrol, new[] { 100f, 100f, 100f, 100f, 100f, 100f }, null);
            ComponentSet.Damage(list, ImpactZone.Front, 30f);
            Assert.AreEqual(70f, ComponentSet.Get(list, ComponentId.BumperFront), 0.01f);
            Assert.Less(ComponentSet.Get(list, ComponentId.CoolingSystem), 100f);
            Assert.AreEqual(100f, ComponentSet.Get(list, ComponentId.BootLid));
        }

        [TestCase(0f, 0.6f, 2.4f, ImpactZone.Front)]
        [TestCase(0.3f, 0.6f, -2.2f, ImpactZone.Rear)]
        [TestCase(-0.95f, 0.6f, 0.4f, ImpactZone.Left)]
        [TestCase(0.95f, 0.6f, -0.4f, ImpactZone.Right)]
        [TestCase(0f, 1.45f, 0f, ImpactZone.Roof)]
        [TestCase(0f, 0.05f, 0f, ImpactZone.Underside)]
        public void ZoneFromLocalPoint(float x, float y, float z, ImpactZone expected) =>
            Assert.AreEqual(expected, ComponentCatalog.ZoneFromLocalPoint(x, y, z, 0.9f, 2.3f, 1.5f));

        [Test]
        public void Wear_HitsTyresAndOilMoreThanTheRoof()
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Petrol, new[] { 100f, 100f, 100f, 100f, 100f, 100f }, null);
            ComponentSet.ApplyWear(list, 100f);
            Assert.Less(ComponentSet.Get(list, ComponentId.OilAndFilter), ComponentSet.Get(list, ComponentId.Roof));
            Assert.Less(ComponentSet.Get(list, ComponentId.TyreFrontLeft), 100f);
            Assert.Greater(ComponentSet.Get(list, ComponentId.TyreFrontLeft), 80f, "100 km is not a whole tyre");
        }

        [Test]
        public void Effects_PerfectCarIsPerfect_WornCarIsWorse()
        {
            var good = new List<ComponentState>();
            ComponentSet.Ensure(good, PowertrainType.Petrol, new[] { 100f, 100f, 100f, 100f, 100f, 100f }, null);
            var e = ComponentSet.Effects(good, PowertrainType.Petrol);
            Assert.AreEqual(1f, e.Power, 0.001f);
            Assert.AreEqual(1f, e.Brakes, 0.001f);
            Assert.IsTrue(e.CanStart);

            var bad = new List<ComponentState>();
            ComponentSet.Ensure(bad, PowertrainType.Petrol, new[] { 20f, 20f, 20f, 100f, 100f, 100f }, null);
            ComponentSet.Set(bad, ComponentId.Battery12V, 0f);
            var w = ComponentSet.Effects(bad, PowertrainType.Petrol);
            Assert.Less(w.Power, 0.7f);
            Assert.Less(w.Brakes, 0.7f);
            Assert.Less(w.FrontGrip, 0.9f);
            Assert.IsFalse(w.CanStart);
        }
    }
}
