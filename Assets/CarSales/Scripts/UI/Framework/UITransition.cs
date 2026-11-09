using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Fades and slightly scales a window in when it opens (theme's openDuration, real time so it also works while
    /// the game is paused). With a gamepad connected, it also puts the focus on the window's first button.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UITransition : MonoBehaviour
    {
        CanvasGroup group;
        float t, duration;
        bool playing;

        public static void Play(GameObject panel)
        {
            if (!panel) return;
            var tr = panel.GetComponent<UITransition>() ?? panel.AddComponent<UITransition>();
            tr.Begin();
        }

        void Begin()
        {
            group = GetComponent<CanvasGroup>();
            duration = UITheme.Current.openDuration;
            t = 0f;
            playing = duration > 0.001f;
            Apply(playing ? 0f : 1f);
            FocusFirst();
        }

        void Update()
        {
            if (!playing) return;
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            Apply(1f - (1f - k) * (1f - k));   // ease out
            if (k >= 1f) playing = false;
        }

        void OnDisable() => Apply(1f);

        void Apply(float k)
        {
            if (group) group.alpha = k;
            transform.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, k);
        }

        void FocusFirst()
        {
            if (Gamepad.current == null || !EventSystem.current) return;
            foreach (var s in GetComponentsInChildren<Selectable>())
            {
                if (!s.IsInteractable() || !s.isActiveAndEnabled) continue;
                EventSystem.current.SetSelectedGameObject(s.gameObject);
                return;
            }
        }
    }
}
