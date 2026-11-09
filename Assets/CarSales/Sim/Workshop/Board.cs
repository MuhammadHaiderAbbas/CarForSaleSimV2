using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum BoardColumn { WaitingForParts = 0, Ready = 1, InProgress = 2, Done = 3 }

    /// <summary>A planned repair on the Workshop Board (spec §6.6: job queue / kanban).</summary>
    [Serializable]
    public class BoardCard
    {
        public string id;
        public string job;
        public string carId;
        public int tier;
        public bool done;
        public int doneDay;
        public bool byMechanic;      // your mechanic is working on it
        public float hoursLeft = -1f;
    }

    /// <summary>Which column a card belongs in, and who can work on it. Pure (tests in BoardTests).</summary>
    public static class BoardLogic
    {
        public const int MechanicWage = 140;     // per day
        public const float MechanicSkill = 0.65f;
        public const float WorkStart = 8f, WorkEnd = 18f;

        public static BoardColumn ColumnOf(BoardCard c, PartsInventory inv, bool garageBusyOnCar)
        {
            if (c.done) return BoardColumn.Done;
            if (c.byMechanic || garageBusyOnCar) return BoardColumn.InProgress;
            var job = WorkshopCatalog.Job(c.job);
            if (job == null) return BoardColumn.WaitingForParts;
            return inv.HasPartsFor(job, (PartTier)c.tier) ? BoardColumn.Ready : BoardColumn.WaitingForParts;
        }

        /// <summary>Hours of work the mechanic does between two clock times (only during working hours).</summary>
        public static float WorkingHours(float fromHour, float toHour)
        {
            if (toHour <= fromHour) return 0f;
            float a = Math.Max(fromHour, WorkStart), b = Math.Min(toHour, WorkEnd);
            return Math.Max(0f, b - a);
        }

        /// <summary>Cards finished before today are cleared from the board.</summary>
        public static int ClearOldDone(List<BoardCard> cards, int today) => cards.RemoveAll(c => c.done && c.doneDay < today);
    }

    public enum CarArea { FrontWheels, RearWheels, Bonnet, Underside, Side, Interior }

    /// <summary>One "hold E" step of a hands-on job.</summary>
    public sealed class HandsOnStep
    {
        public string Label;
        public CarArea Area;
        public int Index;            // which socket in the area (bolt 1, 2...)
        public float Seconds;
    }

    /// <summary>
    /// Steps for doing a repair by hand at your own bay (spec §6.6 "Doing it yourself": hold to unbolt / fit):
    /// undo the bolts, swap the part, do the bolts up again. Fewer steps for small jobs.
    /// </summary>
    public static class HandsOn
    {
        public static CarArea AreaOf(RepairJobDef job)
        {
            var g = ComponentCatalog.Get(job.Components[0])?.Group ?? ComponentGroup.Engine;
            switch (job.Components[0])
            {
                case ComponentId.BrakePadsRear: return CarArea.RearWheels;
                case ComponentId.Exhaust: case ComponentId.Gearbox: case ComponentId.Clutch:
                case ComponentId.ShockAbsorbers: case ComponentId.Springs: case ComponentId.BushesAndArms:
                case ComponentId.QuarterPanelsRear: return CarArea.Underside;
            }
            switch (g)
            {
                case ComponentGroup.Brakes: case ComponentGroup.Tires: return CarArea.FrontWheels;
                case ComponentGroup.Body: return CarArea.Side;
                case ComponentGroup.Interior: return CarArea.Interior;
                default: return CarArea.Bonnet;
            }
        }

        public static int BoltsFor(RepairJobDef job) => job.Hours <= 1f ? 2 : job.Hours <= 4f ? 3 : 4;

        public static List<HandsOnStep> Steps(RepairJobDef job)
        {
            var area = AreaOf(job);
            int bolts = BoltsFor(job);
            var steps = new List<HandsOnStep>();
            string fixing = area == CarArea.Interior ? "clip" : area == CarArea.Side ? "panel screw" : "bolt";
            for (int i = 0; i < bolts; i++) steps.Add(new HandsOnStep { Label = $"Undo {fixing} {i + 1}", Area = area, Index = i, Seconds = 0.8f });
            steps.Add(new HandsOnStep { Label = $"Remove the old part and fit the new one ({job.Name})", Area = area, Index = bolts, Seconds = 2f + Math.Min(3f, job.Hours * 0.3f) });
            for (int i = 0; i < bolts; i++) steps.Add(new HandsOnStep { Label = $"Tighten {fixing} {i + 1}", Area = area, Index = i, Seconds = 0.8f });
            return steps;
        }

        /// <summary>Hands-on work is quicker than the garage estimate and half as risky as quick DIY.</summary>
        public const float TimeFactor = 0.8f;
        public const float RiskFactor = 0.5f;
    }

    /// <summary>A point on the car to check during a hands-on inspection.</summary>
    public sealed class InspectPoint
    {
        public string Label;
        public CarArea Area;
        public int Index;
        public InspectMethod Method;
        public float Seconds;
    }

    public static class HandsOnInspection
    {
        /// <summary>
        /// The walk-around needs all four wheels and the rear; interior = the door; under the bonnet = the bonnet;
        /// the OBD port (with a scanner) is inside the door; underneath only on a lift.
        /// </summary>
        public static List<InspectPoint> Points(bool hasScanner, bool onLift)
        {
            var list = new List<InspectPoint>
            {
                new InspectPoint { Label = "Check the front left tyre and panels", Area = CarArea.FrontWheels, Index = 0, Method = InspectMethod.Visual, Seconds = 1.2f },
                new InspectPoint { Label = "Check the front right tyre and panels", Area = CarArea.FrontWheels, Index = 1, Method = InspectMethod.Visual, Seconds = 1.2f },
                new InspectPoint { Label = "Check the rear left tyre and panels", Area = CarArea.RearWheels, Index = 0, Method = InspectMethod.Visual, Seconds = 1.2f },
                new InspectPoint { Label = "Check the rear right tyre and panels", Area = CarArea.RearWheels, Index = 1, Method = InspectMethod.Visual, Seconds = 1.2f },
                new InspectPoint { Label = "Open the bonnet: belts, leaks, oil cap", Area = CarArea.Bonnet, Index = 0, Method = InspectMethod.UnderBonnet, Seconds = 2f },
                new InspectPoint { Label = "Sit inside: seats, smell, warning lights", Area = CarArea.Interior, Index = 0, Method = InspectMethod.Interior, Seconds = 2f },
            };
            if (hasScanner) list.Add(new InspectPoint { Label = "Plug the scanner into the OBD port", Area = CarArea.Interior, Index = 1, Method = InspectMethod.Scanner, Seconds = 1.5f });
            if (onLift) list.Add(new InspectPoint { Label = "Look underneath (lift)", Area = CarArea.Underside, Index = 0, Method = InspectMethod.Lift, Seconds = 3f });
            return list;
        }

        /// <summary>A method counts as done once all of its points are checked.</summary>
        public static bool MethodComplete(List<InspectPoint> points, HashSet<int> doneIndices, InspectMethod m)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i].Method == m && !doneIndices.Contains(i)) return false;
            return true;
        }
    }
}
