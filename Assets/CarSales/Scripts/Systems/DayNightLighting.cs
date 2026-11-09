using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Moves the sun with the 24h clock (sunrise 06:00, sunset 20:30), dims the sky at night and refreshes the
    /// ambient light from the sky as the day goes on. Untick "Drive Sun" in a city with its own day/night.
    /// </summary>
    public class DayNightLighting : MonoBehaviour
    {
        [SerializeField] bool driveSun = true;
        [SerializeField] Light sun;
        [SerializeField] float sunrise = 6f, sunset = 20.5f;
        [SerializeField] float maxIntensity = 1.3f;
        [SerializeField] Color noonColor = new Color(1f, 0.96f, 0.9f), eveningColor = new Color(1f, 0.55f, 0.3f);
        [Tooltip("Sky brightness at midnight, relative to day (skies with an _Exposure property, e.g. HDRI / procedural).")]
        [SerializeField, Range(0f, 1f)] float nightSkyExposure = 0.08f;
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        float yaw;
        float baseAmbient = 1f, baseExposure = 1f, giTimer;
        Material sky;

        void Start()
        {
            if (!sun) sun = RenderSettings.sun;
            if (!sun)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun) yaw = sun.transform.eulerAngles.y;
            baseAmbient = RenderSettings.ambientIntensity;
            // Work on a copy so the sky material asset is never changed by Play mode.
            if (RenderSettings.skybox && RenderSettings.skybox.HasProperty(ExposureId))
            {
                sky = new Material(RenderSettings.skybox) { name = RenderSettings.skybox.name + " (runtime)" };
                baseExposure = sky.GetFloat(ExposureId);
                RenderSettings.skybox = sky;
            }
        }

        void Update()
        {
            if (!driveSun || !sun || !CarSalesServices.DayCycle) return;
            float h = CarSalesServices.DayCycle.HourOfDay;
            float t = Mathf.InverseLerp(sunrise, sunset, h);
            bool day = h >= sunrise && h <= sunset;
            float height = day ? Mathf.Sin(t * Mathf.PI) : 0f;
            sun.transform.rotation = Quaternion.Euler(day ? Mathf.Lerp(4f, 176f, t) : -25f, yaw, 0f);
            sun.intensity = (day ? Mathf.Lerp(0.15f, maxIntensity, Mathf.Clamp01(height * 1.5f)) : 0.03f) * (CarSalesServices.World ? CarSalesServices.World.SunFactor : 1f);
            sun.color = Color.Lerp(eveningColor, noonColor, Mathf.Clamp01(height * 2f));
            RenderSettings.ambientIntensity = Mathf.Lerp(0.25f, baseAmbient, day ? Mathf.Clamp01(height * 2f + 0.2f) : 0f);
            if (sky)
            {
                float light = day ? Mathf.Clamp01(height * 2.5f + 0.15f) : 0f;
                sky.SetFloat(ExposureId, baseExposure * Mathf.Lerp(nightSkyExposure, 1f, light));
                giTimer -= Time.deltaTime;
                if (giTimer <= 0f) { giTimer = 3f; DynamicGI.UpdateEnvironment(); }   // ambient & reflections follow the sky
            }
        }

        void OnDestroy() { if (sky) Destroy(sky); }
    }
}
