using System;
using UnityEngine;

namespace CarSales
{
    public enum UIThemeChoice { Dark = 0, Light = 1, HighContrast = 2, Custom = 3, ColourBlind = 4 }

    /// <summary>
    /// Every colour the UI uses (spec §6.14.1). UIFactory reads these; screens never hard-code colours
    /// (a unit test checks that). Edit a copy as a <see cref="UIThemeAsset"/> to re-skin the whole game.
    /// </summary>
    [Serializable]
    public class UIPalette
    {
        [Header("Surfaces")]
        public Color windowBg, headerBg, cardBg, cardLight, menuBg, hudBg, hudBgStrong, toastBg, pictureBg, overlay, backdrop, ghost, line;
        [Header("Text")]
        public Color textMain, textDim, textFaint, chatPlayer, keyCap, keyCapText;
        [Header("Accents & status")]
        public Color accent, accentDark, neutral, good, warn, bad, gold, info, special, personal, home;
        [Header("Controls")]
        public Color buttonHover, buttonPressed, buttonDisabled, selection, shadow, crosshair;
        [Header("Map")]
        public Color mapBg, mapFrame;
        [Header("Motion")]
        [Tooltip("Seconds for a window to fade/scale in. 0 = no animation.")]
        public float openDuration = 0.16f;

        public UIPalette Copy() => (UIPalette)MemberwiseClone();
    }

    /// <summary>Optional custom theme: Create > CarSales > UI Theme, put it in a Resources folder named "CarSalesUITheme".</summary>
    [CreateAssetMenu(menuName = "CarSales/UI Theme", fileName = UITheme.ResourceName)]
    public class UIThemeAsset : ScriptableObject
    {
        public UIPalette palette = UITheme.Dark();
    }

    public static class UITheme
    {
        public const string ResourceName = "CarSalesUITheme";
        static UIPalette current;
        static UIThemeChoice currentChoice;

        /// <summary>The palette in use (from Settings). Screens built after a change use the new colours.</summary>
        public static UIPalette Current
        {
            get
            {
                var choice = GameSettings.Theme;
                if (current == null || choice != currentChoice) { current = Build(choice); currentChoice = choice; }
                return current;
            }
        }

        public static UIPalette Build(UIThemeChoice choice)
        {
            switch (choice)
            {
                case UIThemeChoice.Light: return Light();
                case UIThemeChoice.HighContrast: return HighContrast();
                case UIThemeChoice.ColourBlind: return ColourBlind();
                case UIThemeChoice.Custom:
                    var asset = Resources.Load<UIThemeAsset>(ResourceName);
                    return asset && asset.palette != null ? asset.palette.Copy() : Dark();
                default: return Dark();
            }
        }

        static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        /// <summary>The original AUTO WORLD dark blue look.</summary>
        public static UIPalette Dark() => new UIPalette
        {
            windowBg = C(0.035f, 0.06f, 0.12f, 0.98f), headerBg = C(0.055f, 0.1f, 0.2f), cardBg = C(0.075f, 0.115f, 0.215f),
            cardLight = C(0.11f, 0.17f, 0.3f), menuBg = C(0.03f, 0.06f, 0.13f, 0.93f), hudBg = C(0.03f, 0.06f, 0.13f, 0.75f),
            hudBgStrong = C(0.03f, 0.06f, 0.13f, 0.86f), toastBg = C(0.06f, 0.1f, 0.19f, 0.97f), pictureBg = C(0.09f, 0.14f, 0.26f),
            overlay = C(0f, 0f, 0f, 0.25f), backdrop = C(0f, 0.01f, 0.04f, 0.6f), ghost = C(1f, 1f, 1f, 0.07f), line = C(0.25f, 0.5f, 0.95f, 0.45f),
            textMain = C(0.93f, 0.96f, 1f), textDim = C(0.62f, 0.7f, 0.83f), textFaint = C(1f, 1f, 1f, 0.4f), chatPlayer = C(0.85f, 0.92f, 1f),
            keyCap = C(1f, 1f, 1f, 0.92f), keyCapText = C(0.08f, 0.1f, 0.16f),
            accent = C(0.13f, 0.47f, 0.95f), accentDark = C(0.09f, 0.28f, 0.6f), neutral = C(0.24f, 0.29f, 0.4f),
            good = C(0.2f, 0.72f, 0.38f), warn = C(0.96f, 0.62f, 0.12f), bad = C(0.88f, 0.28f, 0.28f), gold = C(1f, 0.83f, 0.3f),
            info = C(0.45f, 0.82f, 1f), special = C(0.55f, 0.3f, 0.8f), personal = C(0.1f, 0.6f, 0.65f), home = C(0.2f, 0.75f, 0.6f),
            buttonHover = C(0.82f, 0.9f, 1f), buttonPressed = C(0.65f, 0.72f, 0.85f), buttonDisabled = C(0.55f, 0.55f, 0.6f, 0.55f),
            selection = C(0.2f, 0.5f, 1f, 0.5f), shadow = C(0f, 0f, 0f, 0.6f), crosshair = C(1f, 1f, 1f, 0.85f),
            mapBg = C(0.08f, 0.12f, 0.2f), mapFrame = C(0.05f, 0.08f, 0.14f),
        };

        /// <summary>Bright "showroom" look: light surfaces, dark text.</summary>
        public static UIPalette Light()
        {
            var p = Dark();
            p.windowBg = C(0.96f, 0.97f, 0.99f, 0.98f); p.headerBg = C(0.88f, 0.91f, 0.97f); p.cardBg = C(1f, 1f, 1f);
            p.cardLight = C(0.9f, 0.93f, 0.98f); p.menuBg = C(0.97f, 0.98f, 1f, 0.94f); p.hudBg = C(1f, 1f, 1f, 0.78f);
            p.hudBgStrong = C(1f, 1f, 1f, 0.9f); p.toastBg = C(1f, 1f, 1f, 0.98f); p.pictureBg = C(0.85f, 0.89f, 0.96f);
            p.overlay = C(0f, 0f, 0f, 0.06f); p.backdrop = C(0.1f, 0.12f, 0.18f, 0.45f); p.ghost = C(0f, 0f, 0f, 0.07f); p.line = C(0.13f, 0.4f, 0.85f, 0.35f);
            p.textMain = C(0.08f, 0.1f, 0.16f); p.textDim = C(0.35f, 0.4f, 0.5f); p.textFaint = C(0f, 0f, 0f, 0.4f); p.chatPlayer = C(0.1f, 0.25f, 0.55f);
            p.keyCap = C(0.12f, 0.15f, 0.22f); p.keyCapText = C(1f, 1f, 1f);
            p.neutral = C(0.45f, 0.5f, 0.6f); p.gold = C(0.78f, 0.55f, 0.02f); p.info = C(0.05f, 0.45f, 0.75f);
            p.mapBg = C(0.86f, 0.9f, 0.95f); p.mapFrame = C(0.8f, 0.85f, 0.92f);
            p.crosshair = C(1f, 1f, 1f, 0.9f);
            return p;
        }

        /// <summary>Accessibility: black surfaces, white text, strong distinct status colours.</summary>
        public static UIPalette HighContrast()
        {
            var p = Dark();
            p.windowBg = C(0f, 0f, 0f, 1f); p.headerBg = C(0.08f, 0.08f, 0.08f); p.cardBg = C(0.1f, 0.1f, 0.1f); p.cardLight = C(0.2f, 0.2f, 0.2f);
            p.menuBg = C(0f, 0f, 0f, 0.97f); p.hudBg = C(0f, 0f, 0f, 0.85f); p.hudBgStrong = C(0f, 0f, 0f, 0.95f); p.toastBg = C(0f, 0f, 0f, 1f);
            p.line = C(1f, 1f, 1f, 0.8f); p.textMain = C(1f, 1f, 1f); p.textDim = C(0.85f, 0.85f, 0.85f); p.textFaint = C(1f, 1f, 1f, 0.7f);
            p.accent = C(1f, 0.85f, 0f); p.accentDark = C(0.55f, 0.45f, 0f); p.neutral = C(0.35f, 0.35f, 0.35f);
            p.good = C(0f, 0.85f, 0.45f); p.warn = C(1f, 0.6f, 0f); p.bad = C(1f, 0.25f, 0.4f); p.gold = C(1f, 0.9f, 0.2f);
            p.info = C(0.3f, 0.85f, 1f); p.openDuration = 0f;
            return p;
        }

        /// <summary>
        /// Phase 10 accessibility: status colours that stay distinct with red-green colour blindness
        /// (blue = good, orange = bad, yellow = warning; text always says the status too).
        /// </summary>
        public static UIPalette ColourBlind()
        {
            var p = Dark();
            p.good = C(0.2f, 0.55f, 1f); p.bad = C(1f, 0.5f, 0.05f); p.warn = C(0.95f, 0.85f, 0.2f);
            p.accent = C(0.55f, 0.35f, 0.95f); p.accentDark = C(0.33f, 0.2f, 0.6f); p.info = C(0.6f, 0.85f, 1f);
            return p;
        }

        /// <summary>Forget the cached palette (after changing the setting or the custom asset).</summary>
        public static void Reload() => current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;
    }
}
