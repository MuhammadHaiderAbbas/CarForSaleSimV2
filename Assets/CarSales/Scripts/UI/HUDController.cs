using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Minimal in-game HUD (spec §6.14.2): clock + shop state + energy (top-left), cash (top-right), GPS strip
    /// (top-centre), minimap (bottom-left), speed/gear/fuel only while driving (bottom-right), a contextual
    /// prompt with a key cap, and a controls list that H shows/hides. Fades out while a menu is open.
    /// </summary>
    public class HUDController : UIPanel
    {
        const string HintsPref = "CarSales.HudHints";
        const float MaxGaugeKmh = 220f;

        TextMeshProUGUI clockText, shopText, goalText, moneyText, deltaText, subText, promptText, promptKey, speedText, gearText, fuelText, energyText, gpsText, hintToggleText;
        UIFactory.Bar energyBar, fuelBar;
        Image gpsArrow, gpsPanel, promptPanel, promptCap, shopDot, speedFill, arrow;
        RectTransform driveCluster, hintsPanel, hintToggle;
        TextMeshProUGUI unreadText;
        MinimapMarker gpsMarker;
        RawImage minimapImage;
        Camera minimapCamera;
        RenderTexture minimapTexture;
        PlayerInteractor interactor;
        CanvasGroup group;
        float deltaTimer, hintsAutoHide = 25f;
        bool hintsOn;

        public override void Build()
        {
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // ---- Top-left: clock, shop state, energy, goal
            var tl = UIFactory.Panel(Root, "Status", UIFactory.HudBg, 1.6f);
            UIFactory.Place(tl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(430f, 96f));
            var tlv = UIFactory.Vertical(tl, 0, 3f);
            tlv.padding = new RectOffset(18, 18, 10, 10);
            clockText = UIFactory.Text(tl.transform, "Mon  Day 1   08:00", 26, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(clockText, h: 32);
            var shopRow = UIFactory.HRow(tl.transform, 20, 8, "Shop", false);
            shopDot = UIFactory.SpriteImage(shopRow, "Dot", UIFactory.Circle, UIFactory.Good, 10f);
            UIFactory.LE(shopDot, h: 10, w: 10, flexW: 0);
            shopRow.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            shopText = UIFactory.Text(shopRow, "", 15, UIFactory.TextDim, TextAlignmentOptions.Left, true);
            UIFactory.LE(shopText, flexW: 1);
            var energyRow = UIFactory.HRow(tl.transform, 16, 10, "Energy", false);
            var barHolder = UIFactory.VCol(energyRow, 0f, "BarHolder");
            barHolder.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            energyBar = UIFactory.ProgressBar(barHolder, UIFactory.Good, 6f);
            energyText = UIFactory.Text(energyRow, "Energy", 13, UIFactory.TextDim, TextAlignmentOptions.Right, true);
            UIFactory.LE(energyText, w: 110, flexW: 0);

            goalText = UIFactory.Text(Root, "", 15, UIFactory.Info, TextAlignmentOptions.TopLeft);
            UIFactory.Place(goalText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -124f), new Vector2(560f, 48f));
            goalText.gameObject.AddComponent<Shadow>().effectColor = UITheme.Current.shadow;

            // ---- Top-right: cash
            moneyText = UIFactory.Text(Root, "$0", 36, UIFactory.Good, TextAlignmentOptions.Right, true, "Money");
            UIFactory.Place(moneyText.rectTransform, Vector2.one, Vector2.one, new Vector2(-26f, -16f), new Vector2(420f, 46f));
            moneyText.gameObject.AddComponent<Shadow>().effectColor = UITheme.Current.shadow;
            deltaText = UIFactory.Text(Root, "", 20, UIFactory.Good, TextAlignmentOptions.Right, true, "Delta");
            UIFactory.Place(deltaText.rectTransform, Vector2.one, Vector2.one, new Vector2(-26f, -86f), new Vector2(300f, 26f));
            subText = UIFactory.Text(Root, "", 15, UIFactory.TextDim, TextAlignmentOptions.Right, true, "Sub");
            UIFactory.Place(subText.rectTransform, Vector2.one, Vector2.one, new Vector2(-26f, -62f), new Vector2(420f, 22f));
            subText.gameObject.AddComponent<Shadow>().effectColor = UITheme.Current.shadow;

            // ---- Top-centre: GPS strip
            gpsPanel = UIFactory.Panel(Root, "GPS", UIFactory.HudBgStrong, 3f);
            UIFactory.Place(gpsPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(420f, 44f));
            gpsArrow = UIFactory.SpriteImage(gpsPanel.transform, "Arrow", UIFactory.Arrow, UIFactory.Gold, 26f);
            UIFactory.Place(gpsArrow.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28f, 0f), new Vector2(26f, 26f));
            gpsText = UIFactory.Text(gpsPanel.transform, "", 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Stretch(gpsText.rectTransform);
            gpsText.rectTransform.offsetMin = new Vector2(54f, 0f);
            gpsPanel.gameObject.SetActive(false);

            // ---- Bottom-left: minimap
            var frame = UIFactory.SpriteImage(Root, "MinimapFrame", UIFactory.Circle, UIFactory.WithAlpha(UIFactory.Accent, 0.85f), 206f);
            UIFactory.Place(frame.rectTransform, Vector2.zero, Vector2.zero, new Vector2(22f, 22f), new Vector2(206f, 206f));
            var mask = UIFactory.SpriteImage(frame.transform, "MinimapMask", UIFactory.Circle, Color.white, 198f);
            mask.rectTransform.anchoredPosition = Vector2.zero;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            minimapImage = UIFactory.Rect("Map", mask.transform).gameObject.AddComponent<RawImage>();
            UIFactory.Stretch(minimapImage.rectTransform);
            minimapImage.raycastTarget = false;
            arrow = UIFactory.SpriteImage(mask.transform, "PlayerArrow", UIFactory.Arrow, Color.white, 22f);
            arrow.rectTransform.anchoredPosition = Vector2.zero;
            var north = UIFactory.Text(frame.transform, "N", 16, UIFactory.TextMain, TextAlignmentOptions.Center, true);
            UIFactory.Place(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 4f), new Vector2(30f, 24f));

            // ---- Bottom-right: driving cluster (only while driving)
            var cluster = UIFactory.Panel(Root, "DriveCluster", UIFactory.HudBgStrong, 1.6f);
            driveCluster = cluster.rectTransform;
            UIFactory.Place(driveCluster, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 22f), new Vector2(320f, 170f));
            var ring = UIFactory.SpriteImage(cluster.transform, "RingBg", UIFactory.Ring, UIFactory.Ghost, 140f);
            UIFactory.Place(ring.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(140f, 140f));
            speedFill = UIFactory.SpriteImage(ring.transform, "RingFill", UIFactory.Ring, UIFactory.Accent, 140f);
            speedFill.rectTransform.anchoredPosition = Vector2.zero;
            speedFill.type = Image.Type.Filled;
            speedFill.fillMethod = Image.FillMethod.Radial360;
            speedFill.fillOrigin = 0;
            speedFill.fillAmount = 0f;
            speedText = UIFactory.Text(ring.transform, "0", 44, UIFactory.TextMain, TextAlignmentOptions.Center, true);
            UIFactory.Place(speedText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(120f, 52f));
            var unit = UIFactory.Text(ring.transform, Loc.T("hud.kmh", "KM/H"), 13, UIFactory.TextDim, TextAlignmentOptions.Center, true);
            UIFactory.Place(unit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -24f), new Vector2(80f, 18f));

            var gearLabel = UIFactory.Text(cluster.transform, Loc.T("hud.gear", "GEAR"), 12, UIFactory.TextDim, TextAlignmentOptions.Left, true);
            UIFactory.Place(gearLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(120f, 16f));
            gearText = UIFactory.Text(cluster.transform, "D", 50, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.Place(gearText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -34f), new Vector2(140f, 60f));
            fuelText = UIFactory.Text(cluster.transform, "FUEL", 12, UIFactory.TextDim, TextAlignmentOptions.Left, true);
            UIFactory.Place(fuelText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 38f), new Vector2(140f, 16f));
            var fuelHolder = UIFactory.Rect("FuelBar", cluster.transform);
            UIFactory.Place(fuelHolder, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 22f), new Vector2(140f, 8f));
            UIFactory.Vertical(fuelHolder, 0, 0f);
            fuelBar = UIFactory.ProgressBar(fuelHolder, UIFactory.Gold, 8f);
            driveCluster.gameObject.SetActive(false);

            // ---- Controls (H)
            var toggle = UIFactory.Panel(Root, "HintToggle", UIFactory.HudBg, 3f);
            hintToggle = toggle.rectTransform;
            UIFactory.Place(hintToggle, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 22f), new Vector2(170f, 40f));
            var th = UIFactory.Horizontal(toggle, 0, 8f);
            th.padding = new RectOffset(8, 10, 4, 4);
            hintToggleText = UIFactory.KeyHint(toggle.transform, "H", "Controls");

            var keys = UIFactory.Panel(Root, "KeyHints", UIFactory.HudBg, 1.6f);
            hintsPanel = keys.rectTransform;
            UIFactory.Place(hintsPanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 72f), new Vector2(260f, 330f));
            var kv = UIFactory.Vertical(keys, 14, 5f);
            kv.padding = new RectOffset(16, 14, 14, 14);
            UIFactory.KeyHint(keys.transform, "E", "Interact");
            UIFactory.KeyHint(keys.transform, "F", "Enter / exit car");
            unreadText = UIFactory.KeyHint(keys.transform, "Tab", "My cars");
            UIFactory.KeyHint(keys.transform, "M", "Map & GPS");
            UIFactory.KeyHint(keys.transform, "P", "Phone");
            UIFactory.KeyHint(keys.transform, "O", "Close / open shops");
            UIFactory.KeyHint(keys.transform, "C", "Camera (driving)");
            UIFactory.KeyHint(keys.transform, "Esc", "Back / menu");

            // ---- Bottom-centre: contextual prompt
            promptPanel = UIFactory.Panel(Root, "Prompt", UIFactory.HudBgStrong, 3f);
            UIFactory.Place(promptPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(760f, 50f));
            var ph = UIFactory.Horizontal(promptPanel, 0, 12f, TextAnchor.MiddleCenter);
            ph.padding = new RectOffset(10, 18, 7, 7);
            ph.childForceExpandHeight = true;
            promptPanel.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            promptCap = UIFactory.Panel(promptPanel.transform, "Key", UITheme.Current.keyCap, 3f);
            UIFactory.LE(promptCap, w: 36, flexW: 0);
            promptKey = UIFactory.Text(promptCap.transform, "E", 18, UITheme.Current.keyCapText, TextAlignmentOptions.Center, true);
            UIFactory.Stretch(promptKey.rectTransform);
            promptText = UIFactory.Text(promptPanel.transform, "", 20, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            promptText.textWrappingMode = TextWrappingModes.NoWrap;

            // Crosshair
            var cross = UIFactory.SpriteImage(Root, "Crosshair", UIFactory.Circle, UIFactory.Crosshair, 6f);
            UIFactory.Place(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));

            hintsOn = PlayerPrefs.GetInt(HintsPref, 0) == 1;   // off by default; shown for the first seconds of a session
        }

        void Start()
        {
            interactor = FindFirstObjectByType<PlayerInteractor>();
            CreateMinimapCamera();

            var eco = CarSalesServices.Economy;
            var day = CarSalesServices.DayCycle;
            eco.OnMoneyChanged += HandleMoney;
            eco.OnReputationChanged += HandleRep;
            day.OnTimeChanged += HandleTime;
            day.OnShopStateChanged += HandleShop;
            if (CarSalesServices.Progress) CarSalesServices.Progress.OnObjectiveChanged += HandleObjective;
            if (CarSalesServices.Bank) CarSalesServices.Bank.OnLoanChanged += HandleLoan;
            if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged += HandleMenu;
            HandleMoney(eco.Money, 0);
            deltaText.text = "";
            HandleTime(day.Hour);
            HandleShop(day.IsOpen);
            HandleObjective();
            UpdateSub();
        }

        void CreateMinimapCamera()
        {
            minimapTexture = new RenderTexture(512, 512, 16) { name = "Minimap" };
            var go = new GameObject("MinimapCamera");
            minimapCamera = go.AddComponent<Camera>();
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = 34f;
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = UIFactory.MapBg;
            minimapCamera.cullingMask = ~((1 << ThumbnailService.StudioLayer) | (1 << 5));
            minimapCamera.nearClipPlane = 1f;
            minimapCamera.farClipPlane = 200f;
            minimapCamera.targetTexture = minimapTexture;
            minimapCamera.depth = -10;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            minimapImage.texture = minimapTexture;
            if (Camera.main) Camera.main.cullingMask &= ~(1 << MinimapMarker.MinimapLayer);
        }

        void OnDestroy()
        {
            if (CarSalesServices.Economy)
            {
                CarSalesServices.Economy.OnMoneyChanged -= HandleMoney;
                CarSalesServices.Economy.OnReputationChanged -= HandleRep;
            }
            if (CarSalesServices.DayCycle)
            {
                CarSalesServices.DayCycle.OnTimeChanged -= HandleTime;
                CarSalesServices.DayCycle.OnShopStateChanged -= HandleShop;
            }
            if (CarSalesServices.Progress) CarSalesServices.Progress.OnObjectiveChanged -= HandleObjective;
            if (CarSalesServices.Bank) CarSalesServices.Bank.OnLoanChanged -= HandleLoan;
            if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged -= HandleMenu;
            if (minimapCamera) Destroy(minimapCamera.gameObject);
            if (minimapTexture) { minimapTexture.Release(); Destroy(minimapTexture); }
        }

        void HandleMenu(bool open) { if (group) group.alpha = open ? 0f : 1f; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam)
            {
                cam.cullingMask &= ~((1 << MinimapMarker.MinimapLayer) | (1 << ThumbnailService.StudioLayer));
                if (minimapCamera)
                {
                    Vector3 p = cam.transform.position;
                    minimapCamera.transform.position = new Vector3(p.x, p.y + 80f, p.z);
                }
                arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -cam.transform.eulerAngles.y);
            }
            if (!interactor) interactor = FindFirstObjectByType<PlayerInteractor>();
            UpdateEnergyAndGps();
            UpdateDriving();
            UpdatePrompt();
            UpdateHints();
            TickMoney();

            if (deltaTimer > 0f)
            {
                deltaTimer -= Time.unscaledDeltaTime;
                var c = deltaText.color;
                c.a = Mathf.Clamp01(deltaTimer / 0.6f);
                deltaText.color = c;
                if (deltaTimer <= 0f) deltaText.text = "";
            }
        }

        void UpdateDriving()
        {
            var drive = CarSalesServices.TestDrive;
            bool driving = drive && drive.IsDriving;
            if (driveCluster.gameObject.activeSelf != driving) driveCluster.gameObject.SetActive(driving);
            if (!driving) return;
            float kmh = drive.SpeedKmh;
            speedText.text = Mathf.RoundToInt(kmh).ToString();
            speedFill.fillAmount = Mathf.Clamp01(kmh / MaxGaugeKmh) * 0.8f;
            speedFill.color = kmh > 130f ? UIFactory.Warn : UIFactory.Accent;
            gearText.text = string.IsNullOrEmpty(drive.Gear) ? "-" : drive.Gear;
            float f = drive.Fuel;
            fuelBar.Set(f / 100f);
            fuelBar.SetColor(f < 15f ? UIFactory.Bad : UIFactory.Gold);
            fuelText.text = f < 15f ? $"{Loc.T("hud.fuel", "FUEL")} {f:0}%  {Loc.T("hud.low", "LOW")}" : $"{Loc.T("hud.fuel", "FUEL")} {f:0}%";
            fuelText.color = f < 15f ? UIFactory.Bad : UIFactory.TextDim;
        }

        void UpdatePrompt()
        {
            var drive = CarSalesServices.TestDrive;
            string prompt = drive && drive.IsDriving
                ? (string.IsNullOrEmpty(drive.ContextPrompt) ? "" : drive.ContextPrompt)
                : interactor ? interactor.CurrentPrompt : "";
            bool show = !string.IsNullOrEmpty(prompt);
            if (promptPanel.gameObject.activeSelf != show) promptPanel.gameObject.SetActive(show);
            if (!show) return;
            // "[E] Do something" -> key cap "E" + "Do something"
            string key = null, text = prompt;
            if (prompt.Length > 3 && prompt[0] == '[')
            {
                int close = prompt.IndexOf(']');
                if (close > 1 && close < 8) { key = prompt.Substring(1, close - 1); text = prompt.Substring(close + 1).TrimStart(); }
            }
            promptCap.gameObject.SetActive(key != null);
            if (key != null) { promptKey.text = key; UIFactory.LE(promptCap, w: Mathf.Max(36f, key.Length * 12f + 18f), flexW: 0); }
            promptText.text = text;
        }

        void UpdateHints()
        {
            var kb = Keyboard.current;
            bool menu = CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen;
            if (!menu && kb != null && kb.hKey.wasPressedThisFrame)
            {
                hintsOn = !hintsOn;
                hintsAutoHide = 0f;
                PlayerPrefs.SetInt(HintsPref, hintsOn ? 1 : 0);
            }
            if (hintsAutoHide > 0f) hintsAutoHide -= Time.unscaledDeltaTime;
            bool driving = driveCluster.gameObject.activeSelf;
            // The list shows when switched on (or during the first seconds of a session); never over the dashboard.
            bool showList = !driving && (hintsOn || hintsAutoHide > 0f);
            if (hintsPanel.gameObject.activeSelf != showList) hintsPanel.gameObject.SetActive(showList);
            if (hintToggle.gameObject.activeSelf == driving) hintToggle.gameObject.SetActive(!driving);
            hintToggleText.text = showList ? "Hide controls" : "Controls";

            var msgs = CarSalesServices.Messages;
            if (unreadText && msgs)
            {
                int unread = msgs.UnreadCount;
                unreadText.text = unread > 0 ? $"My cars  <color={UIFactory.Hex(UIFactory.Gold)}>({unread} msg)</color>" : "My cars";
            }
        }

        // Phase 10 polish: the money readout counts up/down to the new balance instead of jumping.
        double shownMoney;
        int targetMoney, moneyStart;
        float moneyTween = -1f;
        const float MoneyTweenTime = 0.6f;

        void TickMoney()
        {
            if (moneyTween < 0f || !moneyText) return;
            moneyTween += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(moneyTween / MoneyTweenTime);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic
            shownMoney = moneyStart + (targetMoney - moneyStart) * (double)eased;
            if (t >= 1f) { shownMoney = targetMoney; moneyTween = -1f; }
            int shown = (int)System.Math.Round(shownMoney);
            moneyText.text = UIFactory.Money(shown);
            moneyText.color = shown < 0 ? UIFactory.Bad : UIFactory.Good;
        }

        void HandleMoney(int money, int delta)
        {
            if (delta != 0 && isActiveAndEnabled)
            {
                moneyStart = (int)System.Math.Round(shownMoney);
                targetMoney = money;
                moneyTween = 0f;
            }
            else
            {
                shownMoney = targetMoney = money;
                moneyTween = -1f;
                moneyText.text = UIFactory.Money(money);
                moneyText.color = money < 0 ? UIFactory.Bad : UIFactory.Good;
            }
            if (delta != 0)
            {
                deltaText.text = (delta > 0 ? "+" : "") + UIFactory.Money(delta);
                deltaText.color = delta > 0 ? UIFactory.Good : UIFactory.Bad;
                deltaTimer = 2.5f;
            }
        }

        void HandleRep(float rep) => UpdateSub();
        void HandleLoan() => UpdateSub();

        void UpdateSub()
        {
            var eco = CarSalesServices.Economy;
            int loan = CarSalesServices.Bank ? CarSalesServices.Bank.Loan : 0;
            subText.text = $"Reputation {eco.Reputation:0}" + (loan > 0 ? $"   <color={UIFactory.Hex(UIFactory.Warn)}>Loan {UIFactory.Money(loan)}</color>" : "");
        }

        void HandleTime(float hour)
        {
            var day = CarSalesServices.DayCycle;
            float hod = day.HourOfDay;
            int h = Mathf.FloorToInt(hod), m = Mathf.FloorToInt((hod - h) * 60f);
            clockText.text = $"{CarSales.Foundation.GameCalendar.WeekdayShort(day.CurrentDay)}  Day {day.CurrentDay}   {h:00}:{m:00}" +
                             (CarSalesServices.World ? $"  <size=60%><color={UIFactory.Hex(UIFactory.TextDim)}>{CarSalesServices.World.Describe()}</color></size>" : "");
            HandleShop(day.IsOpen);
        }

        void HandleShop(bool open)
        {
            var day = CarSalesServices.DayCycle;
            Color c;
            if (open) { shopText.text = $"Dealerships open until {day.CloseHour:00}:00"; c = UIFactory.Good; }
            else if (day.Hour < day.OpenHour) { shopText.text = $"Dealerships open at {day.OpenHour:00}:00"; c = UIFactory.Warn; }
            else if (day.Hour >= day.CloseHour) { shopText.text = day.Hour >= DayCycleManager.EarliestSleepHour ? "Closed - go home and sleep" : "Dealerships closed for today"; c = UIFactory.Bad; }
            else { shopText.text = "Closed by you (O to reopen)"; c = UIFactory.Bad; }
            shopDot.color = c;
        }

        void UpdateEnergyAndGps()
        {
            var day = CarSalesServices.DayCycle;
            if (day && energyBar != null)
            {
                float e = day.Energy;
                energyBar.Set(e / 100f);
                energyBar.SetColor(e > 40f ? UIFactory.Good : e > 15f ? UIFactory.Warn : UIFactory.Bad);
                energyText.text = e > 15f ? $"{Loc.T("hud.energy", "Energy")} {e:0}%" : $"<color={UIFactory.Hex(UIFactory.Bad)}>{Loc.T("hud.tired", "Tired")} {e:0}%</color>";
            }
            var target = GpsSystem.Target;
            if (gpsPanel.gameObject.activeSelf != (bool)target) gpsPanel.gameObject.SetActive(target);
            if (!gpsMarker)
            {
                var go = new GameObject("GpsMarker");
                gpsMarker = MinimapMarker.Attach(go, UIFactory.Gold, 9f);
            }
            gpsMarker.SetVisible(target);
            var cam = Camera.main;
            if (!target || !cam) return;
            gpsMarker.transform.position = target.transform.position + Vector3.up * 2f;
            Vector3 to = target.transform.position - cam.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 15f)
            {
                gpsText.text = $"Arrived: {target.placeName}";
                gpsArrow.rectTransform.localEulerAngles = Vector3.zero;
                return;
            }
            float angle = Vector3.SignedAngle(new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z), to, Vector3.up);
            gpsArrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
            gpsText.text = $"{target.placeName}   <color={UIFactory.Hex(UIFactory.TextDim)}>{(dist >= 1000f ? $"{dist / 1000f:0.0} km" : $"{dist:0} m")}</color>";
        }

        void HandleObjective()
        {
            if (CarSalesServices.Progress) goalText.text = CarSalesServices.Progress.CurrentObjectiveText;
        }
    }
}
