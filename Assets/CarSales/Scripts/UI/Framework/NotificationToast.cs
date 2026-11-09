using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>A notification card (icon, title, message, close) that fades out by itself.</summary>
    public class NotificationToast : MonoBehaviour
    {
        CanvasGroup group;
        float life = 5f, age;

        public static NotificationToast Create(Transform parent, ToastType type, string title, string message)
        {
            Color color = type == ToastType.Success ? UIFactory.Good : type == ToastType.Warning ? UIFactory.Warn : UIFactory.Accent;
            if (string.IsNullOrEmpty(title))
                title = type == ToastType.Success ? "Success" : type == ToastType.Warning ? "Heads up" : "Notice";

            var card = UIFactory.Panel(parent, "Toast", UIFactory.ToastBg, 1.6f);
            var outline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = UIFactory.WithAlpha(color, 0.5f);
            var h = UIFactory.Horizontal(card, 0, 12f, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(14, 10, 12, 12);
            h.childForceExpandHeight = false;

            UIFactory.Icon(card.transform, color, type == ToastType.Warning ? "!" : "i", 40f, type == ToastType.Success);
            var col = UIFactory.VCol(card.transform, 2f);
            UIFactory.Text(col, title, 19, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.Text(col, message, 16, UIFactory.TextDim, TextAlignmentOptions.Left);

            var toast = card.gameObject.AddComponent<NotificationToast>();
            toast.group = card.gameObject.AddComponent<CanvasGroup>();
            toast.life = Mathf.Clamp(3.5f + message.Length * 0.03f, 4f, 8f);
            var close = UIFactory.Btn(card.transform, "X", UIFactory.Ghost, () => Destroy(card.gameObject), 30f, 15f);
            UIFactory.LE(close, h: 30, w: 30, flexW: 0);
            return toast;
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            if (group) group.alpha = age < 0.2f ? age / 0.2f : Mathf.Clamp01((life - age) / 0.6f);
            if (age >= life) Destroy(gameObject);
        }
    }
}
