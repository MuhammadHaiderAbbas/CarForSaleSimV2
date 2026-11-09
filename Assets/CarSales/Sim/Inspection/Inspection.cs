using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public sealed class MethodInfo
    {
        public InspectMethod Method;
        public string Name, Description;
        public int Minutes;
        public int Cost;
    }

    public sealed class InspectionResult
    {
        public readonly List<FaultInstance> found = new List<FaultInstance>();
        public readonly List<string> notes = new List<string>();
    }

    /// <summary>
    /// Inspection methods (spec §6.5). Each one can find the faults whose FoundBy includes it; skill (0-1) and the
    /// fault's severity decide the chance to notice it. Scanner and lift are certain.
    /// </summary>
    public static class InspectionEngine
    {
        public const int HistoryCheckCost = 15;
        public const int FullInspectionCost = 120;
        public const int LiftCost = 40;
        public const int ScannerPrice = 300;

        public static readonly MethodInfo[] Methods =
        {
            new MethodInfo { Method = InspectMethod.Visual, Name = "Walk-around", Minutes = 2, Description = "Panels, paint, glass, lights, tyres, smoke." },
            new MethodInfo { Method = InspectMethod.Interior, Name = "Interior check", Minutes = 2, Description = "Seats, smells, warning lights, wear vs. odometer." },
            new MethodInfo { Method = InspectMethod.UnderBonnet, Name = "Under the bonnet", Minutes = 3, Description = "Leaks, belts, fluids, the oil cap." },
            new MethodInfo { Method = InspectMethod.Scanner, Name = "OBD scanner", Minutes = 1, Description = "Reads stored fault codes. Needs a scanner (Workshop app)." },
            new MethodInfo { Method = InspectMethod.RoadTest, Name = "Road test", Minutes = 0, Description = "Drive at least 1 km: noises, slipping, pulling." },
            new MethodInfo { Method = InspectMethod.Lift, Name = "On the lift", Minutes = 15, Cost = LiftCost, Description = "Brakes, suspension, exhaust, rust (at the town workshop)." },
            new MethodInfo { Method = InspectMethod.History, Name = "History check", Minutes = 0, Cost = HistoryCheckCost, Description = "Write-offs, finance, mileage records." },
        };

        public static MethodInfo Info(InspectMethod m)
        {
            foreach (var i in Methods) if (i.Method == m) return i;
            return null;
        }

        /// <summary>Chance to notice a fault with this method.</summary>
        public static float NoticeChance(InspectMethod method, float severity, float skill)
        {
            if (method == InspectMethod.Scanner || method == InspectMethod.Lift) return 1f;
            if (method == InspectMethod.RoadTest) return severity < 0.25f ? 0.15f : Math.Min(1f, 0.7f + 0.3f * skill);
            return Math.Min(1f, 0.5f + 0.35f * skill + 0.3f * severity);
        }

        /// <summary>Runs one physical method and marks what it found as known.</summary>
        public static InspectionResult Run(InspectMethod method, List<ComponentState> components, List<FaultInstance> faults,
                                           HistoryRecord history, VehicleKnowledge knowledge, float skill, Random rng)
        {
            var result = new InspectionResult();
            knowledge?.Mark(method);
            if (faults != null)
                foreach (var f in faults)
                {
                    var def = f.Def;
                    if (f.known || def == null || (def.FoundBy & method) == 0) continue;
                    if (rng.NextDouble() >= NoticeChance(method, f.severity, skill)) continue;
                    f.known = true;
                    f.foundBy = (int)method;
                    result.found.Add(f);
                }

            switch (method)
            {
                case InspectMethod.Visual:
                    float tread = 0f; int n = 0;
                    foreach (var id in new[] { ComponentId.TyreFrontLeft, ComponentId.TyreFrontRight, ComponentId.TyreRearLeft, ComponentId.TyreRearRight })
                    {
                        var c = ComponentSet.Find(components, id);
                        if (c != null) { tread += TreadMm(c.health); n++; }
                    }
                    if (n > 0) result.notes.Add($"Tyres: about {tread / n:0.0} mm of tread left.");
                    break;
                case InspectMethod.Interior:
                    if (history != null && history.clocked && !history.clockingSuspected && rng.NextDouble() < 0.45 + 0.4 * skill)
                    {
                        history.clockingSuspected = true;
                        result.notes.Add("The pedals and seat are more worn than the mileage suggests. Clocked?");
                    }
                    break;
            }
            if (result.found.Count == 0 && result.notes.Count == 0) result.notes.Add("Nothing new found.");
            return result;
        }

        /// <summary>Workshop full inspection: every physical method at high skill.</summary>
        public static InspectionResult Full(List<ComponentState> components, List<FaultInstance> faults, HistoryRecord history,
                                            VehicleKnowledge knowledge, Random rng)
        {
            var all = new InspectionResult();
            foreach (var m in new[] { InspectMethod.Visual, InspectMethod.Interior, InspectMethod.UnderBonnet, InspectMethod.Scanner, InspectMethod.Lift, InspectMethod.RoadTest })
            {
                var r = Run(m, components, faults, history, knowledge, 0.9f, rng);
                all.found.AddRange(r.found);
                foreach (var note in r.notes) if (note != "Nothing new found.") all.notes.Add(note);
            }
            return all;
        }

        /// <summary>HPI-style history check: reveals write-offs, finance, plate changes, and clocking if records show it.</summary>
        public static List<string> HistoryCheck(DocumentSet docs, HistoryRecord h, int displayedMileage, int day, VehicleKnowledge knowledge)
        {
            var lines = new List<string>();
            knowledge?.Mark(InspectMethod.History);
            h.checkedHistory = true;
            h.checkedDay = day;
            lines.Add(h.writeOff == WriteOff.None ? "No insurance write-off recorded." :
                      h.writeOff == WriteOff.CatN ? "WRITE-OFF: Category N (non-structural damage, repaired)." :
                                                    "WRITE-OFF: Category S (structural damage, repaired).");
            lines.Add(h.outstandingFinance && !h.financeSettled ? "OUTSTANDING FINANCE: a lender still owns part of this car." : "No outstanding finance.");
            if (VehicleRecords.MileageAnomaly(docs, displayedMileage))
            {
                h.clockingConfirmed = true;
                lines.Add($"MILEAGE ANOMALY: an MOT recorded {VehicleRecords.HighestRecordedMileage(docs):N0} km, the odometer shows {displayedMileage:N0} km.");
            }
            else lines.Add(docs.motHistory.Count > 0 ? "Mileage readings are consistent." : "No mileage records yet (car too young for an MOT).");
            lines.Add(h.plateChanges > 0 ? "The registration plate has been changed before." : "Original registration plate.");
            lines.Add($"{docs.keepers} previous keeper{(docs.keepers == 1 ? "" : "s")}.");
            return lines;
        }

        /// <summary>Tyre health 0-100 → tread depth (8 mm new; UK legal minimum 1.6 mm).</summary>
        public static float TreadMm(float health) => Math.Max(0f, Math.Min(100f, health)) / 100f * 8f;

        public static int KnownCount(List<FaultInstance> faults)
        {
            int n = 0;
            if (faults != null) foreach (var f in faults) if (f.known) n++;
            return n;
        }
    }

    /// <summary>The annual roadworthiness test (spec §6.7). Results reveal the faults they mention.</summary>
    public static class MotTest
    {
        public const int Fee = 55;

        static void Check(List<ComponentState> comps, ComponentId id, float failBelow, float adviseBelow, string fail, string advise, MotRecord rec)
        {
            var c = ComponentSet.Find(comps, id);
            if (c == null) return;
            if (c.health < failBelow) rec.fails.Add(fail);
            else if (c.health < adviseBelow) rec.advisories.Add(advise);
        }

        public static MotRecord Run(List<ComponentState> comps, List<FaultInstance> faults, int mileage, int day, int year)
        {
            var rec = new MotRecord { day = day, year = year, mileage = mileage };
            foreach (var t in new[] { (ComponentId.TyreFrontLeft, "front left"), (ComponentId.TyreFrontRight, "front right"), (ComponentId.TyreRearLeft, "rear left"), (ComponentId.TyreRearRight, "rear right") })
                Check(comps, t.Item1, 20f, 37.5f, $"Tyre tread below 1.6 mm ({t.Item2})", $"Tyre close to the legal limit ({t.Item2})", rec);
            Check(comps, ComponentId.BrakePadsFront, 20f, 35f, "Front brake pads below 1.5 mm", "Front brake pads wearing thin", rec);
            Check(comps, ComponentId.BrakePadsRear, 20f, 35f, "Rear brake pads below 1.5 mm", "Rear brake pads wearing thin", rec);
            Check(comps, ComponentId.BrakeDiscs, 25f, 40f, "Brake discs excessively worn", "Brake discs worn or scored", rec);
            Check(comps, ComponentId.Lights, 40f, 55f, "Headlamp aim / lamp not working", "Lamp lens slightly damaged", rec);
            Check(comps, ComponentId.ShockAbsorbers, 25f, 40f, "Shock absorber leaking excessively", "Shock absorber has a light misting of oil", rec);
            Check(comps, ComponentId.BushesAndArms, 25f, 40f, "Suspension arm bush excessively worn", "Suspension bush deteriorating", rec);
            Check(comps, ComponentId.SteeringRack, 30f, 45f, "Steering rack excessive play", "Steering rack gaiter split", rec);
            Check(comps, ComponentId.Exhaust, 30f, 45f, "Exhaust emissions above the limit", "Exhaust has a minor leak", rec);
            Check(comps, ComponentId.Glass, 30f, 50f, "Windscreen damage in the driver's view", "Windscreen chip outside the driver's view", rec);
            if (faults != null)
                foreach (var f in faults)
                {
                    var def = f.Def;
                    if (def == null) continue;
                    if (def.FailsMot(f.severity)) { if (!rec.fails.Contains(def.Name)) rec.fails.Add(def.Name); }
                    else if (def.AdvisesMot(f.severity)) { if (!rec.advisories.Contains(def.Name)) rec.advisories.Add(def.Name); }
                    else continue;
                    if (!f.known) { f.known = true; f.foundBy = (int)InspectMethod.Lift; }
                }
            rec.passed = rec.fails.Count == 0;
            return rec;
        }
    }
}
