using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>Staff app: your team (role, skill, wage, morale), this week's candidates, and how salespeople may sell.</summary>
    public class StaffPageUI : ComputerPage
    {
        UIFactory.TabBar views;
        RectTransform teamView, hireView, rulesView, teamList, hireList;
        TextMeshProUGUI summary, marginText;
        Slider margin;
        Button handleButton;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 10f);
            summary = UIFactory.Text(Area, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(summary, h: 28);
            views = UIFactory.Tabs(Area, new[] { "Team", "Recruitment", "Rules" }, _ => Refresh(), 42f, 200f, 16f);
            var host = UIFactory.Rect("Views", Area);
            UIFactory.LE(host, flexH: 1, flexW: 1);
            teamView = UIFactory.Rect("Team", host); UIFactory.Stretch(teamView); UIFactory.Vertical(teamView, 0, 6f);
            teamList = UIFactory.ScrollList(teamView, 8f);
            hireView = UIFactory.Rect("Hire", host); UIFactory.Stretch(hireView); UIFactory.Vertical(hireView, 0, 6f);
            hireList = UIFactory.ScrollList(hireView, 8f);
            rulesView = UIFactory.Rect("Rules", host); UIFactory.Stretch(rulesView);
            UIFactory.Vertical(rulesView, 0, 10f);
            UIFactory.SectionTitle(rulesView, "Salespeople");
            marginText = UIFactory.Text(rulesView, "", 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(marginText, h: 26);
            margin = UIFactory.MakeSlider(rulesView, UIFactory.Accent, 30f);
            margin.minValue = 0f; margin.maxValue = 40f; margin.wholeNumbers = true;
            margin.onValueChanged.AddListener(v => { if (CarSalesServices.Staff) CarSalesServices.Staff.SetMinMargin(v); marginText.text = $"Minimum margin: {v:0}% over what the car cost you"; });
            handleButton = UIFactory.Btn(rulesView, "", UIFactory.CardLight, () => { var s = CarSalesServices.Staff; if (s) s.SetSalesHandle(!s.State.salesHandleCustomers); Refresh(); }, 44f, 15f);
            UIFactory.LE(handleButton, h: 44, w: 520, flexW: 0);
            var note = UIFactory.Text(rulesView, $"If a customer with a '!' waits {StaffManager.SalespersonDelay:0} seconds, a free salesperson takes over. Staff always tell buyers " +
                                                 "about the faults you know (no complaints), and never sell below your minimum. Wages are paid at the end of every week.", 15, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            note.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(note, h: 60);
            views.Select(0, false);
        }

        public override void Refresh()
        {
            var s = CarSalesServices.Staff;
            teamView.gameObject.SetActive(views.Index == 0);
            hireView.gameObject.SetActive(views.Index == 1);
            rulesView.gameObject.SetActive(views.Index == 2);
            if (!s) return;
            summary.text = $"{s.Employees.Count} staff  -  wages {UIFactory.Money(s.WeeklyWages)}/week" +
                           (s.Employees.Any(e => e.sickDay == CarRecords.Today) ? $"  -  <color={UIFactory.Hex(UIFactory.Warn)}>someone is off sick</color>" : "");
            if (views.Index == 0)
            {
                UIFactory.ClearChildren(teamList);
                foreach (var e in s.Employees) Person(teamList, e, hired: true);
                if (teamList.childCount == 0) UIFactory.EmptyState(teamList, "No staff yet", "Hire from Recruitment: new candidates every week.");
            }
            else if (views.Index == 1)
            {
                UIFactory.ClearChildren(hireList);
                foreach (var e in s.State.candidates.ToList()) Person(hireList, e, hired: false);
                if (hireList.childCount == 0) UIFactory.EmptyState(hireList, "No candidates left this week", "New people apply every week.");
            }
            else
            {
                margin.SetValueWithoutNotify(s.State.minMarginPct);
                marginText.text = $"Minimum margin: {s.State.minMarginPct:0}% over what the car cost you";
                handleButton.GetComponentInChildren<TextMeshProUGUI>().text = s.State.salesHandleCustomers ? "Salespeople talk to waiting customers: ON" : "Salespeople talk to waiting customers: OFF";
                UIFactory.SetButtonColor(handleButton, s.State.salesHandleCustomers ? UIFactory.Good : UIFactory.CardLight);
            }
        }

        void Person(Transform parent, Employee e, bool hired)
        {
            var row = UIFactory.Panel(parent, e.id, UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: 84);
            var h = UIFactory.Horizontal(row, 10, 12f);
            h.padding = new RectOffset(14, 12, 8, 8);
            h.childForceExpandHeight = false;
            var color = e.Role == StaffRole.Salesperson ? UIFactory.Good : e.Role == StaffRole.Mechanic ? UIFactory.Warn : e.Role == StaffRole.Detailer ? UIFactory.Info :
                        e.Role == StaffRole.Driver ? UIFactory.Special : e.Role == StaffRole.Receptionist ? UIFactory.Personal : UIFactory.Gold;
            UIFactory.Icon(row.transform, color, StaffRules.RoleName(e.Role).Substring(0, 1), 40f);
            var col = UIFactory.VCol(row.transform, 2f);
            UIFactory.Text(col, $"<b>{e.name}</b>  -  {StaffRules.RoleName(e.Role)}  -  skill {e.skill}  -  {UIFactory.Money(e.wage)}/week" +
                                (hired ? $"  -  morale {e.morale * 100f:0}%{(e.sickDay == CarRecords.Today ? "  -  OFF SICK" : "")}" : ""), 16, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.Text(col, StaffRules.RoleDuty(e.Role) + (hired && e.jobsDone > 0 ? $"  ({e.jobsDone} jobs done)" : ""), 13, UIFactory.TextDim, TextAlignmentOptions.Left);
            var bar = UIFactory.ProgressBar(col, color, 6f);
            bar.Set(e.skill / 100f);
            var emp = e;
            var b = hired
                ? UIFactory.Btn(row.transform, "Let go", UIFactory.Neutral, () => UIFactory.Confirm("Let go", $"Let {emp.name} go? You pay one week's notice ({UIFactory.Money(emp.wage)}).", "Let go", UIFactory.Bad, () => { CarSalesServices.Staff.Fire(emp); Refresh(); }), 42f, 14f)
                : UIFactory.Btn(row.transform, "Hire", UIFactory.Good, () => { CarSalesServices.Staff.Hire(emp); Refresh(); }, 42f, 15f);
            UIFactory.LE(b, h: 42, w: 120, flexW: 0);
        }
    }
}
