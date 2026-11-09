using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Inspection (spec §6.5): a checklist of methods (walk-around, interior, under the bonnet, scanner, road test,
    /// lift, history check, full inspection). Each one reveals the faults it can find; the findings list shows only
    /// what you've found, with the symptom, fault code, MOT effect and a repair estimate.
    /// </summary>
    public class InspectionUI : UIPanel
    {
        CarInstance car;
        CarDisplay owned;   // the car's display when it stands somewhere (your car or a lot car), else null
        WorkshopManager.Context context;
        RawImage photo;
        TextMeshProUGUI nameText, overallText, claimText, notesText;
        RectTransform barsArea, checklist, findings;
        Button partsButton, handButton;
        readonly List<string> notes = new List<string>();

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Vehicle inspection", new Vector2(1400f, 840f), Back);
            UIFactory.Horizontal(body, 0, 26f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            var left = UIFactory.VCol(body, 10f, "Left", 430f);
            photo = UIFactory.CarPicture(left, 230f);
            nameText = UIFactory.Text(left, "", 24, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(nameText, h: 60);
            overallText = UIFactory.Text(left, "", 18, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(overallText, h: 48);
            claimText = UIFactory.Text(left, "", 16, UIFactory.Warn, TextAlignmentOptions.Left);
            UIFactory.LE(claimText, h: 24);
            barsArea = UIFactory.VCol(left, 6f, "Bars");
            UIFactory.LE(barsArea, flexH: 1);
            var buttons = UIFactory.HRow(left, 46f, 10f);
            partsButton = UIFactory.Btn(buttons, "All parts", UIFactory.Accent, () => CarSalesServices.UI.parts.Show(car, owned), 46f, 17f);
            handButton = UIFactory.Btn(buttons, "By hand", UIFactory.Warn, InspectByHand, 46f, 17f);
            UIFactory.Btn(buttons, "Back", UIFactory.Neutral, Back, 46f, 17f);

            var mid = UIFactory.VCol(body, 8f, "Checklist", 470f);
            UIFactory.SectionTitle(mid, "Checklist");
            checklist = UIFactory.ScrollList(mid, 6f, "Methods");
            notesText = UIFactory.Text(mid, "", 15, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            notesText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(notesText, h: 110);

            var right = UIFactory.VCol(body, 8f, "Findings");
            UIFactory.SectionTitle(right, "What you've found");
            findings = UIFactory.ScrollList(right, 6f, "Faults");
        }

        /// <summary>owner = the car's display (your car, or a car standing at the Used Car Market); null for an online listing.</summary>
        public void Show(CarInstance target, CarDisplay owner)
        {
            if (car != target) notes.Clear();
            car = target;
            owned = owner;
            context = WorkshopManager.ContextOf(car, owner);
            OpenSelf();
            CarParts.EnsureParts(car);
            Render();
        }

        bool ConditionKnown => context == WorkshopManager.Context.Owned || CarValueCalculator.IsConditionKnown(car);

        void Render()
        {
            var def = CarSalesServices.Database.Get(car.carId);
            UIFactory.SetCarPicture(photo, car);
            nameText.text = $"{def.year} {def.brand} {def.displayName}\n<size=65%><color={UIFactory.Hex(UIFactory.TextDim)}>{car.identity.plate}   -   {car.mileage:N0} km</color></size>";
            bool known = ConditionKnown;
            overallText.text = known
                ? $"Condition <color={UIFactory.Hex(CarParts.LabelColor(car.condition))}><b>{car.condition:0}% {CarParts.Label(car.condition)}</b></color>\nFaults found: {InspectionEngine.KnownCount(car.faults)}"
                : $"Seller says <b>{car.reportedCondition:0}%</b> - not verified\nFaults found: {InspectionEngine.KnownCount(car.faults)}";
            float lie = car.reportedCondition - car.condition;
            claimText.text = context != WorkshopManager.Context.Owned && known && lie >= 5f ? $"The seller claimed {lie:0}% better than reality!" : "";
            partsButton.interactable = known;
            handButton.interactable = owned && CarSalesServices.Workshop;

            UIFactory.ClearChildren(barsArea);
            if (known)
                foreach (var part in CarParts.All)
                {
                    float v = CarParts.Get(car, part);
                    var row = UIFactory.HRow(barsArea, 26, 8, part.ToString(), false);
                    var l = UIFactory.Text(row, CarParts.DisplayName(part), 15, UIFactory.TextMain, TextAlignmentOptions.Left, true);
                    UIFactory.LE(l, w: 120, flexW: 0);
                    var holder = UIFactory.VCol(row, 0f, "Bar");
                    holder.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    var bar = UIFactory.ProgressBar(holder, CarParts.LabelColor(v), 10f);
                    bar.Set(v / 100f);
                    var t = UIFactory.Text(row, $"{v:0}%", 15, CarParts.LabelColor(v), TextAlignmentOptions.Right, true);
                    UIFactory.LE(t, w: 50, flexW: 0);
                }
            else UIFactory.EmptyState(barsArea, "Condition not verified", "A full inspection shows every area's real condition.");

            RenderChecklist();
            RenderFindings();
            notesText.text = notes.Count == 0 ? "Work through the checklist. Each check finds different kinds of problems."
                                              : string.Join("\n", notes.Skip(Mathf.Max(0, notes.Count - 5)));
        }

        void RenderChecklist()
        {
            UIFactory.ClearChildren(checklist);
            var ws = CarSalesServices.Workshop;
            foreach (var info in InspectionEngine.Methods)
            {
                var m = info.Method;
                bool done = m == InspectMethod.History ? car.history.checkedHistory : car.knowledge.Done(m);
                string reason = "";
                bool usable = ws && ws.CanUse(m, car, context, out reason);
                string cost = info.Cost > 0 ? UIFactory.Money(info.Cost) : "free";
                string time = info.Minutes > 0 ? $"{info.Minutes} min" : "";
                MethodRow(info.Name, info.Description, done, done ? "Done" : usable ? $"Check ({cost}{(time != "" ? ", " + time : "")})" : reason,
                          usable && !done, () => Do(m));
            }
            int fee = CarValueCalculator.InspectionCost();
            MethodRow("Full inspection (garage)", "A mechanic checks everything except the history. Works on online listings too.",
                      car.inspected && car.knowledge.Done(InspectMethod.Lift), car.inspected && car.knowledge.Done(InspectMethod.Lift) ? "Done" : $"Book ({(fee > 0 ? UIFactory.Money(fee) : "free")})",
                      ws && !(car.inspected && car.knowledge.Done(InspectMethod.Lift)), Full);
        }

        void MethodRow(string title, string desc, bool done, string action, bool enabled, UnityEngine.Events.UnityAction onClick)
        {
            var row = UIFactory.Panel(checklist, title, done ? UIFactory.WithAlpha(UIFactory.Good, 0.18f) : UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: 66);
            var h = UIFactory.Horizontal(row, 8, 10f);
            h.padding = new RectOffset(10, 10, 6, 6);
            h.childForceExpandHeight = false;
            UIFactory.Icon(row.transform, done ? UIFactory.Good : UIFactory.Neutral, done ? "" : "?", 30f, done);
            var col = UIFactory.VCol(row.transform, 0f);
            UIFactory.Text(col, title, 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            var d = UIFactory.Text(col, desc, 13, UIFactory.TextDim, TextAlignmentOptions.Left);
            d.textWrappingMode = TextWrappingModes.Normal;
            var b = UIFactory.Btn(row.transform, action, done ? UIFactory.Good : enabled ? UIFactory.Accent : UIFactory.Neutral, onClick, 40f, 13f);
            UIFactory.LE(b, h: 44, w: 150, flexW: 0);
            b.interactable = enabled;
        }

        void RenderFindings()
        {
            UIFactory.ClearChildren(findings);
            var known = CarRecords.KnownFaults(car).OrderByDescending(f => f.severity).ToList();
            if (car.history.checkedHistory)
            {
                var h = car.history;
                if (h.writeOff != WriteOff.None) Finding(h.writeOff == WriteOff.CatN ? "Category N write-off" : "Category S write-off", "Recorded by the history check", "Lowers the value a lot", UIFactory.Bad);
                if (h.outstandingFinance && !h.financeSettled) Finding("Outstanding finance", "A lender can take the car back", "Settle it in the Workshop app (Paperwork)", UIFactory.Bad);
                if (h.clockingConfirmed) Finding("Clocked mileage", "An earlier MOT recorded more kilometres", "Lowers the value", UIFactory.Bad);
            }
            else if (car.history.clockingSuspected) Finding("Mileage looks wrong", "Interior wear doesn't match the odometer", "Run a history check to be sure", UIFactory.Warn);

            foreach (var f in known)
            {
                var def = f.Def;
                string mot = def.FailsMot(f.severity) ? "MOT FAIL" : def.AdvisesMot(f.severity) ? "MOT advisory" : "";
                string sub = def.Symptom + (string.IsNullOrEmpty(def.Dtc) ? "" : $"   code {def.Dtc}");
                string cost = $"Fix about {UIFactory.Money(CarRecords.RepairEstimate(car, f))}" + (mot != "" ? $"   -   {mot}" : "") + $"   -   {SeverityText(f.severity)}";
                Finding(def.Name, sub, cost, f.severity >= 0.6f || mot == "MOT FAIL" ? UIFactory.Bad : UIFactory.Warn);
            }
            if (findings.childCount == 0) UIFactory.EmptyState(findings, "Nothing found yet", "No news isn't always good news: some faults only show on the lift or a road test.");
        }

        static string SeverityText(float s) => s >= 0.75f ? "severe" : s >= 0.45f ? "moderate" : "mild";

        void Finding(string title, string sub, string detail, Color color)
        {
            var row = UIFactory.Panel(findings, title, UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: 84);
            var h = UIFactory.Horizontal(row, 8, 10f);
            h.padding = new RectOffset(0, 10, 0, 0);
            var stripe = UIFactory.Fill(row.transform, "Stripe", color);
            UIFactory.LE(stripe, w: 6, flexW: 0);
            var col = UIFactory.VCol(row.transform, 1f);
            col.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(6, 0, 8, 6);
            UIFactory.Text(col, title, 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            var s = UIFactory.Text(col, sub, 14, UIFactory.TextDim, TextAlignmentOptions.Left);
            s.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Text(col, detail, 13, color, TextAlignmentOptions.Left, true);
        }

        void Do(InspectMethod m)
        {
            var ws = CarSalesServices.Workshop;
            if (!ws) return;
            if (m == InspectMethod.History)
            {
                var lines = ws.RunHistoryCheck(car);
                if (lines != null) { notes.Add("<b>History check:</b> " + string.Join(" ", lines)); }
            }
            else
            {
                var r = ws.Inspect(car, m, context);
                if (r != null) Log(InspectionEngine.Info(m).Name, r);
            }
            Render();
        }

        void Full()
        {
            var ws = CarSalesServices.Workshop;
            if (!ws) return;
            var r = ws.FullInspection(car);
            if (r != null) Log("Full inspection", r);
            Render();
        }

        void Log(string name, InspectionResult r)
        {
            string found = r.found.Count == 0 ? "no new faults" : $"{r.found.Count} new: " + string.Join(", ", r.found.Select(f => f.Def.Name));
            notes.Add($"<b>{name}:</b> {found}. {string.Join(" ", r.notes.Where(n => n != "Nothing new found."))}");
        }

        /// <summary>Walk around the car and check each point yourself (on your lift: underneath too).</summary>
        void InspectByHand()
        {
            if (!owned || !CarSalesServices.Workshop) return;
            bool lift = WorkStation.BayOf(owned, StationType.Mechanical) != null;
            CarSalesServices.Workshop.Session.StartInspection(owned, lift);
        }

        void Back()
        {
            if (CarSalesServices.UI.CanGoBack) { GoBack(); return; }
            if (owned && context == WorkshopManager.Context.Owned) CarSalesServices.UI.carMenu.Show(owned);
            else CarSalesServices.UI.vehicleDetails.Reopen();
        }
    }
}
