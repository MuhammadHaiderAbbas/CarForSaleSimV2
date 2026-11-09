using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 10 accessibility: a caption line at the bottom of the screen for speech (auctioneer calls, what
    /// customers and sellers say). Settings → Subtitles turns it off.
    /// </summary>
    public static class Subtitles
    {
        static TextMeshProUGUI line;
        static float until;
        static string last;

        public static void Say(string speaker, string text, float seconds = 3.5f)
        {
            if (!GameSettings.Subtitles || string.IsNullOrEmpty(text) || text == last && Time.unscaledTime < until) return;
            var ui = CarSalesServices.UI;
            if (!ui) return;
            if (!line)
            {
                var panel = UIFactory.Panel(ui.transform, "Subtitles", UIFactory.HudBgStrong, 3f);
                UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 175f), new Vector2(1000f, 46f));
                panel.raycastTarget = false;
                line = UIFactory.Text(panel.transform, "", 19, UIFactory.TextMain, TextAlignmentOptions.Center, true);
                UIFactory.Stretch(line.rectTransform, 6f);
                line.gameObject.AddComponent<Fader>();
            }
            last = text;
            line.text = string.IsNullOrEmpty(speaker) ? text : $"<color={UIFactory.Hex(UIFactory.Gold)}>{speaker}:</color> {text}";
            line.transform.parent.gameObject.SetActive(true);
            line.transform.parent.SetAsLastSibling();
            until = Time.unscaledTime + seconds;
        }

        class Fader : MonoBehaviour
        {
            void Update() { if (Time.unscaledTime > until) transform.parent.gameObject.SetActive(false); }
        }
    }
}
