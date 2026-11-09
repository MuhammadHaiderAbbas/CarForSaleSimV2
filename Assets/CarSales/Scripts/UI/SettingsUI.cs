using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Works in-game (through UIManager) and in the main menu (no UIManager).
    /// Phase 10: tabs - General, Display, Audio, Driving, Controls (key rebinding).
    /// </summary>
    public class SettingsUI : UIPanel
    {
        Slider sensitivity, volume;
        TextMeshProUGUI sensitivityLabel, volumeLabel, drivingLabel, gearboxLabel, absLabel, tcsLabel, escLabel, themeLabel, scaleLabel, trafficLabel, perfLabel;
        TextMeshProUGUI subtitlesLabel, languageLabel, difficultyLabel;
        Slider uiScale;
        readonly Slider[] groupSliders = new Slider[5];
        readonly TextMeshProUGUI[] groupLabels = new TextMeshProUGUI[5];
        readonly List<RectTransform> pages = new List<RectTransform>();
        RectTransform controlsList;
        InputActionRebindingExtensions.RebindingOperation rebinding;
        InputAction rebindAction;
        bool rebindWasEnabled;
        public GameObject showOnBackInMenu;

        public static readonly string[] GroupNames = { "Music", "Sound effects", "Ambience", "Vehicles", "Interface" };
        public static readonly string[] DifficultyNames = { "EASY", "NORMAL", "HARD", "REALISTIC", "CUSTOM" };

        public override void Build()
        {
            var body = UIFactory.Window(Root, Loc.T("settings.title", "Settings"), new Vector2(760f, 920f), Back);
            UIFactory.Vertical(body, 0, 12f);
            string[] tabNames =
            {
                Loc.T("settings.tab.general", "General"), Loc.T("settings.tab.display", "Display"), Loc.T("settings.tab.audio", "Audio"),
                Loc.T("settings.tab.driving", "Driving"), Loc.T("settings.tab.controls", "Controls")
            };
            var tabs = UIFactory.Tabs(body, tabNames, ShowPage, 44f, 0f, 16f);
            for (int i = 0; i < tabNames.Length; i++)
            {
                var page = UIFactory.VCol(body, 10f, "Page" + i);
                UIFactory.LE(page, flexH: 1);
                pages.Add(page);
            }

            BuildGeneral(pages[0]);
            BuildDisplay(pages[1]);
            BuildAudio(pages[2]);
            BuildDriving(pages[3]);
            BuildControls(pages[4]);

            UIFactory.Btn(body, Loc.T("common.back", "Back"), UIFactory.Neutral, Back, 52f);
            tabs.Select(0);
        }

        void ShowPage(int index)
        {
            CancelRebind();
            for (int i = 0; i < pages.Count; i++) pages[i].gameObject.SetActive(i == index);
            if (index == 4) RefreshControls();
        }

        // ------------------------------------------------------------------ GENERAL
        void BuildGeneral(Transform p)
        {
            sensitivityLabel = UIFactory.Text(p, "", 21, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            sensitivity = UIFactory.MakeSlider(p, UIFactory.Accent, 32f);
            sensitivity.minValue = 0.02f; sensitivity.maxValue = 0.5f;
            sensitivity.onValueChanged.AddListener(v => { GameSettings.MouseSensitivity = v; UpdateLabels(); });
            UIFactory.Btn(p, "", UIFactory.AccentDark, () => { GameSettings.Subtitles = !GameSettings.Subtitles; UpdateLabels(); }, out subtitlesLabel, 48f, 18f);
            UIFactory.Btn(p, "", UIFactory.AccentDark, () =>
            {
                var langs = Loc.Available();
                int i = System.Array.IndexOf(langs, GameSettings.Language);
                GameSettings.Language = langs[(i + 1) % langs.Length];
                UpdateLabels();
            }, out languageLabel, 48f, 18f);
            UIFactory.Btn(p, "", UIFactory.AccentDark, () => { GameSettings.Difficulty = (GameSettings.Difficulty + 1) % (BalanceSettings.HasCustom ? 5 : 4); UpdateLabels(); }, out difficultyLabel, 48f, 18f);
            var note = UIFactory.Text(p, Loc.T("settings.difficulty.note", "Difficulty applies when you start a new game (starting money, customer budgets, repair costs, footfall). Language applies to screens opened after the change."), 15, UIFactory.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(note, h: 48);
            UIFactory.Spacer(p);
        }

        // ------------------------------------------------------------------ DISPLAY
        void BuildDisplay(Transform p)
        {
            scaleLabel = UIFactory.Text(p, "", 19, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            uiScale = UIFactory.MakeSlider(p, UIFactory.Accent, 28f);
            uiScale.minValue = 0.8f; uiScale.maxValue = 1.3f;
            uiScale.onValueChanged.AddListener(v => { GameSettings.UiScale = Mathf.Round(v * 20f) / 20f; UpdateLabels(); });
            UIFactory.Btn(p, "", UIFactory.AccentDark, () =>
            {
                GameSettings.Theme = (UIThemeChoice)(((int)GameSettings.Theme + 1) % 5);
                UpdateLabels();
            }, out themeLabel, 48f, 17f);
            UIFactory.Btn(p, "", UIFactory.AccentDark, () => { GameSettings.TrafficLevel = (GameSettings.TrafficLevel + 1) % 4; UpdateLabels(); }, out trafficLabel, 46f, 17f);
            UIFactory.Btn(p, "", UIFactory.Neutral, () => { GameSettings.ShowPerformance = !GameSettings.ShowPerformance; UpdateLabels(); }, out perfLabel, 46f, 17f);
            UIFactory.Spacer(p);
        }

        // ------------------------------------------------------------------ AUDIO
        void BuildAudio(Transform p)
        {
            volumeLabel = UIFactory.Text(p, "", 21, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            volume = UIFactory.MakeSlider(p, UIFactory.Good, 32f);
            volume.minValue = 0f; volume.maxValue = 1f;
            volume.onValueChanged.AddListener(v => { GameSettings.Volume = v; UpdateLabels(); });
            for (int g = 0; g < GroupNames.Length; g++)
            {
                int group = g;
                groupLabels[g] = UIFactory.Text(p, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
                groupSliders[g] = UIFactory.MakeSlider(p, UIFactory.Accent, 26f);
                groupSliders[g].minValue = 0f; groupSliders[g].maxValue = 1f;
                groupSliders[g].onValueChanged.AddListener(v => { GameSettings.SetGroupVolume(group, v); AudioManager.ApplyGroupVolumes(); UpdateLabels(); });
            }
            UIFactory.Spacer(p);
        }

        // ------------------------------------------------------------------ DRIVING
        void BuildDriving(Transform p)
        {
            UIFactory.Btn(p, "", UIFactory.AccentDark, () =>
            {
                GameSettings.Driving = GameSettings.Driving == DrivingModel.Realistic ? DrivingModel.Arcade : DrivingModel.Realistic;
                UpdateLabels();
            }, out drivingLabel, 52f, 19f);
            UIFactory.Btn(p, "", UIFactory.AccentDark, () => { GameSettings.ManualGearbox = !GameSettings.ManualGearbox; UpdateLabels(); }, out gearboxLabel, 52f, 19f);
            var assists = UIFactory.HRow(p, 52f, 10f);
            UIFactory.Btn(assists, "", UIFactory.Neutral, () => { GameSettings.Abs = !GameSettings.Abs; UpdateLabels(); }, out absLabel, 52f, 18f);
            UIFactory.Btn(assists, "", UIFactory.Neutral, () => { GameSettings.TractionControl = !GameSettings.TractionControl; UpdateLabels(); }, out tcsLabel, 52f, 18f);
            UIFactory.Btn(assists, "", UIFactory.Neutral, () => { GameSettings.StabilityControl = !GameSettings.StabilityControl; UpdateLabels(); }, out escLabel, 52f, 18f);
            var note = UIFactory.Text(p, Loc.T("settings.driving.note", "Applies the next time you get into a car. F3 while driving shows telemetry."), 15, UIFactory.TextDim);
            UIFactory.LE(note, h: 24);
            UIFactory.Spacer(p);
        }

        // ------------------------------------------------------------------ CONTROLS
        void BuildControls(Transform p)
        {
            var hint = UIFactory.Text(p, Loc.T("settings.controls.hint", "Click a key to change it, then press the new key (Escape cancels). Saved for every save game."), 15, UIFactory.TextDim);
            hint.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(hint, h: 44);
            controlsList = UIFactory.ScrollList(p, 6f, "Bindings");
            UIFactory.Btn(p, Loc.T("settings.controls.reset", "Reset all keys to default"), UIFactory.Bad, () =>
            {
                CancelRebind();
                CarSalesInput.ResetOverrides();
                RefreshControls();
            }, 46f, 17f);
        }

        void RefreshControls()
        {
            if (!controlsList) return;
            UIFactory.ClearChildren(controlsList);
            int shown = 0;
            foreach (var action in CarSalesInput.AllActions())
            {
                if (action == null) continue;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b.isComposite) continue;
                    string partName = b.isPartOfComposite ? $" ({b.name})" : "";
                    var row = UIFactory.HRow(controlsList, 40f, 10f);
                    var name = UIFactory.Text(row, Pretty(action.name) + partName, 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
                    UIFactory.LE(name, flexW: 1);
                    string keyText = action.GetBindingDisplayString(i);
                    if (string.IsNullOrEmpty(keyText)) keyText = "-";
                    int index = i;
                    var act = action;
                    TextMeshProUGUI keyLabel = null;
                    var btn = UIFactory.Btn(row, keyText, UIFactory.CardLight, () => StartRebind(act, index, keyLabel), out keyLabel, 40f, 16f);
                    UIFactory.LE(btn, h: 40, w: 220, flexW: 0);
                    shown++;
                }
            }
            if (shown == 0) UIFactory.EmptyState(controlsList, Loc.T("settings.controls.none", "No controls found"), Loc.T("settings.controls.none.hint", "Load a game once so the input actions are linked."));
        }

        static string Pretty(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < actionName.Length; i++)
            {
                char c = actionName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(actionName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return Loc.T("action." + actionName, sb.ToString());
        }

        void StartRebind(InputAction action, int bindingIndex, TextMeshProUGUI label)
        {
            CancelRebind();
            if (action == null) return;
            if (label) label.text = Loc.T("settings.controls.press", "Press a key...");
            bool wasEnabled = action.enabled;
            rebindAction = action;
            rebindWasEnabled = wasEnabled;
            action.Disable();
            rebinding = action.PerformInteractiveRebinding(bindingIndex);
            if (rebinding == null) { if (wasEnabled) action.Enable(); RefreshControls(); return; }
            rebinding.WithControlsExcluding("<Mouse>/position")
                     .WithControlsExcluding("<Mouse>/delta")
                     .WithCancelingThrough("<Keyboard>/escape")
                     .OnMatchWaitForAnother(0.1f)
                     .OnComplete(op => { FinishRebind(action, wasEnabled); CarSalesInput.SaveOverrides(); })
                     .OnCancel(op => FinishRebind(action, wasEnabled))
                     .Start();
        }

        void FinishRebind(InputAction action, bool enable)
        {
            if (rebinding != null) { rebinding.Dispose(); rebinding = null; }
            rebindAction = null;
            if (enable && action != null) action.Enable();
            RefreshControls();
        }

        void CancelRebind()
        {
            if (rebinding == null) return;
            var op = rebinding;
            rebinding = null;
            op.Dispose();
            if (rebindAction != null && rebindWasEnabled) rebindAction.Enable();
            rebindAction = null;
        }

        void OnDisable() => CancelRebind();

        // ------------------------------------------------------------------ COMMON
        void OnEnable()
        {
            if (!sensitivity) return;
            sensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            volume.SetValueWithoutNotify(GameSettings.Volume);
            if (uiScale) uiScale.SetValueWithoutNotify(GameSettings.UiScale);
            for (int g = 0; g < groupSliders.Length; g++) if (groupSliders[g]) groupSliders[g].SetValueWithoutNotify(GameSettings.GroupVolume(g));
            UpdateLabels();
        }

        public void Show()
        {
            if (CarSalesServices.UI) OpenSelf();
            else gameObject.SetActive(true);
        }

        void Back()
        {
            CancelRebind();
            if (CarSalesServices.UI && CarSalesServices.UI.pause) { CarSalesServices.UI.pause.Show(); return; }
            gameObject.SetActive(false);
            if (showOnBackInMenu) showOnBackInMenu.SetActive(true);
        }

        static string OnOff(bool on) => on ? Loc.T("common.on", "ON") : Loc.T("common.off", "OFF");

        void UpdateLabels()
        {
            sensitivityLabel.text = $"{Loc.T("settings.sensitivity", "Mouse sensitivity")}: {GameSettings.MouseSensitivity:0.00}";
            volumeLabel.text = $"{Loc.T("settings.volume", "Master volume")}: {GameSettings.Volume * 100f:0}%";
            if (!drivingLabel) return;
            for (int g = 0; g < groupLabels.Length; g++)
                if (groupLabels[g]) groupLabels[g].text = $"{Loc.T("settings.group." + g, GroupNames[g])}: {GameSettings.GroupVolume(g) * 100f:0}%";
            subtitlesLabel.text = $"{Loc.T("settings.subtitles", "Subtitles")}: {OnOff(GameSettings.Subtitles)}";
            languageLabel.text = $"{Loc.T("settings.language", "Language")}: {GameSettings.Language.ToUpper()}";
            difficultyLabel.text = $"{Loc.T("settings.difficulty", "Difficulty (new games)")}: {DifficultyNames[GameSettings.Difficulty]}";
            scaleLabel.text = $"{Loc.T("settings.uiscale", "Interface size")}: {GameSettings.UiScale * 100f:0}%";
            string theme = GameSettings.Theme == UIThemeChoice.ColourBlind ? "COLOUR-BLIND SAFE" : GameSettings.Theme.ToString().ToUpper();
            themeLabel.text = $"{Loc.T("settings.theme", "Theme")}: {theme}  ({Loc.T("settings.theme.note", "applies after loading / main menu")})" +
                              (GameSettings.Theme == UIThemeChoice.Custom ? "  - Resources/CarSalesUITheme" : "");
            drivingLabel.text = GameSettings.Driving == DrivingModel.Realistic ? Loc.T("settings.physics.realistic", "Physics: REALISTIC (wheels, gearbox, weight)") : Loc.T("settings.physics.arcade", "Physics: ARCADE (simple)");
            gearboxLabel.text = GameSettings.ManualGearbox ? Loc.T("settings.gearbox.manual", "Gearbox: MANUAL (Left Shift up, Left Ctrl down)") : Loc.T("settings.gearbox.auto", "Gearbox: AUTOMATIC");
            absLabel.text = "ABS: " + OnOff(GameSettings.Abs);
            tcsLabel.text = $"{Loc.T("settings.tcs", "Traction control")}: {OnOff(GameSettings.TractionControl)}";
            escLabel.text = $"{Loc.T("settings.esc", "Stability")}: {OnOff(GameSettings.StabilityControl)}";
            string[] traffic = { "OFF", "LOW", "NORMAL", "BUSY" };
            trafficLabel.text = $"{Loc.T("settings.traffic", "Traffic & people")}: {traffic[GameSettings.TrafficLevel]}";
            perfLabel.text = $"{Loc.T("settings.perf", "Frame-rate overlay")}: {OnOff(GameSettings.ShowPerformance)}";
        }
    }
}
