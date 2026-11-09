using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Phase 10 credits: the game's own credits plus every imported asset that needs attribution (Asset Studio registry).
    /// Opened from the main menu and the pause menu. The build tool writes the same text to CREDITS.txt.
    /// </summary>
    public static class CreditsUI
    {
        public const string GameName = "Car For Sale Simulator";

        /// <summary>The full credits text (plain text, no rich-text tags).</summary>
        public static string Text()
        {
            var sb = new StringBuilder();
            sb.Append(GameName).Append('\n');
            string company = Application.companyName;
            if (!string.IsNullOrEmpty(company) && company != "DefaultCompany") sb.Append("by ").Append(company).Append('\n');
            sb.Append("Version ").Append(Application.version).Append("\n\n");
            sb.Append("Made with Unity (Universal Render Pipeline), TextMesh Pro, the Input System and AI Navigation.\n");
            sb.Append("Sounds and ambience generated in code unless replaced in the Audio Library.\n\n");
            var reg = AssetRegistry.Load();
            if (reg) sb.Append(reg.CreditsText(GameName));
            else sb.Append("No third-party assets.\n");
            sb.Append("\nThank you for playing.\n");
            return sb.ToString();
        }

        /// <summary>Shows the credits over <paramref name="parent"/> (a canvas). Close returns to the caller.</summary>
        public static GameObject Show(Transform parent, UnityAction onClose = null)
        {
            var overlay = UIFactory.Panel(parent, "Credits", UIFactory.Overlay, 0f);
            UIFactory.Stretch(overlay.rectTransform);
            var root = overlay.rectTransform;
            UnityAction close = () => { Object.Destroy(overlay.gameObject); onClose?.Invoke(); };
            var body = UIFactory.Window(root, Loc.T("credits.title", "Credits"), new Vector2(820f, 880f), close);
            UIFactory.Vertical(body, 0, 12f);
            var list = UIFactory.ScrollList(body, 8f, "CreditsScroll");
            var text = UIFactory.Text(list, Text(), 18, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;
            var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIFactory.Btn(body, Loc.T("common.close", "Close"), UIFactory.Neutral, close, 52f);
            return overlay.gameObject;
        }
    }
}
