using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarSales
{
    public enum ToastType { Info, Success, Warning }

    /// <summary>
    /// Creates the entire UI from code on startup (HUD, computer, all windows, notifications) and handles
    /// opening/closing, cursor, pause and toasts. Put it on a GameObject with (or without) a Canvas.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [SerializeField] InputActionReference closeAction, inventoryAction, mapAction, phoneAction;

        [Header("Created at runtime")]
        public HUDController hud;
        public ComputerHubUI computerHub;
        public HomePageUI home;
        public MarketplaceUI marketplace;
        public InventoryUI inventory;
        public MessagesUI messages;
        public UpgradesUI upgrades;
        public BankUI bank;
        public StatsUI stats;
        public RealEstateUI realEstate;
        public MapUI map;
        public PhoneUI phone;
        public AuctionUI auction;
        public PrivateSellerUI privateSeller;
        public ListingEditorUI listingEditor;
        public HandoverUI handover;
        public VehicleDetailsUI vehicleDetails;
        public InspectionUI inspection;
        public PartsUI parts;
        public PurchaseConfirmUI purchaseConfirm;
        public CarMenuUI carMenu;
        public GarageUI garage;
        public CarWashUI carWash;
        public PriceUI priceUI;
        public NegotiationUI negotiation;
        public DailyReportUI dailyReport;
        public PauseMenuUI pause;
        public SettingsUI settings;
        public EndScreenUI endScreen;

        public event Action<bool> OnMenuStateChanged;
        public bool IsMenuOpen => openPanel != null;
        public GameObject OpenPanel => openPanel;

        GameObject openPanel;
        readonly System.Collections.Generic.List<GameObject> history = new System.Collections.Generic.List<GameObject>();
        RectTransform toastArea;
        InputAction close, inventoryKey, mapKey, phoneKey;

        void Awake()
        {
            CarSalesServices.UI = this;
            Time.timeScale = 1f;
            EnsureCanvas();
            // Remove anything an older version of the wizard put under this canvas.
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);

            hud = Create<HUDController>("HUD", true);
            computerHub = Create<ComputerHubUI>("Computer");
            home = computerHub.Home;
            marketplace = computerHub.Marketplace;
            inventory = computerHub.Inventory;
            messages = computerHub.Messages;
            upgrades = computerHub.Upgrades;
            bank = computerHub.Bank;
            stats = computerHub.Stats;
            realEstate = computerHub.RealEstate;
            vehicleDetails = Create<VehicleDetailsUI>("VehicleDetails");
            inspection = Create<InspectionUI>("Inspection");
            parts = Create<PartsUI>("Parts");
            purchaseConfirm = Create<PurchaseConfirmUI>("PurchaseConfirm");
            carMenu = Create<CarMenuUI>("CarMenu");
            garage = Create<GarageUI>("Garage");
            carWash = Create<CarWashUI>("CarWash");
            priceUI = Create<PriceUI>("SetPrice");
            negotiation = Create<NegotiationUI>("Negotiation");
            dailyReport = Create<DailyReportUI>("DailyReport");
            pause = Create<PauseMenuUI>("Pause");
            settings = Create<SettingsUI>("Settings");
            endScreen = Create<EndScreenUI>("EndScreen");
            map = Create<MapUI>("Map");
            phone = Create<PhoneUI>("Phone");
            auction = Create<AuctionUI>("Auction");
            privateSeller = Create<PrivateSellerUI>("PrivateSeller");
            listingEditor = Create<ListingEditorUI>("ListingEditor");
            handover = Create<HandoverUI>("Handover");

            toastArea = UIFactory.Rect("Notifications", transform);
            UIFactory.Place(toastArea, Vector2.one, Vector2.one, new Vector2(-24f, -150f), new Vector2(440f, 640f));
            var v = UIFactory.Vertical(toastArea, 0, 10f, TextAnchor.UpperRight);
            v.childForceExpandWidth = true;
        }

        void EnsureCanvas()
        {
            if (!GetComponent<Canvas>())
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
            }
            var scaler = GetComponent<CanvasScaler>();
            if (!scaler) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameSettings.UiReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
        }

        void ApplyUiScale(float _)
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler) scaler.referenceResolution = GameSettings.UiReferenceResolution;
        }

        T Create<T>(string name, bool active = false) where T : UIPanel
        {
            var rt = UIFactory.Rect(name, transform);
            UIFactory.Stretch(rt);
            var panel = rt.gameObject.AddComponent<T>();
            panel.Build();
            rt.gameObject.SetActive(active);
            return panel;
        }

        void OnEnable()
        {
            close = CarSalesInput.Resolve(closeAction, "CloseMenu");
            inventoryKey = CarSalesInput.Resolve(inventoryAction, "Inventory");
            mapKey = CarSalesInput.Resolve(mapAction, "Map");
            phoneKey = CarSalesInput.Resolve(phoneAction, "Phone");
            if (close != null) close.performed += OnCloseInput;
            if (inventoryKey != null) inventoryKey.performed += OnInventoryInput;
            if (mapKey != null) mapKey.performed += OnMapInput;
            if (phoneKey != null) phoneKey.performed += OnPhoneInput;
            GameSettings.OnUiScaleChanged += ApplyUiScale;
        }

        void OnMapInput(InputAction.CallbackContext _)
        {
            if (openPanel && map && openPanel == map.gameObject) { Close(); return; }
            if (!openPanel) map.Show();
        }

        void OnPhoneInput(InputAction.CallbackContext _)
        {
            if (openPanel && phone && openPanel == phone.gameObject) { Close(); return; }
            if (!openPanel) phone.Show();
        }

        void OnDisable()
        {
            if (close != null) close.performed -= OnCloseInput;
            if (inventoryKey != null) inventoryKey.performed -= OnInventoryInput;
            if (mapKey != null) mapKey.performed -= OnMapInput;
            if (phoneKey != null) phoneKey.performed -= OnPhoneInput;
            GameSettings.OnUiScaleChanged -= ApplyUiScale;
            Time.timeScale = 1f;
        }

        bool IsModal(GameObject panel) =>
            (dailyReport && panel == dailyReport.gameObject) || (endScreen && panel == endScreen.gameObject);

        bool IsPausePanel(GameObject panel) =>
            (pause && panel == pause.gameObject) || (settings && panel == settings.gameObject);

        void OnCloseInput(InputAction.CallbackContext _)
        {
            // An open dialog (UIFactory.Modal) eats Esc first.
            var modal = transform.Find("Modal");
            if (modal)
            {
                if (openPanel == modal.gameObject) Close();
                Destroy(modal.gameObject);
                return;
            }
            if (openPanel)
            {
                if (IsModal(openPanel)) return;
                Back();
            }
            else if (pause) pause.Show();
        }

        /// <summary>
        /// Esc / gamepad B: the open screen may handle it itself (phone -> home screen, computer -> desktop);
        /// otherwise return to the screen it was opened from, or close the menus.
        /// </summary>
        public void Back()
        {
            if (!openPanel) return;
            foreach (var h in openPanel.GetComponents<IBackHandler>())
                if (h.HandleBack()) return;
            while (history.Count > 0)
            {
                var prev = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (!prev || prev == openPanel) continue;
                openPanel.SetActive(false);
                openPanel = null;
                Open(prev, false);
                return;
            }
            if (settings && openPanel == settings.gameObject && pause) { history.Clear(); pause.Show(); return; }
            Close();
        }

        /// <summary>True when Back would return to another screen instead of closing.</summary>
        public bool CanGoBack => history.Exists(p => p && p != openPanel);

        void OnInventoryInput(InputAction.CallbackContext _)
        {
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving) return;
            if (openPanel && computerHub && openPanel == computerHub.gameObject) { Close(); return; }
            if (openPanel) return;
            inventory.Show();
        }

        public void Open(GameObject panel) => Open(panel, true);

        void Open(GameObject panel, bool remember)
        {
            if (remember)
            {
                int existing = history.IndexOf(panel);
                if (existing >= 0) history.RemoveRange(existing, history.Count - existing);   // no loops
                else if (openPanel && openPanel != panel && !IsModal(openPanel)) history.Add(openPanel);
                if (history.Count > 12) history.RemoveAt(0);
            }
            if (openPanel && openPanel != panel) openPanel.SetActive(false);
            openPanel = panel;
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            if (toastArea) toastArea.SetAsLastSibling();
            UITransition.Play(panel);
            Time.timeScale = IsPausePanel(panel) ? 0f : 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            OnMenuStateChanged?.Invoke(true);
        }

        public void Close()
        {
            history.Clear();
            if (!openPanel) return;
            var panel = openPanel;
            openPanel = null;
            panel.SetActive(false);
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            OnMenuStateChanged?.Invoke(false);
        }

        // ------------------------------------------------------------------ NOTIFICATIONS
        public void Notify(string message) => Toast(ToastType.Info, null, message);
        public void Error(string message) => Toast(ToastType.Warning, null, message);
        public void Success(string title, string message) => Toast(ToastType.Success, title, message);

        public void Toast(ToastType type, string title, string message)
        {
            Debug.Log($"[CarSales] {title} {message}");
            if (type == ToastType.Warning) CarSalesServices.PlaySfx(Sfx.Error, 0.6f);
            if (!toastArea) return;
            NotificationToast.Create(toastArea, type, title, message);
            int extra = toastArea.childCount - 4;
            for (int i = 0; i < extra; i++) Destroy(toastArea.GetChild(i).gameObject);
        }
    }
}
