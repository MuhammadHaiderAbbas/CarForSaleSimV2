using System;
using CarSales.Foundation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>Main menu scene. Builds its own UI (put it on any GameObject; a Canvas is added if missing).</summary>
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] string gameScene = "DealershipTest";
        GameObject mainPanel;
        SettingsUI settings;
        Button continueButton, loadButton;
        TextMeshProUGUI infoText;

        // Save slots
        SaveStore store;
        GameObject slotPanel;
        TextMeshProUGUI slotTitle, slotHint;
        readonly Button[] slotButtons = new Button[SaveStore.SlotCount];
        readonly TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[SaveStore.SlotCount];
        readonly Button[] deleteButtons = new Button[SaveStore.SlotCount];
        readonly RawImage[] slotThumbs = new RawImage[SaveStore.SlotCount];
        readonly Texture2D[] thumbTextures = new Texture2D[SaveStore.SlotCount];
        bool slotModeNew;
        int confirmOverwrite = -1, confirmDelete = -1;

        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameSettings.Apply();
            EnsureCanvas();
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            Build();
        }

        void EnsureCanvas()
        {
            if (!GetComponent<Canvas>()) gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetComponent<CanvasScaler>();
            if (!scaler) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameSettings.UiReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
        }

        void Build()
        {
            var panel = UIFactory.Panel(transform, "MainPanel", UIFactory.MenuBg, 1f);
            mainPanel = panel.gameObject;
            UIFactory.Place(panel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(90f, 0f), new Vector2(600f, 840f));
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = UIFactory.Line;
            var v = UIFactory.Vertical(panel, 44, 16f);

            var logo = UIFactory.Text(panel.transform, "AUTO WORLD", 66, UIFactory.TextMain, TextAlignmentOptions.Center, true);
            UIFactory.LE(logo, h: 80);
            var sub = UIFactory.Text(panel.transform, "USED CARS", 24, UIFactory.Accent, TextAlignmentOptions.Center, true);
            UIFactory.LE(sub, h: 30);
            var tag = UIFactory.Text(panel.transform, Loc.T("menu.tagline", "Car For Sale Simulator  -  Buy. Fix. Sell. Build an empire."), 18, UIFactory.TextDim, TextAlignmentOptions.Center);
            UIFactory.LE(tag, h: 50);
            UIFactory.Divider(panel.transform);
            infoText = UIFactory.Text(panel.transform, "", 17, UIFactory.TextDim, TextAlignmentOptions.Center);
            UIFactory.LE(infoText, h: 30);

            continueButton = UIFactory.Btn(panel.transform, Loc.T("menu.continue", "Continue"), UIFactory.Good, ContinueLatest, 70f, 24f);
            UIFactory.Btn(panel.transform, Loc.T("menu.new", "New Game"), UIFactory.Accent, () => OpenSlots(true), 70f, 24f);
            loadButton = UIFactory.Btn(panel.transform, Loc.T("menu.load", "Load Game"), UIFactory.AccentDark, () => OpenSlots(false), 70f, 24f);
            UIFactory.Btn(panel.transform, Loc.T("menu.settings", "Settings"), UIFactory.Neutral, () => { mainPanel.SetActive(false); settings.Show(); }, 70f, 24f);
            UIFactory.Btn(panel.transform, Loc.T("menu.credits", "Credits"), UIFactory.Neutral, () => { mainPanel.SetActive(false); CreditsUI.Show(transform, () => mainPanel.SetActive(true)); }, 56f, 20f);
            UIFactory.Btn(panel.transform, Loc.T("menu.quit", "Quit"), UIFactory.Bad, PauseMenuUI.QuitGame, 70f, 24f);
            UIFactory.Spacer(panel.transform);
            var version = UIFactory.Text(panel.transform, "v" + SaveManager.GameVersion, 14, UIFactory.TextFaint, TextAlignmentOptions.Center);
            UIFactory.LE(version, h: 20);

            var settingsRoot = UIFactory.Rect("Settings", transform);
            UIFactory.Stretch(settingsRoot);
            settings = settingsRoot.gameObject.AddComponent<SettingsUI>();
            settings.Build();
            settings.showOnBackInMenu = mainPanel;
            settingsRoot.gameObject.SetActive(false);

            BuildSlotPanel();
        }

        void BuildSlotPanel()
        {
            var panel = UIFactory.Panel(transform, "SlotPanel", UIFactory.MenuBg, 1f);
            slotPanel = panel.gameObject;
            UIFactory.Place(panel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(90f, 0f), new Vector2(760f, 840f));
            panel.gameObject.AddComponent<Outline>().effectColor = UIFactory.Line;
            UIFactory.Vertical(panel, 40, 18f);
            slotTitle = UIFactory.Text(panel.transform, "", 44, UIFactory.TextMain, TextAlignmentOptions.Center, true);
            UIFactory.LE(slotTitle, h: 60);
            slotHint = UIFactory.Text(panel.transform, "", 17, UIFactory.TextDim, TextAlignmentOptions.Center);
            UIFactory.LE(slotHint, h: 44);
            for (int i = 0; i < SaveStore.SlotCount; i++)
            {
                int slot = i + 1;
                var row = UIFactory.HRow(panel.transform, 132, 12, "Slot" + slot);
                slotButtons[i] = UIFactory.Btn(row, "Slot", UIFactory.CardLight, () => PickSlot(slot), out slotLabels[i], 132f, 20f);
                UIFactory.LE(slotButtons[i], flexW: 1);
                slotLabels[i].alignment = TextAlignmentOptions.MidlineLeft;
                slotLabels[i].margin = new Vector4(232f, 0f, 10f, 0f);
                var frame = UIFactory.Panel(slotButtons[i].transform, "Thumb", UIFactory.PictureBg, 2f);
                UIFactory.Place(frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(200f, 112f));
                frame.raycastTarget = false;
                frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                slotThumbs[i] = UIFactory.Rect("Image", frame.transform).gameObject.AddComponent<RawImage>();
                UIFactory.Stretch(slotThumbs[i].rectTransform);
                slotThumbs[i].raycastTarget = false;
                deleteButtons[i] = UIFactory.Btn(row, Loc.T("common.delete", "Delete"), UIFactory.Bad, () => DeleteSlot(slot), 132f, 18f);
                UIFactory.LE(deleteButtons[i], w: 130);
            }
            UIFactory.Spacer(panel.transform);
            UIFactory.Btn(panel.transform, Loc.T("common.back", "Back"), UIFactory.Neutral, CloseSlots, 64f, 22f);
            slotPanel.SetActive(false);
        }

        void OpenSlots(bool newGame)
        {
            slotModeNew = newGame;
            confirmOverwrite = confirmDelete = -1;
            mainPanel.SetActive(false);
            slotPanel.SetActive(true);
            RefreshSlots();
        }

        void CloseSlots()
        {
            slotPanel.SetActive(false);
            mainPanel.SetActive(true);
            RefreshMain();
        }

        void RefreshSlots()
        {
            slotTitle.text = slotModeNew ? "NEW GAME" : "LOAD GAME";
            slotHint.text = slotModeNew ? "Pick a slot for your new career. Each slot keeps 3 automatic backups."
                                        : "Pick a save to continue. The game saves every night when you sleep.";
            for (int i = 0; i < SaveStore.SlotCount; i++)
            {
                int slot = i + 1;
                var info = store.Peek(slot);
                string head = $"<size=110%><b>SLOT {slot}</b></size>";
                string body;
                if (!info.Exists) body = $"<color={UIFactory.Hex(UIFactory.TextDim)}>Empty</color>";
                else if (!info.Readable) body = $"<color={UIFactory.Hex(UIFactory.Bad)}>Damaged - cannot be loaded</color>";
                else
                {
                    string when = info.SavedAtUtc > DateTime.MinValue ? info.SavedAtUtc.ToLocalTime().ToString("dd MMM yyyy  HH:mm") : "unknown time";
                    body = $"{info.Label}   <color={UIFactory.Hex(UIFactory.Gold)}>{UIFactory.Money(info.Money)}</color>\n" +
                           $"<size=85%><color={UIFactory.Hex(UIFactory.TextDim)}>Saved {when}{(info.FromBackup ? "  (restored from backup)" : "")}</color></size>";
                }
                if (confirmOverwrite == slot) body = $"<color={UIFactory.Hex(UIFactory.Warn)}>Overwrite this save? Click again to confirm.</color>";
                if (confirmDelete == slot) body = $"<color={UIFactory.Hex(UIFactory.Bad)}>Delete this save and its backups? Click Delete again.</color>";
                slotLabels[i].text = head + "\n" + body;
                if (thumbTextures[i]) Destroy(thumbTextures[i]);
                thumbTextures[i] = info.Readable ? SaveThumbnail.Load(store.ThumbnailFile(slot)) : null;
                slotThumbs[i].texture = thumbTextures[i];
                slotThumbs[i].color = thumbTextures[i] ? Color.white : UIFactory.Clear;
                slotButtons[i].interactable = slotModeNew || info.Readable;
                deleteButtons[i].gameObject.SetActive(info.Exists);
            }
        }

        void PickSlot(int slot)
        {
            confirmDelete = -1;
            if (slotModeNew)
            {
                if (store.Exists(slot) && confirmOverwrite != slot) { confirmOverwrite = slot; RefreshSlots(); return; }
                StartGame(slot, true);
            }
            else StartGame(slot, false);
        }

        void DeleteSlot(int slot)
        {
            confirmOverwrite = -1;
            if (confirmDelete != slot) { confirmDelete = slot; RefreshSlots(); return; }
            store.Delete(slot);
            confirmDelete = -1;
            RefreshSlots();
        }

        void ContinueLatest()
        {
            int slot = store.MostRecentSlot();
            if (slot > 0) StartGame(slot, false);
        }

        void RefreshMain()
        {
            int latest = store.MostRecentSlot();
            bool any = false;
            for (int s = 1; s <= SaveStore.SlotCount; s++) any |= store.Peek(s).Readable;
            continueButton.interactable = latest > 0;
            loadButton.interactable = any;
            if (latest > 0)
            {
                var info = store.Peek(latest);
                infoText.text = $"Continue: slot {latest} - {info.Label}, {UIFactory.Money(info.Money)}";
            }
            else infoText.text = "No saved game yet - start a New Game!";
        }

        void OnEnable() => GameSettings.OnUiScaleChanged += ApplyUiScale;
        void OnDisable() => GameSettings.OnUiScaleChanged -= ApplyUiScale;
        void OnDestroy() { foreach (var t in thumbTextures) if (t) Destroy(t); }
        void ApplyUiScale(float _) { var s = GetComponent<CanvasScaler>(); if (s) s.referenceResolution = GameSettings.UiReferenceResolution; }

        void Start()
        {
            store = SaveManager.OpenStore();
            RefreshMain();
        }

        void StartGame(int slot, bool newGame)
        {
            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                infoText.text = slotHint.text = $"Scene '{gameScene}' is not in the build scene list.";
                return;
            }
            GameSettings.ActiveSlot = slot;
            if (newGame) PlayerPrefs.SetInt(GameSettings.NewGameFlagKey, 1);
            SceneManager.LoadScene(gameScene);
        }
    }
}
