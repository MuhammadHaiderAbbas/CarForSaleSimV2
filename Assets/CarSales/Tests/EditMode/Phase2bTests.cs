using System;
using System.Collections.Generic;
using CarSales.Sim;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class ManualGearboxTests
    {
        static float WheelRpm(float speedMs, float radius) => speedMs / radius * 60f / (2f * (float)Math.PI);

        static Powertrain Manual(out VehicleProfile p)
        {
            p = VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Petrol);
            return new Powertrain(p) { Manual = true };
        }

        static void Settle(Powertrain pt, float speed, float radius)
        {
            for (int i = 0; i < 30; i++) pt.Step(0.02f, 0.3f, false, WheelRpm(speed, radius), speed);
        }

        [Test]
        public void NeverShiftsByItself()
        {
            var pt = Manual(out var p);
            float r = p.WheelRadiusOr(0.32f);
            for (float v = 0f; v < 25f; v += 0.05f) pt.Step(0.02f, 1f, false, WheelRpm(v, r), v);
            Assert.AreEqual(1, pt.Gear);
            Assert.IsTrue(pt.AtLimiter || pt.Rpm >= p.engine.redlineRpm - 1f, "1st gear hits the limiter at 90 km/h");
        }

        [Test]
        public void ShiftUpAndDown_TakesShiftTime()
        {
            var pt = Manual(out var p);
            float r = p.WheelRadiusOr(0.32f);
            Settle(pt, 10f, r);
            Assert.IsTrue(pt.ShiftUp(10f));
            Assert.AreEqual("N", pt.GearLabel, "torque is cut while shifting");
            Settle(pt, 10f, r);
            Assert.AreEqual(2, pt.Gear);
            Assert.IsTrue(pt.ShiftDown(10f));
            Settle(pt, 10f, r);
            Assert.AreEqual(1, pt.Gear);
        }

        [Test]
        public void RefusesDownshiftThatWouldOverRev()
        {
            var pt = Manual(out var p);
            float r = p.WheelRadiusOr(0.32f);
            for (int g = 1; g < 4; g++) { pt.ShiftUp(30f); Settle(pt, 30f, r); }
            Assert.AreEqual(4, pt.Gear);
            for (int i = 0; i < 3; i++) { pt.ShiftDown(30f); Settle(pt, 30f, r); }   // 108 km/h: 1st and 2nd would over-rev
            Assert.Greater(pt.Gear, 1);
        }

        [Test]
        public void Reverse_OnlyFromStandstill()
        {
            var pt = Manual(out var p);
            Assert.IsFalse(pt.ShiftDown(5f), "not while rolling");
            Assert.IsTrue(pt.ShiftDown(0f));
            Assert.AreEqual(-1, pt.Gear);
            Assert.Less(pt.Step(0.02f, 0.5f, true, 0f, 0f), 0f);
            Assert.IsTrue(pt.ShiftUp(0f));
            Assert.AreEqual(1, pt.Gear);
        }

        [Test]
        public void MapManual_WIsThrottle_SIsBrake()
        {
            var w = DriveInput.MapManual(1f, false);
            Assert.AreEqual(1f, w.Throttle);
            Assert.AreEqual(0f, w.Brake);
            var s = DriveInput.MapManual(-0.5f, true);
            Assert.AreEqual(0.5f, s.Brake);
            Assert.AreEqual(0f, s.Throttle);
            Assert.IsTrue(s.WantReverse);
        }
    }

    public class DentModelTests
    {
        static List<ComponentState> Car(float body)
        {
            var list = new List<ComponentState>();
            ComponentSet.Ensure(list, PowertrainType.Petrol, new[] { 100f, 100f, 100f, body, 100f, 100f }, null);
            return list;
        }

        static void Displace(float[] d, float x, float y, float z, out float dx, out float dy, out float dz) =>
            DentModel.Displace(x, y, z, -0.9f, 0.9f, 1.5f, -2.3f, 2.3f, d, out dx, out dy, out dz);

        [Test]
        public void HealthyOrScratchedCar_HasNoDents()
        {
            Assert.IsFalse(DentModel.Any(DentModel.ZoneDepths(Car(100f))));
            Assert.IsFalse(DentModel.Any(DentModel.ZoneDepths(Car(90f))), "light wear is scratches, not dents");
            Assert.IsFalse(DentModel.Any(DentModel.ZoneDepths(null)));
        }

        [Test]
        public void FrontDamage_PushesTheNoseBack_NotTheTail()
        {
            var list = Car(100f);
            ComponentSet.Damage(list, ImpactZone.Front, 70f);
            var d = DentModel.ZoneDepths(list);
            Assert.Greater(d[(int)ImpactZone.Front], 0.05f);
            Assert.AreEqual(0f, d[(int)ImpactZone.Rear]);

            Displace(d, 0.2f, 0.6f, 2.3f, out _, out _, out float noseDz);
            Displace(d, 0.2f, 0.6f, -2.3f, out _, out _, out float tailDz);
            Displace(d, 0.2f, 0.6f, 0f, out _, out _, out float midDz);
            Assert.Less(noseDz, -0.02f);
            Assert.AreEqual(0f, tailDz);
            Assert.AreEqual(0f, midDz, "the dent stays near the nose");
        }

        [Test]
        public void SideDamage_PushesThatSideIn()
        {
            var list = Car(100f);
            ComponentSet.Damage(list, ImpactZone.Left, 80f);
            var d = DentModel.ZoneDepths(list);
            Displace(d, -0.9f, 0.6f, 0f, out float leftDx, out _, out _);
            Displace(d, 0.9f, 0.6f, 0f, out float rightDx, out _, out _);
            Assert.Greater(leftDx, 0.01f, "left side moves towards the centre (+x)");
            Assert.AreEqual(0f, rightDx);
        }

        [Test]
        public void Repair_RemovesDents_AndSignatureChanges()
        {
            var list = Car(100f);
            ComponentSet.Damage(list, ImpactZone.Rear, 60f);
            int damaged = DentModel.Signature(DentModel.ZoneDepths(list));
            ComponentSet.SetGroup(list, ComponentGroup.Body, PowertrainType.Petrol, 100f);
            var repaired = DentModel.ZoneDepths(list);
            Assert.IsFalse(DentModel.Any(repaired));
            Assert.AreNotEqual(damaged, DentModel.Signature(repaired));
        }

        [Test]
        public void SameVertex_GetsTheSameDentEveryTime()
        {
            var list = Car(100f);
            ComponentSet.Damage(list, ImpactZone.Front, 50f);
            var d = DentModel.ZoneDepths(list);
            Displace(d, 0.3f, 0.5f, 2.2f, out var a, out var b, out var c);
            Displace(d, 0.3f, 0.5f, 2.2f, out var a2, out var b2, out var c2);
            Assert.AreEqual(a, a2); Assert.AreEqual(b, b2); Assert.AreEqual(c, c2);
        }
    }
}
