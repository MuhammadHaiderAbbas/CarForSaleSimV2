using UnityEngine;

namespace CarSales
{
    public enum DrivingModel { Realistic = 0, Arcade = 1 }

    /// <summary>Player preferences (stored in PlayerPrefs, shared by all save games).</summary>
    public static class GameSettings
    {
        const string SensitivityKey = "CarSales.MouseSensitivity";
        const string VolumeKey = "CarSales.Volume";
        public const string NewGameFlagKey = "CarSales.StartNewGame";
        const string ActiveSlotKey = "CarSales.ActiveSlot";
        const string DrivingKey = "CarSales.DrivingModel", AbsKey = "CarSales.ABS", TcsKey = "CarSales.TCS", EscKey = "CarSales.ESC";
        const string ManualKey = "CarSales.ManualGearbox";
        const string ThemeKey = "CarSales.UITheme", UiScaleKey = "CarSales.UIScale";

        static bool loaded;
        static float sensitivity, volume;

        static void Load()
        {
            if (loaded) return;
            sensitivity = PlayerPrefs.GetFloat(SensitivityKey, 0.12f);
            volume = PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            loaded = true;
        }

        public static float MouseSensitivity
        {
            get { Load(); return sensitivity; }
            set { Load(); sensitivity = value; PlayerPrefs.SetFloat(SensitivityKey, value); PlayerPrefs.Save(); }
        }

        public static float Volume
        {
            get { Load(); return volume; }
            set { Load(); volume = value; PlayerPrefs.SetFloat(VolumeKey, value); PlayerPrefs.Save(); AudioListener.volume = value; }
        }

        /// <summary>The save slot the game reads and writes (1-3). Chosen in the main menu.</summary>
        public static int ActiveSlot
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ActiveSlotKey, 1), 1, CarSales.Foundation.SaveStore.SlotCount);
            set { PlayerPrefs.SetInt(ActiveSlotKey, Mathf.Clamp(value, 1, CarSales.Foundation.SaveStore.SlotCount)); PlayerPrefs.Save(); }
        }

        /// <summary>Realistic wheel physics, or the simple v4 arcade handling.</summary>
        public static DrivingModel Driving
        {
            get => (DrivingModel)Mathf.Clamp(PlayerPrefs.GetInt(DrivingKey, 0), 0, 1);
            set { PlayerPrefs.SetInt(DrivingKey, (int)value); PlayerPrefs.Save(); }
        }

        public static bool Abs { get => Flag(AbsKey); set => SetFlag(AbsKey, value); }
        public static bool TractionControl { get => Flag(TcsKey); set => SetFlag(TcsKey, value); }
        public static bool StabilityControl { get => Flag(EscKey); set => SetFlag(EscKey, value); }

        /// <summary>Manual gearbox (Left Shift = up, Left Ctrl = down, R from 1st at a standstill). Default off.</summary>
        public static bool ManualGearbox
        {
            get => PlayerPrefs.GetInt(ManualKey, 0) == 1;
            set => SetFlag(ManualKey, value);
        }

        /// <summary>Colour theme of the UI. Screens use it the next time they are built (load a game / main menu).</summary>
        public static UIThemeChoice Theme
        {
            get => (UIThemeChoice)Mathf.Clamp(PlayerPrefs.GetInt(ThemeKey, 0), 0, 4);
            set { PlayerPrefs.SetInt(ThemeKey, (int)value); PlayerPrefs.Save(); UITheme.Reload(); }
        }

        /// <summary>Size of the whole UI, 0.8 - 1.3 (applies immediately).</summary>
        public static float UiScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, 1f), 0.8f, 1.3f);
            set { PlayerPrefs.SetFloat(UiScaleKey, Mathf.Clamp(value, 0.8f, 1.3f)); PlayerPrefs.Save(); OnUiScaleChanged?.Invoke(UiScale); }
        }
        public static event System.Action<float> OnUiScaleChanged;

        /// <summary>CanvasScaler reference resolution for the current UI scale (bigger scale = smaller reference).</summary>
        public static Vector2 UiReferenceResolution => new Vector2(1920f, 1080f) / UiScale;

        /// <summary>Phase 8b: traffic and pedestrians 0 = off, 1 = low, 2 = normal, 3 = busy.</summary>
        public static int TrafficLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("CarSales.Traffic", 2), 0, 3);
            set { PlayerPrefs.SetInt("CarSales.Traffic", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }
        public static float TrafficMultiplier => TrafficLevel == 0 ? 0f : TrafficLevel == 1 ? 0.5f : TrafficLevel == 2 ? 1f : 1.6f;

        /// <summary>Phase 10: on-screen captions for the auctioneer, customers and sellers.</summary>
        public static bool Subtitles
        {
            get => PlayerPrefs.GetInt("CarSales.Subtitles", 1) == 1;
            set => SetFlag("CarSales.Subtitles", value);
        }

        /// <summary>Phase 10: language of the strings table (Resources/Localization/&lt;code&gt;.json).</summary>
        public static string Language
        {
            get => PlayerPrefs.GetString("CarSales.Language", "en");
            set { PlayerPrefs.SetString("CarSales.Language", value); PlayerPrefs.Save(); Loc.Reload(); }
        }

        /// <summary>Phase 10 sound groups: 0 music, 1 sound effects, 2 ambience, 3 vehicles, 4 interface.</summary>
        public static float GroupVolume(int group) => PlayerPrefs.GetFloat("CarSales.Vol" + group, 1f);
        public static void SetGroupVolume(int group, float v) { PlayerPrefs.SetFloat("CarSales.Vol" + group, Mathf.Clamp01(v)); PlayerPrefs.Save(); }

        /// <summary>Phase 10: difficulty (0 easy, 1 normal, 2 hard, 3 realistic, 4 custom file); used when a new game starts.</summary>
        public static int Difficulty
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("CarSales.Difficulty", 1), 0, 4);
            set { PlayerPrefs.SetInt("CarSales.Difficulty", Mathf.Clamp(value, 0, 4)); PlayerPrefs.Save(); }
        }

        /// <summary>Phase 8b: small frame-time overlay (top right).</summary>
        public static bool ShowPerformance
        {
            get => PlayerPrefs.GetInt("CarSales.ShowPerf", 0) == 1;
            set => SetFlag("CarSales.ShowPerf", value);
        }

        static bool Flag(string key) => PlayerPrefs.GetInt(key, 1) == 1;
        static void SetFlag(string key, bool on) { PlayerPrefs.SetInt(key, on ? 1 : 0); PlayerPrefs.Save(); }

        public static void Apply() => AudioListener.volume = Volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => loaded = false;
    }
}
