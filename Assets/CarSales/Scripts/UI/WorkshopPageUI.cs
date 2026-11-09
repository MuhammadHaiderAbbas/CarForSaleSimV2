using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Workshop app (spec §6.6/§6.7): repair jobs for your cars (part quality, supplier, garage or do-it-yourself),
    /// spare-parts storage and orders, jobs in progress, and MOT tests and paperwork (logbook, keys, finance).
    /// </summary>
    public class WorkshopPageUI : ComputerPage
    {
        static readonly string[] TierNames = { "Used", "Aftermarket", "OEM" };
        static readonly string[] SupplierNames = { "Motor factor", "Online -15%, tomorrow", "Breaker's yard (used)", "Counter -10%" };
        static readonly string[] ColumnNames = { "Waiting for parts", "Ready to start", "In progress", "Done today" };

        UIFactory.TabBar views, tiers, suppliers;
        RectTransform carList, repairs, parts, jobs, papers, repairsView, partsView, jobsView, papersView, boardView, staffRow;
        readonly RectTransform[] columns = new RectTransform[4];
        TextMeshProUGUI staffText;
        Button hireButton;
        string boardFilterCar;
        TextMeshProUGUI carHeader;
        string selectedId;
        bool showAll;
        PartTier tier = PartTier.Aftermarket;
        Supplier supplier = Supplier.MotorFactor;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 10f);
            views = UIFactory.Tabs(Area, new[] { "Board", "Repairs", "Parts", "Garage jobs", "MOT & paperwork" }, _ => Refresh(), 42f, 190f, 16f);
            var host = UIFactory.Rect("Views", Area);
            UIFactory.LE(host, flexH: 1, flexW: 1);

            // Board (kanban)
            boardView = View(host, "Board");
            UIFactory.Vertical(boardView, 0, 10f);
            var staff = UIFactory.Panel(boardView, "Staff", UIFactory.CardBg, 1.8f);
            staffRow = staff.rectTransform;
            UIFactory.LE(staff, h: 62);
            var sh = UIFactory.Horizontal(staff, 10, 12f);
            sh.padding = new RectOffset(16, 12, 8, 8);
            staffText = UIFactory.Text(staff.transform, "", 16, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.LE(staffText, flexW: 1);
            hireButton = UIFactory.Btn(staff.transform, "Hire", UIFactory.Good, ToggleMechanic, 42f, 15f);
            UIFactory.LE(hireButton, h: 42, w: 200, flexW: 0);
            var cols = UIFactory.Rect("Columns", boardView);
            UIFactory.LE(cols, flexH: 1, flexW: 1);
            UIFactory.Horizontal(cols, 0, 12f, TextAnchor.UpperLeft, true).childForceExpandHeight = true;
            for (int i = 0; i < 4; i++)
            {
                var col = UIFactory.Panel(cols, ColumnNames[i], UIFactory.WithAlpha(UIFactory.CardBg, 0.5f), 1.6f);
                UIFactory.LE(col, flexW: 1, flexH: 1);
                var cv = UIFactory.Vertical(col, 10, 6f);
                cv.padding = new RectOffset(10, 10, 10, 10);
                UIFactory.SectionTitle(col.transform, ColumnNames[i]);
                columns[i] = UIFactory.ScrollList(col.transform, 6f, "Cards");
            }

            // Repairs
            repairsView = View(host, "Repairs");
            UIFactory.Horizontal(repairsView, 0, 16f, TextAnchor.UpperLeft).childForceExpandHeight = true;
            var left = UIFactory.VCol(repairsView, 6f, "Cars", 300f);
            UIFactory.SectionTitle(left, "Your cars");
            carList = UIFactory.ScrollList(left, 6f, "CarList");
            var right = UIFactory.VCol(repairsView, 8f, "Jobs");
            carHeader = UIFactory.Text(right, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(carHeader, h: 52);
            var opts = UIFactory.HRow(right, 38, 10, "Options", false);
            tiers = UIFactory.Tabs(opts, TierNames, i => { tier = (PartTier)i; Refresh(); }, 36f, 120f, 14f);
            suppliers = UIFactory.Tabs(opts, SupplierNames, i => { supplier = (Supplier)i; Refresh(); }, 36f, 170f, 13f);
            var allBtn = UIFactory.Btn(right, "Show all jobs for this car", UIFactory.CardLight, () => { showAll = !showAll; Refresh(); }, 32f, 14f);
            UIFactory.LE(allBtn, h: 32, w: 260, flexW: 0);
            repairs = UIFactory.ScrollList(right, 6f, "JobList");

            partsView = View(host, "Parts");
            UIFactory.Vertical(partsView, 0, 8f);
            parts = UIFactory.ScrollList(partsView, 6f, "PartList");

            jobsView = View(host, "Jobs");
            UIFactory.Vertical(jobsView, 0, 8f);
            jobs = UIFactory.ScrollList(jobsView, 6f, "RunningJobs");

            papersView = View(host, "Papers");
            UIFactory.Vertical(papersView, 0, 8f);
            papers = UIFactory.ScrollList(papersView, 6f, "PaperList");

            views.Select(0, false);
            tiers.Select((int)tier, false);
            suppliers.SetInteractable(3, false);
            suppliers.Select((int)supplier, false);
        }

        static RectTransform View(Transform host, string name)
        {
            var v = UIFactory.Rect(name, host);
            UIFactory.Stretch(v);
            return v;
        }

        void OnEnable()
        {
            if (CarSalesServices.Workshop) CarSalesServices.Workshop.OnChanged += RefreshIfShown;
        }

        void OnDisable()
        {
            if (CarSalesServices.Workshop) CarSalesServices.Workshop.OnChanged -= RefreshIfShown;
        }

        void RefreshIfShown() { if (isActiveAndEnabled) Refresh(); }

        static WorkshopManager WS => CarSalesServices.Workshop;
        static List<CarInstance> Cars => CarSalesServices.Inventory ? CarSalesServices.Inventory.OwnedCars : new List<CarInstance>();

        public override void Refresh()
        {
            if (views == null) return;
            boardView.gameObject.SetActive(views.Index == 0);
            repairsView.gameObject.SetActive(views.Index == 1);
            partsView.gameObject.SetActive(views.Index == 2);
            jobsView.gameObject.SetActive(views.Index == 3);
            papersView.gameObject.SetActive(views.Index == 4);
            if (!WS) return;
            int running = WS.Jobs.Count;
            views.SetLabel(3, running > 0 ? $"Garage jobs ({running})" : "Garage jobs");
            bool counter = WS.AtCounter;
            suppliers.SetInteractable(3, counter);
            if (!counter && supplier == Supplier.Counter) { supplier = Supplier.MotorFactor; suppliers.Select(0, false); }
            switch (views.Index)
            {
                case 1: RenderRepairs(); break;
                case 2: RenderParts(); break;
                case 3: RenderJobs(); break;
                case 4: RenderPapers(); break;
                default: RenderBoard(); break;
            }
        }

        // ------------------------------------------------------------------ REPAIRS
        void RenderRepairs()
        {
            UIFactory.ClearChildren(carList);
            var cars = Cars;
            if (cars.Count == 0)
            {
                UIFactory.EmptyState(carList, "No cars yet", "Buy a car first.");
                carHeader.text = "";
                UIFactory.ClearChildren(repairs);
                return;
            }
            if (selectedId == null || !cars.Exists(c => c.instanceId == selectedId)) selectedId = cars[0].instanceId;
            foreach (var c in cars)
            {
                var car = c;
                CarParts.EnsureParts(car);
                int known = InspectionEngine.KnownCount(car.faults);
                var b = UIFactory.Btn(carList, $"{car.Name}\n<size=75%>{car.condition:0}%  -  {known} fault{(known == 1 ? "" : "s")} found{(WS.JobOn(car) != null ? "  -  in the garage" : "")}</size>",
                                      car.instanceId == selectedId ? UIFactory.Accent : UIFactory.CardLight, () => { selectedId = car.instanceId; Refresh(); }, out var label, 54f, 15f);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(12f, 0f, 6f, 0f);
            }

            var sel = cars.Find(c => c.instanceId == selectedId);
            var running = WS.JobOn(sel);
            carHeader.text = $"{sel.Name}  <size=75%><color={UIFactory.Hex(UIFactory.TextDim)}>{sel.identity.plate}  -  condition {sel.condition:0}%  -  " +
                             $"skill {WS.Skill * 100f:0}%{(running != null ? $"  -  garage busy: {WorkshopCatalog.Job(running.job)?.Name} ({running.hoursLeft:0.0} h left)" : "")}</color></size>";

            UIFactory.ClearChildren(repairs);
            var jobsForCar = showAll
                ? WorkshopCatalog.Jobs.Where(j => j.Components.Any(id => ComponentSet.Find(sel.components, id) != null)).ToList()
                : WorkshopCatalog.Suggested(sel.components, sel.faults);
            if (jobsForCar.Count == 0) UIFactory.EmptyState(repairs, "Nothing needs doing", "No worn parts or known faults. Inspect the car to find hidden ones.");
            foreach (var job in jobsForCar) JobRow(sel, job, running != null);
        }

        void JobRow(CarInstance car, RepairJobDef job, bool garageBusy)
        {
            var row = UIFactory.Panel(repairs, job.Id, UIFactory.CardBg, 1.8f);
            UIFactory.LE(row, h: 92);
            var h = UIFactory.Horizontal(row, 8, 10f);
            h.padding = new RectOffset(12, 10, 8, 8);
            h.childForceExpandHeight = false;
            var col = UIFactory.VCol(row.transform, 2f);
            UIFactory.Text(col, job.Name, 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            var health = string.Join(", ", job.Components.Select(id => (id, s: ComponentSet.Find(car.components, id))).Where(x => x.s != null)
                                                .Select(x => $"{ComponentCatalog.Get(x.id).Name} {x.s.health:0}%").Take(3));
            var faults = CarRecords.KnownFaults(car).Where(f => f.Def != null && job.Components.Contains(f.Def.Component)).Select(f => f.Def.Name).ToList();
            UIFactory.Text(col, health + (faults.Count > 0 ? $"   <color={UIFactory.Hex(UIFactory.Bad)}>{string.Join(", ", faults)}</color>" : ""), 13, UIFactory.TextDim, TextAlignmentOptions.Left);

            var missing = WS.Missing(job, tier);
            bool hasParts = missing.Count == 0;
            int price = missing.Sum(m => WS.PartPrice(m.part, tier, supplier, car) * m.count);
            bool available = missing.All(m => WS.Available(supplier, m.part, tier));
            string partsText = job.Parts.Length == 0 ? "No parts needed" : hasParts ? $"<color={UIFactory.Hex(UIFactory.Good)}>Parts in storage ({TierNames[(int)tier]})</color>"
                             : available ? $"Parts {UIFactory.Money(price)} from {WorkshopManager.SupplierName(supplier)}" : $"<color={UIFactory.Hex(UIFactory.Warn)}>{WorkshopManager.SupplierName(supplier)} can't supply this ({TierNames[(int)tier]})</color>";
            UIFactory.Text(col, $"{partsText}   -   labour {job.Hours:0.#} h ({UIFactory.Money(WorkshopManager.LabourCost(job))} at the garage)", 13, UIFactory.TextMain, TextAlignmentOptions.Left);

            var order = UIFactory.Btn(row.transform, hasParts ? "In stock" : "Order parts", UIFactory.Accent, () => WS.OrderParts(car, job, tier, supplier), 40f, 14f);
            UIFactory.LE(order, h: 40, w: 120, flexW: 0);
            order.interactable = !hasParts && available;
            var plan = UIFactory.Btn(row.transform, "Add to\nboard", UIFactory.CardLight, () => WS.AddToBoard(car, job, tier), 40f, 13f);
            UIFactory.LE(plan, h: 44, w: 80, flexW: 0);
            var garage = UIFactory.Btn(row.transform, $"Town garage\n<size=80%>{UIFactory.Money(WorkshopManager.LabourCost(job))}</size>", UIFactory.Good, () => WS.StartGarageJob(car, job, tier), 40f, 13f);
            UIFactory.LE(garage, h: 44, w: 110, flexW: 0);
            garage.interactable = hasParts && !garageBusy;
            var hand = UIFactory.Btn(row.transform, $"By hand (bay)\n<size=80%>{job.Hours * HandsOn.TimeFactor:0.#} h, {WorkshopCatalog.DiyFailChance(WS.Skill) * HandsOn.RiskFactor * 100f:0}% risk</size>", UIFactory.Warn, () => WS.StartHandsOnRepair(car, job, tier), 40f, 12f);
            UIFactory.LE(hand, h: 44, w: 120, flexW: 0);
            hand.interactable = hasParts && !garageBusy;
            var diy = UIFactory.Btn(row.transform, $"Quick DIY\n<size=80%>{job.Hours * 1.2f:0.#} h, {WorkshopCatalog.DiyFailChance(WS.Skill) * 100f:0}% risk</size>", UIFactory.Neutral, () => WS.DoItYourself(car, job, tier), 40f, 12f);
            UIFactory.LE(diy, h: 44, w: 110, flexW: 0);
            diy.interactable = hasParts && !garageBusy;
        }

        // ------------------------------------------------------------------ BOARD
        /// <summary>Opens the Workshop app on the board (optionally only one car's cards).</summary>
        public void ShowBoard(string carId = null)
        {
            boardFilterCar = carId;
            Show();
            views.Select(0, false);
            Refresh();
        }

        /// <summary>Opened from the Parts Direct counter: Repairs with counter prices.</summary>
        public void ShowCounter()
        {
            Show();
            views.Select(1, false);
            supplier = Supplier.Counter;
            suppliers.SetInteractable(3, true);
            suppliers.Select(3, false);
            Refresh();
        }

        /// <summary>Phase 7: mechanics are hired in the Staff app.</summary>
        void ToggleMechanic() => CarSalesServices.UI.computerHub.ShowPage(CarSalesServices.UI.computerHub.Staff);

        void RenderBoard()
        {
            bool centre = WorkStation.Exists(StationType.Mechanical);
            var working = WS.Board.FirstOrDefault(b => b.byMechanic && !b.done);
            var staffMgr = CarSalesServices.Staff;
            int mechs = staffMgr ? staffMgr.MechanicCount : 0;
            staffText.text = mechs > 0
                ? $"<b>{mechs} mechanic{(mechs == 1 ? "" : "s")} at work</b> (best skill {staffMgr.MechanicSkill})  -  " + (working != null ? $"working on {WorkshopCatalog.Job(working.job)?.Name} ({working.hoursLeft:0.0} h left)" : "waiting for a Ready job")
                  + (centre ? "" : $"\n<color={UIFactory.Hex(UIFactory.Warn)}>No service centre in this town yet: run Build Everything.</color>")
                : WS.MechanicHired
                ? $"<b>Mechanic</b>  -  {UIFactory.Money(BoardLogic.MechanicWage)}/day  -  " + (working != null ? $"working on {WorkshopCatalog.Job(working.job)?.Name} ({working.hoursLeft:0.0} h left)" : "waiting for a Ready job")
                  + (centre ? "" : $"\n<color={UIFactory.Hex(UIFactory.Warn)}>No service centre in this town yet: run Build Everything.</color>")
                : "<b>No mechanic</b>  -  do the jobs by hand at your bays, send them to the town garage, or hire a mechanic.";
            hireButton.GetComponentInChildren<TextMeshProUGUI>().text = "Staff app";
            UIFactory.SetButtonColor(hireButton, UIFactory.Accent);

            foreach (var c in columns) UIFactory.ClearChildren(c);
            var cards = WS.Board.Where(b => boardFilterCar == null || b.carId == boardFilterCar).ToList();
            foreach (var card in cards) Card(card, WS.ColumnOf(card));
            for (int i = 0; i < 4; i++)
                if (columns[i].childCount == 0) UIFactory.EmptyState(columns[i], i == 0 ? "Plan jobs from Repairs" : "-", null);
            if (boardFilterCar != null)
            {
                var b = UIFactory.Btn(columns[0], "Show every car", UIFactory.CardLight, () => { boardFilterCar = null; Refresh(); }, 32f, 13f);
                b.transform.SetAsFirstSibling();
            }
        }

        void Card(BoardCard card, BoardColumn column)
        {
            var car = Cars.Find(c => c.instanceId == card.carId);
            var job = WorkshopCatalog.Job(card.job);
            if (job == null) return;
            var parent = columns[(int)column];
            var p = UIFactory.Panel(parent, card.id, UIFactory.CardBg, 1.8f);
            var v = UIFactory.Vertical(p, 10, 4f);
            v.padding = new RectOffset(12, 12, 10, 10);
            p.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIFactory.Text(p.transform, $"<b>{job.Name}</b>", 15, UIFactory.TextMain, TextAlignmentOptions.Left).textWrappingMode = TextWrappingModes.Normal;
            var running = WS.Jobs.FirstOrDefault(j => j.carId == card.carId && j.job == card.job);
            string status = column == BoardColumn.InProgress ? (card.byMechanic ? $"Your mechanic: {card.hoursLeft:0.0} h left" : running != null ? $"Town garage: {running.hoursLeft:0.0} h left" : "") :
                            column == BoardColumn.Done ? "Finished" :
                            column == BoardColumn.WaitingForParts ? string.Join(", ", WS.Missing(job, (PartTier)card.tier).Select(m => $"{m.count} x {WorkshopCatalog.Part(m.part)?.Name}")) : "Parts in storage";
            UIFactory.Text(p.transform, $"{car?.Name ?? "(car sold)"}  -  {TierNames[card.tier]}\n<color={UIFactory.Hex(UIFactory.TextDim)}>{status}</color>", 13, UIFactory.TextMain, TextAlignmentOptions.Left).textWrappingMode = TextWrappingModes.Normal;
            if (car == null || column == BoardColumn.Done || column == BoardColumn.InProgress) return;
            var row = UIFactory.HRow(p.transform, 34, 6, "Buttons");
            var t = (PartTier)card.tier;
            if (column == BoardColumn.WaitingForParts)
            {
                bool ok = WS.Missing(job, t).All(m => WS.Available(supplier, m.part, t));
                var order = UIFactory.Btn(row, ok ? $"Order ({WorkshopManager.SupplierName(supplier)})" : "Not available", UIFactory.Accent, () => WS.OrderParts(car, job, t, supplier), 34f, 12f);
                order.interactable = ok;
            }
            else
            {
                UIFactory.Btn(row, "By hand", UIFactory.Warn, () => WS.StartHandsOnRepair(car, job, t), 34f, 12f);
                UIFactory.Btn(row, "Garage", UIFactory.Good, () => WS.StartGarageJob(car, job, t), 34f, 12f);
            }
            var x = UIFactory.Btn(row, "X", UIFactory.Neutral, () => WS.RemoveCard(card), 34f, 12f);
            UIFactory.LE(x, h: 34, w: 34, flexW: 0);
        }

        // ------------------------------------------------------------------ PARTS
        void RenderParts()
        {
            UIFactory.ClearChildren(parts);
            var scanner = UIFactory.Panel(parts, "Scanner", UIFactory.CardBg, 1.8f);
            UIFactory.LE(scanner, h: 70);
            var sh = UIFactory.Horizontal(scanner, 10, 12f);
            sh.padding = new RectOffset(14, 12, 8, 8);
            var st = UIFactory.Text(scanner.transform, WS.HasScanner ? "<b>OBD scanner</b>\nYou own one: use it in any inspection."
                                                                     : "<b>OBD scanner</b>\nReads stored fault codes (misfires, catalyst, sensors...).", 16, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.LE(st, flexW: 1);
            var buy = UIFactory.Btn(scanner.transform, WS.HasScanner ? "Owned" : $"Buy {UIFactory.Money(InspectionEngine.ScannerPrice)}", UIFactory.Accent, () => WS.BuyScanner(), 44f, 15f);
            UIFactory.LE(buy, h: 44, w: 150, flexW: 0);
            buy.interactable = !WS.HasScanner;

            UIFactory.SectionTitle(parts, "In storage");
            var inv = WS.Inventory;
            if (inv.stock.Count == 0) UIFactory.EmptyState(parts, "Storage is empty", "Order parts from the Repairs tab.");
            foreach (var s in inv.stock.OrderBy(x => x.part))
                Line(parts, $"{s.count} x {WorkshopCatalog.Part(s.part)?.Name ?? s.part}", TierNames[s.tier]);
            UIFactory.SectionTitle(parts, "On order");
            if (inv.orders.Count == 0) UIFactory.EmptyState(parts, "Nothing on order", null);
            foreach (var o in inv.orders)
                Line(parts, $"{o.count} x {WorkshopCatalog.Part(o.part)?.Name ?? o.part} ({TierNames[o.tier]})", $"arrives day {o.arrivesDay}");
        }

        static void Line(Transform parent, string left, string right)
        {
            var row = UIFactory.Panel(parent, "Line", UIFactory.CardBg, 2.5f);
            UIFactory.LE(row, h: 36);
            var h = UIFactory.Horizontal(row, 0, 8f);
            h.padding = new RectOffset(14, 14, 0, 0);
            var l = UIFactory.Text(row.transform, left, 16, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.LE(l, flexW: 1);
            var r = UIFactory.Text(row.transform, right, 15, UIFactory.TextDim, TextAlignmentOptions.Right, true);
            UIFactory.LE(r, w: 220, flexW: 0);
        }

        // ------------------------------------------------------------------ JOBS
        void RenderJobs()
        {
            UIFactory.ClearChildren(jobs);
            if (WS.Jobs.Count == 0) { UIFactory.EmptyState(jobs, "The garage isn't working on any of your cars", "Book a job from the Repairs tab. Jobs progress while the clock runs."); return; }
            foreach (var j in WS.Jobs)
            {
                var car = Cars.Find(c => c.instanceId == j.carId);
                var def = WorkshopCatalog.Job(j.job);
                var row = UIFactory.Panel(jobs, "Job", UIFactory.CardBg, 1.8f);
                UIFactory.LE(row, h: 72);
                var v = UIFactory.Vertical(row, 12, 6f);
                v.padding = new RectOffset(16, 16, 10, 10);
                UIFactory.Text(row.transform, $"<b>{def?.Name ?? j.job}</b>  -  {car?.Name ?? "?"}  -  {TierNames[j.tier]} parts  -  {j.hoursLeft:0.0} h left", 16, UIFactory.TextMain, TextAlignmentOptions.Left);
                var bar = UIFactory.ProgressBar(row.transform, UIFactory.Accent, 10f);
                bar.Set(j.hoursTotal > 0f ? 1f - j.hoursLeft / j.hoursTotal : 1f);
            }
        }

        // ------------------------------------------------------------------ MOT & PAPERWORK
        void RenderPapers()
        {
            UIFactory.ClearChildren(papers);
            var cars = Cars;
            if (cars.Count == 0) { UIFactory.EmptyState(papers, "No cars yet", null); return; }
            foreach (var c in cars)
            {
                var car = c;
                CarParts.EnsureParts(car);
                var d = car.documents;
                var row = UIFactory.Panel(papers, car.Name, UIFactory.CardBg, 1.8f);
                UIFactory.LE(row, h: 80);
                var h = UIFactory.Horizontal(row, 8, 10f);
                h.padding = new RectOffset(14, 10, 8, 8);
                h.childForceExpandHeight = false;
                bool motOk = CarRecords.MotValid(car);
                var info = UIFactory.Text(row.transform,
                    $"<b>{car.Name}</b>  {car.identity.plate}\n" +
                    $"<size=85%>MOT: <color={UIFactory.Hex(motOk ? UIFactory.Good : UIFactory.Bad)}>{CarRecords.MotText(car)}</color>   " +
                    $"Logbook: {(d.v5cPresent ? "yes" : d.v5cArrivesDay > 0 ? $"arrives day {d.v5cArrivesDay}" : $"<color={UIFactory.Hex(UIFactory.Bad)}>missing</color>")}   " +
                    $"Keys: {d.keys}   {CarRecords.ServiceText(d)}</size>", 16, UIFactory.TextMain, TextAlignmentOptions.Left);
                UIFactory.LE(info, flexW: 1);

                var mot = UIFactory.Btn(row.transform, $"MOT test\n<size=80%>{UIFactory.Money(MotTest.Fee)}</size>", UIFactory.Accent, () => Mot(car), 44f, 14f);
                UIFactory.LE(mot, h: 48, w: 110, flexW: 0);
                var v5c = UIFactory.Btn(row.transform, $"Logbook\n<size=80%>{UIFactory.Money(WorkshopManager.V5cFee)}</size>", UIFactory.Neutral, () => WS.OrderLogbook(car), 44f, 14f);
                UIFactory.LE(v5c, h: 48, w: 100, flexW: 0);
                v5c.interactable = !d.v5cPresent && d.v5cArrivesDay <= 0;
                var key = UIFactory.Btn(row.transform, $"New key\n<size=80%>{UIFactory.Money(WorkshopManager.LocksmithFee)}</size>", UIFactory.Neutral, () => WS.CutKey(car), 44f, 14f);
                UIFactory.LE(key, h: 48, w: 100, flexW: 0);
                key.interactable = d.keys < 2;
                int owed = car.history.checkedHistory ? WS.FinanceOwed(car) : 0;
                var fin = UIFactory.Btn(row.transform, owed > 0 ? $"Settle finance\n<size=80%>{UIFactory.Money(owed)}</size>" : "Finance\n<size=80%>none known</size>",
                                        owed > 0 ? UIFactory.Bad : UIFactory.Neutral, () => UIFactory.Confirm("Settle finance", $"Pay {UIFactory.Money(owed)} to the lender so the {car.Name} can't be repossessed?", "Pay", UIFactory.Bad, () => WS.SettleFinance(car)), 44f, 13f);
                UIFactory.LE(fin, h: 48, w: 130, flexW: 0);
                fin.interactable = owed > 0;
            }
        }

        void Mot(CarInstance car)
        {
            var rec = WS.RunMot(car);
            if (rec == null) return;
            string body = rec.passed ? $"PASS. Valid for {VehicleRecords.MotValidDays} days." : "FAIL:\n- " + string.Join("\n- ", rec.fails);
            if (rec.advisories.Count > 0) body += "\n\nAdvisories:\n- " + string.Join("\n- ", rec.advisories);
            if (!rec.passed) body += "\n\nFix the failures (Repairs tab) and test again.";
            UIFactory.Confirm($"MOT - {car.Name}", body, "OK", rec.passed ? UIFactory.Good : UIFactory.Bad, null);
            Refresh();
        }
    }
}
