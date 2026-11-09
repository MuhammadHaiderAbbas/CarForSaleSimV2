using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Keeps the town within the frame budget (spec §11: 60 fps, 1% lows ≥ 45): when the frame rate stays under 45
    /// for 5 s it thins out traffic and pedestrians (down to 30%); when it's comfortably above 58 it lets them back.
    /// Settings → Frame-rate overlay shows fps, frame time, traffic and pedestrian counts.
    /// </summary>
    public class PerformanceGovernor : MonoBehaviour
    {
        public static float Scale { get; private set; } = 1f;
        public static float SmoothedFps { get; private set; } = 60f;
        float slowFor, fastFor, worstMs, worstTimer;
        TextMeshProUGUI overlay;

        void Update()
        {
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            SmoothedFps = Mathf.Lerp(SmoothedFps, 1f / dt, 0.05f);
            worstMs = Mathf.Max(worstMs, dt * 1000f);
            worstTimer += dt;
            if (SmoothedFps < 45f) { slowFor += dt; fastFor = 0f; }
            else if (SmoothedFps > 58f) { fastFor += dt; slowFor = 0f; }
            else { slowFor = fastFor = 0f; }
            if (slowFor > 5f) { Scale = Mathf.Max(0.3f, Scale - 0.15f); slowFor = 0f; }
            if (fastFor > 10f) { Scale = Mathf.Min(1f, Scale + 0.1f); fastFor = 0f; }
            UpdateOverlay();
        }

        void UpdateOverlay()
        {
            bool show = GameSettings.ShowPerformance && CarSalesServices.UI;
            if (!show) { if (overlay) overlay.gameObject.SetActive(false); return; }
            if (!overlay)
            {
                overlay = UIFactory.Text(CarSalesServices.UI.transform, "", 14, UIFactory.TextMain, TextAlignmentOptions.TopRight, true, "PerfOverlay");
                UIFactory.Place(overlay.rectTransform, Vector2.one, Vector2.one, new Vector2(-24f, -120f), new Vector2(360f, 80f));
                overlay.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = UITheme.Current.shadow;
            }
            overlay.gameObject.SetActive(true);
            if (worstTimer < 0.5f) return;
            var traffic = FindFirstObjectByType<TrafficSystem>();
            var people = FindFirstObjectByType<PedestrianSystem>();
            overlay.text = $"{SmoothedFps:0} fps  {1000f / Mathf.Max(1f, SmoothedFps):0.0} ms  (worst {worstMs:0} ms)\n" +
                           $"traffic {(traffic ? traffic.Cars.Count : 0)}  people {(people ? people.Count : 0)}  density {Scale * 100f:0}%";
            worstMs = 0f;
            worstTimer = 0f;
        }
    }
}
