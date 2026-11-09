using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// First-person work on a car (spec §6.5 Inspection Mode, §6.6 DIY): markers appear on the car (wheels, bonnet,
    /// door, underneath); look at one and hold E until it fills. Inspection points can be done in any order; repair
    /// steps go in order (undo bolts, swap the part, tighten). Walk more than 12 m away to stop.
    /// </summary>
    public class HandsOnSession : MonoBehaviour
    {
        public const float MaxDistance = 12f;
        enum Kind { None, Inspection, Repair }

        Kind kind;
        CarDisplay car;
        readonly List<HandsOnSocket> sockets = new List<HandsOnSocket>();
        List<InspectPoint> points;
        List<HandsOnStep> steps;
        int stepIndex;
        RepairJobDef job;
        PartTier tier;
        bool onLift;
        HandsOnSocket holding;
        float progress;
        InputAction interact;
        PlayerInteractor player;
        RectTransform overlay;
        TextMeshProUGUI overlayText;
        Material markerMat, doneMat;
        readonly HashSet<InspectMethod> reported = new HashSet<InspectMethod>();

        public bool Running => kind != Kind.None;
        public CarDisplay Car => car;

        // ------------------------------------------------------------------ START / STOP
        public void StartInspection(CarDisplay target, bool lift)
        {
            if (!Begin(target)) return;
            kind = Kind.Inspection;
            onLift = lift;
            points = HandsOnInspection.Points(CarSalesServices.Workshop && CarSalesServices.Workshop.HasScanner, lift);
            reported.Clear();
            for (int i = 0; i < points.Count; i++) Spawn(i, points[i].Area, points[i].Index, points[i].Label, points[i].Seconds);
            CarSalesServices.UI.Notify("Walk around the car: look at a marker and hold E.");
            UpdateOverlay();
        }

        /// <summary>Parts must already be taken out of storage (WorkshopManager does that).</summary>
        public void StartRepair(CarDisplay target, RepairJobDef repair, PartTier partTier)
        {
            if (!Begin(target)) return;
            kind = Kind.Repair;
            job = repair;
            tier = partTier;
            steps = HandsOn.Steps(job);
            stepIndex = 0;
            // One marker per bolt position plus the part itself; tightening reuses the bolt markers.
            int bolts = HandsOn.BoltsFor(job);
            for (int i = 0; i <= bolts; i++) Spawn(i, steps[0].Area, i, i < bolts ? $"Bolt {i + 1}" : "Part", 1f);
            CarSalesServices.UI.Notify($"{job.Name}: hold E on the glowing markers, one after another.");
            UpdateOverlay();
        }

        bool Begin(CarDisplay target)
        {
            if (Running) Stop(false, "Previous job stopped.");
            if (!target) return false;
            car = target;
            CarSalesServices.UI.Close();
            if (!player) player = FindFirstObjectByType<PlayerInteractor>();
            interact = CarSalesInput.Resolve(null, "Interact");
            CreateOverlay();
            return true;
        }

        public void Stop(bool finished, string message = null)
        {
            if (kind == Kind.Repair && !finished && job != null && CarSalesServices.Workshop)
                CarSalesServices.Workshop.ReturnParts(job, tier);   // nothing was fitted
            foreach (var s in sockets) if (s) Destroy(s.gameObject);
            sockets.Clear();
            if (overlay) Destroy(overlay.gameObject);
            kind = Kind.None;
            holding = null;
            car = null;
            job = null;
            if (!string.IsNullOrEmpty(message) && CarSalesServices.UI) CarSalesServices.UI.Notify(message);
        }

        void OnDestroy() { if (markerMat) Destroy(markerMat); if (doneMat) Destroy(doneMat); }

        // ------------------------------------------------------------------ MARKERS
        static Bounds CarBounds(CarDisplay d)
        {
            var rs = d.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(d.transform.position + Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 4.4f));
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        Vector3 Position(CarArea area, int index, int count)
        {
            var t = car.transform;
            var b = CarBounds(car);
            // Size in the car's own axes.
            float halfW = Mathf.Max(0.7f, Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(t.right.x), Mathf.Abs(t.right.y), Mathf.Abs(t.right.z)))));
            float halfL = Mathf.Max(1.6f, Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(t.forward.x), Mathf.Abs(t.forward.y), Mathf.Abs(t.forward.z)))));
            Vector3 c = new Vector3(b.center.x, t.position.y, b.center.z);
            Vector3 f = t.forward, r = t.right;
            float spread = count > 1 ? (index / (float)(count - 1) - 0.5f) : 0f;
            switch (area)
            {
                case CarArea.FrontWheels:
                    return count == 2 && kind == Kind.Inspection
                        ? c + f * halfL * 0.62f + r * (index == 0 ? -halfW : halfW) * 1.02f + Vector3.up * 0.45f
                        : c + f * halfL * 0.62f - r * halfW * 1.02f + Vector3.up * (0.3f + 0.1f * index) + f * spread * 0.35f;
                case CarArea.RearWheels:
                    return count == 2 && kind == Kind.Inspection
                        ? c - f * halfL * 0.62f + r * (index == 0 ? -halfW : halfW) * 1.02f + Vector3.up * 0.45f
                        : c - f * halfL * 0.62f - r * halfW * 1.02f + Vector3.up * (0.3f + 0.1f * index) + f * spread * 0.35f;
                case CarArea.Bonnet:
                    return c + f * halfL * 0.75f + r * spread * halfW * 1.2f + Vector3.up * (b.size.y * 0.75f + 0.1f);
                case CarArea.Underside:
                    return c - r * halfW * 1.05f + f * spread * halfL * 0.8f + Vector3.up * (onLift || kind == Kind.Repair ? 0.25f : 0.2f);
                case CarArea.Side:
                    return c + r * halfW * 1.04f + f * spread * halfL * 1.1f + Vector3.up * 0.75f;
                default: // Interior: driver's door (UK cars: right-hand side), OBD port lower
                    return c + r * halfW * 1.05f + f * 0.25f + Vector3.up * (index == 0 ? 1.0f : 0.55f);
            }
        }

        void Spawn(int index, CarArea area, int areaIndex, string label, float seconds)
        {
            int count = kind == Kind.Inspection ? points.Count(p => p.Area == area) : HandsOn.BoltsFor(job) + 1;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "HandsOn_" + label;
            go.layer = 20;   // Interactable
            go.transform.position = Position(area, kind == Kind.Repair ? index : areaIndex, count);
            go.transform.localScale = Vector3.one * 0.16f;
            var col = go.GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 2.2f;   // bigger than the marker so it's easy to look at
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = Marker(false);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var s = go.AddComponent<HandsOnSocket>();
            s.session = this;
            s.index = index;
            s.label = label;
            s.seconds = seconds;
            sockets.Add(s);
        }

        Material Marker(bool done)
        {
            if (done && doneMat) return doneMat;
            if (!done && markerMat) return markerMat;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = done ? "HandsOnDone" : "HandsOnMarker" };
            Color c = done ? UIFactory.Good : UIFactory.Gold;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (done) doneMat = m; else markerMat = m;
            return m;
        }

        // ------------------------------------------------------------------ HOLD
        int CurrentRepairSocket()
        {
            if (kind != Kind.Repair || steps == null || stepIndex >= steps.Count) return -1;
            return steps[stepIndex].Index;   // bolt i, or "part" (index = bolts)
        }

        public bool IsActive(HandsOnSocket s)
        {
            if (!Running || !s) return false;
            if (kind == Kind.Inspection) return !s.done;
            return s.index == CurrentRepairSocket();
        }

        string StepLabel(HandsOnSocket s) => kind == Kind.Repair ? steps[stepIndex].Label : s.label;
        float StepSeconds(HandsOnSocket s) => kind == Kind.Repair ? steps[stepIndex].Seconds : s.seconds;

        public string PromptFor(HandsOnSocket s)
        {
            if (!IsActive(s)) return "";
            int pct = holding == s ? Mathf.RoundToInt(progress / StepSeconds(s) * 100f) : 0;
            return $"[E] Hold: {StepLabel(s)}" + (pct > 0 ? $"  {pct}%" : "");
        }

        public void BeginHold(HandsOnSocket s)
        {
            if (!IsActive(s)) return;
            if (holding != s) progress = 0f;
            holding = s;
        }

        void Update()
        {
            if (!Running) return;
            if (!car || (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving)) { Stop(false, "Hands-on work stopped."); return; }
            if (player && Vector3.Distance(player.transform.position, car.transform.position) > MaxDistance)
            {
                Stop(false, kind == Kind.Repair ? "You walked away: the job is cancelled and the parts go back on the shelf." : "Inspection stopped.");
                return;
            }
            if (!holding) return;
            bool pressed = (interact != null && interact.IsPressed()) || (Keyboard.current != null && Keyboard.current.eKey.isPressed);
            bool looking = player && ReferenceEquals(player.CurrentTarget, holding);
            if (!pressed || !looking) { holding = null; progress = 0f; return; }
            progress += Time.deltaTime;
            if (progress < StepSeconds(holding)) return;
            var s = holding;
            holding = null;
            progress = 0f;
            CarSalesServices.PlaySfx(Sfx.Click, 0.6f);
            if (kind == Kind.Inspection) CompletePoint(s); else CompleteStep();
        }

        void CompletePoint(HandsOnSocket s)
        {
            s.done = true;
            s.GetComponent<Renderer>().sharedMaterial = Marker(true);
            var done = new HashSet<int>(sockets.Where(x => x.done).Select(x => x.index));
            foreach (var m in points.Select(p => p.Method).Distinct())
            {
                if (reported.Contains(m) || !HandsOnInspection.MethodComplete(points, done, m)) continue;
                reported.Add(m);
                var ws = CarSalesServices.Workshop;
                var ctx = WorkshopManager.ContextOf(car.Data, car);
                var r = ws ? ws.Inspect(car.Data, m, ctx, freeLift: true, passTime: true) : null;
                if (r != null)
                {
                    string found = r.found.Count == 0 ? "nothing new" : string.Join(", ", r.found.Select(f => f.Def.Name));
                    var extra = r.notes.Where(n => n != "Nothing new found.").ToList();
                    CarSalesServices.UI.Toast(r.found.Count > 0 ? ToastType.Warning : ToastType.Info, InspectionEngine.Info(m).Name,
                        $"Found: {found}." + (extra.Count > 0 ? " " + string.Join(" ", extra) : ""));
                }
            }
            UpdateOverlay();
            if (sockets.All(x => x.done))
            {
                var c = car;
                Stop(true, "Inspection complete. Open the car's inspection screen for the full list.");
                if (c) CarSalesServices.UI.inspection.Show(c.Data, c);
            }
        }

        void CompleteStep()
        {
            int bolts = HandsOn.BoltsFor(job);
            var current = sockets.Find(x => x.index == CurrentRepairSocket());
            stepIndex++;
            if (current) current.GetComponent<Renderer>().sharedMaterial = Marker(true);
            if (stepIndex == bolts + 1)
                foreach (var b in sockets) if (b.index < bolts) b.GetComponent<Renderer>().sharedMaterial = Marker(false);   // now tighten them
            UpdateOverlay();
            if (stepIndex < steps.Count) return;
            var c = car; var j = job; var t = tier;
            job = null;   // parts are used
            Stop(true);
            if (CarSalesServices.Workshop) CarSalesServices.Workshop.CompleteHandsOn(c.Data, j, t);
        }

        // ------------------------------------------------------------------ OVERLAY
        void CreateOverlay()
        {
            var ui = CarSalesServices.UI;
            if (!ui) return;
            var panel = UIFactory.Panel(ui.transform, "HandsOnOverlay", UIFactory.HudBgStrong, 1.6f);
            overlay = panel.rectTransform;
            UIFactory.Place(overlay, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 60f), new Vector2(400f, 380f));
            panel.raycastTarget = false;
            overlayText = UIFactory.Text(panel.transform, "", 16, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            UIFactory.Stretch(overlayText.rectTransform, 16f);
        }

        void UpdateOverlay()
        {
            if (!overlayText) return;
            string ok = UIFactory.Hex(UIFactory.Good), dim = UIFactory.Hex(UIFactory.TextDim), cur = UIFactory.Hex(UIFactory.Gold);
            var lines = new List<string>();
            if (kind == Kind.Inspection)
            {
                lines.Add($"<b>INSPECTION</b>  -  {car.Data.Name}");
                for (int i = 0; i < points.Count; i++)
                    lines.Add(sockets[i].done ? $"<color={ok}>[x] {points[i].Label}</color>" : $"<color={dim}>[  ]</color> {points[i].Label}");
            }
            else
            {
                lines.Add($"<b>{job.Name.ToUpperInvariant()}</b>  -  {car.Data.Name}");
                for (int i = 0; i < steps.Count; i++)
                    lines.Add(i < stepIndex ? $"<color={ok}>[x] {steps[i].Label}</color>" : i == stepIndex ? $"<color={cur}>> {steps[i].Label}</color>" : $"<color={dim}>   {steps[i].Label}</color>");
            }
            lines.Add($"\n<size=85%><color={dim}>Look at a glowing marker and hold E. Walk away to stop.</color></size>");
            overlayText.text = string.Join("\n", lines);
        }
    }
}
