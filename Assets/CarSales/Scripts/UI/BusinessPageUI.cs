using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>Business app (spec §6.12): dealership level, your sites, marketing, insurance & running costs, analytics, property development.</summary>
    public class BusinessPageUI : ComputerPage
    {
        UIFactory.TabBar views;
        RectTransform list;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 10f);
            views = UIFactory.Tabs(Area, new[] { "Level", "Sites", "Marketing", "Insurance & costs", "Analytics", "Property" }, _ => Refresh(), 42f, 190f, 15f);
            list = UIFactory.ScrollList(Area, 8f);
            views.Select(0, false);
        }

        static BusinessManager B => CarSalesServices.Business;

        TextMeshProUGUI Spec(string label, string value)
        {
            var v = UIFactory.SpecRow(list, label, 32f);
            v.text = value;
            return v;
        }

        void Para(string text, float h = 40f)
        {
            var t = UIFactory.Text(list, text, 15, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(t, h: h);
        }

        RectTransform Row(string text, float h = 64f)
        {
            var row = UIFactory.Panel(list, "Row", UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: h);
            var hl = UIFactory.Horizontal(row, 10, 12f);
            hl.padding = new RectOffset(16, 12, 8, 8);
            var t = UIFactory.Text(row.transform, text, 16, UIFactory.TextMain, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(t, flexW: 1);
            return row.rectTransform;
        }

        void Button(RectTransform row, string label, Color c, UnityEngine.Events.UnityAction a, bool on = true, float w = 170f)
        {
            var b = UIFactory.Btn(row, label, c, () => { a(); Refresh(); }, 42f, 14f);
            UIFactory.LE(b, h: 42, w: w, flexW: 0);
            b.interactable = on;
        }

        public override void Refresh()
        {
            UIFactory.ClearChildren(list);
            if (!B) return;
            switch (views.Index)
            {
                case 1: Sites(); break;
                case 2: Marketing(); break;
                case 3: Costs(); break;
                case 4: Analytics(); break;
                case 5: Property(); break;
                default: Level(); break;
            }
        }

        void Level()
        {
            var (level, next) = B.LevelInfo();
            int shown = Mathf.Max(level, B.Level);
            UIFactory.SectionTitle(list, $"Level {shown}: {BusinessRules.LevelNames[shown]}");
            var bar = UIFactory.ProgressBar(list, UIFactory.Gold, 12f);
            bar.Set(shown / 6f);
            var i = B.Inputs();
            Spec("Cars sold", i.carsSold.ToString());
            Spec("Reputation", $"{i.reputation:0}");
            Spec("Staff", $"{i.staff} of {B.MaxStaff}");
            Spec("Dealerships owned", i.dealerships.ToString());
            Spec("Workshop jobs done", i.workshopJobs.ToString());
            Spec("Net worth", UIFactory.Money(i.netWorth));
            Spec("Next level", shown >= 6 ? "Top level reached" : next);
            UIFactory.SectionTitle(list, "What levels unlock");
            for (int l = 1; l <= 6; l++)
                Spec($"{l}. {BusinessRules.LevelNames[l]}", $"{BusinessRules.MaxStaff(l)} staff" + (l >= 5 ? ", luxury buyers at every site" : ""));
        }

        void Sites()
        {
            var today = CarRecords.Today;
            foreach (var d in DealershipManager.All.Where(x => x))
            {
                var cars = CarSalesServices.Inventory.OwnedCars.Where(c => c.location == CarLocation.Spot && c.dealershipId == d.dealershipId).ToList();
                int listed = cars.Count(c => c.state == CarState.Listed);
                string status = !d.IsOwned ? "not owned" : d.IsOpenNow ? "open" : "closed";
                var row = Row($"<b>{d.displayName}</b>  -  {status}\n<size=85%>{cars.Count}/{d.spots.Count} spots used  -  {listed} for sale  -  " +
                              $"utilities {UIFactory.Money(BusinessRules.Utilities(d.spots.Count))}/day, rates {UIFactory.Money(BusinessRules.BusinessRates(d.spots.Count))}/week</size>");
                var place = MapPlace.All.FirstOrDefault(p => p && p.placeName == d.displayName);
                if (place) Button(row, "GPS", UIFactory.Accent, () => GpsSystem.SetTarget(place), true, 90f);
            }
            int week = B.State.sales.Count(s => s.day > today - 7);
            Para($"Sales in the last 7 days: {week}. Buy more sites in Real Estate, or build new ones with the World Builder (editor).");
        }

        void Marketing()
        {
            Para("Campaigns bring more customers (and some bring a particular kind of buyer). Several at once still help, but each adds less. Charged every morning.", 44f);
            Spec("Customers now vs. normal", $"{B.FootfallFactor * 100f:0}% (reviews included)");
            Spec("Online enquiries vs. normal", $"{B.LeadsFactor * 100f:0}%");
            foreach (var c in BusinessRules.Campaigns)
            {
                var active = B.State.campaigns.FirstOrDefault(x => x.id == c.Id && x.untilDay >= CarRecords.Today);
                var row = Row($"<b>{c.Name}</b>  -  {UIFactory.Money(c.CostPerDay)}/day  -  +{c.Footfall * 100f:0}% customers" + (c.Leads > 0 ? $", +{c.Leads * 100f:0}% enquiries" : "") +
                              $"\n<size=85%>{c.Description}{(active != null ? $"  <color={UIFactory.Hex(UIFactory.Good)}>Running until day {active.untilDay}</color>" : "")}</size>");
                var id = c.Id;
                Button(row, "+7 days", UIFactory.Good, () => B.StartCampaign(id, 7), true, 110f);
                if (active != null) Button(row, "Stop", UIFactory.Neutral, () => B.StopCampaign(id), true, 90f);
            }
            if (CarSalesServices.World)
            {
                var row = Row($"<b>Open day</b>  -  {UIFactory.Money(WorldSim.OpenDayCost)} once  -  +60% customers tomorrow");
                Button(row, "Book", UIFactory.Gold, () => CarSalesServices.World.BookOpenDay(), true, 110f);
            }
        }

        void Costs()
        {
            UIFactory.SectionTitle(list, "Insurance (premium every week)");
            foreach (InsuranceTier t in System.Enum.GetValues(typeof(InsuranceTier)))
            {
                string cover = t == InsuranceTier.None ? "No cover: thefts and vandalism are all yours." :
                               t == InsuranceTier.Basic ? $"Stock theft and break-ins, excess {UIFactory.Money(BusinessRules.Excess(t))}." :
                               $"Also forecourt damage and vandalism, excess {UIFactory.Money(BusinessRules.Excess(t))}.";
                var row = Row($"<b>{t}</b>  -  {UIFactory.Money(B.WeeklyPremium(t))}/week{(B.State.Insurance == t ? $"  <color={UIFactory.Hex(UIFactory.Good)}>(your policy)</color>" : "")}\n<size=85%>{cover}</size>");
                var tt = t;
                Button(row, B.State.Insurance == t ? "Current" : "Choose", UIFactory.Accent, () => B.SetInsurance(tt), B.State.Insurance != t, 110f);
            }
            Para($"The premium follows your stock value and claims in the last year ({B.State.claimDays.Count(d => d >= CarRecords.Today - 364)}).");
            var sec = Row($"<b>CCTV & alarm</b>  -  {UIFactory.Money(BusinessRules.SecurityWeekly)}/week  -  60% fewer thefts and vandals, 15% off the insurance");
            Button(sec, B.Security ? "Cancel" : "Install", B.Security ? UIFactory.Neutral : UIFactory.Good, () => B.SetSecurity(!B.Security), true, 110f);
            UIFactory.SectionTitle(list, "Running costs");
            int util = DealershipManager.All.Where(d => d && d.IsOwned).Sum(d => BusinessRules.Utilities(d.spots.Count));
            int rates = DealershipManager.All.Where(d => d && d.IsOwned).Sum(d => BusinessRules.BusinessRates(d.spots.Count));
            Spec("Utilities", $"{UIFactory.Money(util)} a day");
            Spec("Business rates", $"{UIFactory.Money(rates)} a week");
            Spec("Staff wages", CarSalesServices.Staff ? $"{UIFactory.Money(CarSalesServices.Staff.WeeklyWages)} a week" : "-");
            Spec("Marketing", $"{UIFactory.Money(B.Running.Sum(c => c.CostPerDay))} a day");
            Spec("Overdraft", $"{UIFactory.Money(B.OverdraftLimit)} available (credit rating {B.CreditRating}/100)");
        }

        void Analytics()
        {
            var a = BusinessRules.Analyse(B.State.sales, CarSalesServices.Sourcing ? CarSalesServices.Sourcing.State.leads.Count + B.State.sales.Count(s => s.viaLead) : 0);
            UIFactory.SectionTitle(list, $"All recorded sales ({a.sales})");
            Spec("Revenue", UIFactory.Money(a.revenue));
            Spec("Profit", UIFactory.Money(a.profit));
            Spec("Average margin", $"{a.avgMarginPct:0.0}%");
            Spec("Average days to sell", $"{a.avgDaysToSell:0.0}");
            Spec("Online enquiries that bought", $"{a.leadConversion * 100f:0}%");
            UIFactory.SectionTitle(list, "Sales by buyer type");
            foreach (var kv in a.byPersona.OrderByDescending(x => x.Value))
                Spec(kv.Key < 0 ? "Wholesale / other" : Buyers.Get((PersonaId)kv.Key).Name, kv.Value.ToString());
            UIFactory.SectionTitle(list, "Sales by body type");
            foreach (var kv in a.byCategory.OrderByDescending(x => x.Value))
                Spec(((MarketCategory)kv.Key).ToString(), kv.Value.ToString());
        }

        void Property()
        {
            var props = CarSalesServices.Properties;
            if (!props) return;
            Para("Develop the investment properties you own (refurbish, extend): each level costs 40% of the price × the level and adds 30% to the nightly income (max 3).", 44f);
            foreach (var p in props.Properties.Where(x => x.type == PropertyType.Investment))
            {
                bool owned = props.Owns(p.id);
                int lvl = B.DevelopmentLevel(p.id);
                var row = Row($"<b>{p.displayName}</b>  -  {(owned ? $"level {lvl}, earns {UIFactory.Money(props.IncomeOf(p))} a night" : "not owned")}");
                var pp = p;
                if (owned && lvl < BusinessRules.MaxDevelopment)
                    Button(row, $"Develop {UIFactory.Money(BusinessRules.DevelopmentCost(p.price, lvl))}", UIFactory.Good, () => B.Develop(pp), true, 200f);
            }
        }
    }
}
