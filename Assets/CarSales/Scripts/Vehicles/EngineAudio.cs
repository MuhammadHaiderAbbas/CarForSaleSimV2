using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Engine sound made in code (no audio files needed): a looping engine tone pitched by rpm and louder under load.
    /// Electric cars get a quiet motor whine. Replace with recorded engine loops later (spec §6.15).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        static AudioClip combustionClip, electricClip;

        IVehicleDriver driver;
        AudioSource source;
        float idle, redline;
        bool electric;

        public void Init(IVehicleDriver vehicle, VehicleProfile profile)
        {
            driver = vehicle;
            electric = profile != null && profile.IsElectric;
            idle = profile != null ? profile.engine.idleRpm : 900f;
            redline = profile != null ? (electric ? profile.electric.maxRpm : profile.engine.redlineRpm) : 6000f;
            source = GetComponent<AudioSource>();
            source.clip = electric ? ElectricClip() : CombustionClip();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0.6f;
            source.minDistance = 4f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.Play();
        }

        void Update()
        {
            if (driver == null || !source) return;
            float rpm01 = Mathf.Clamp01((driver.Rpm - (electric ? 0f : idle)) / Mathf.Max(1f, redline - (electric ? 0f : idle)));
            bool running = !driver.OutOfFuel;
            float targetPitch = electric ? 0.4f + rpm01 * 1.6f : 0.6f + rpm01 * 1.5f;
            float targetVolume = running ? (electric ? 0.05f + 0.2f * rpm01 : 0.16f + 0.34f * driver.Load + 0.1f * rpm01) : 0f;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            source.pitch = Mathf.Lerp(source.pitch, targetPitch, k);
            source.volume = Mathf.Lerp(source.volume, targetVolume * GameSettings.GroupVolume(AudioManager.GroupVehicles), k);
        }

        static AudioClip CombustionClip()
        {
            if (combustionClip) return combustionClip;
            // 1 second of a 40 Hz "firing" tone with harmonics; whole cycles, so it loops without clicks.
            var data = new float[SampleRate];
            var rng = new System.Random(3);
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                for (int h = 1; h <= 7; h++) s += Mathf.Sin(2f * Mathf.PI * 40f * h * t) / h;
                float firing = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 20f * t);      // uneven combustion pulses
                data[i] = (s * 0.35f * firing) + (float)(rng.NextDouble() - 0.5) * 0.04f;
            }
            combustionClip = AudioClip.Create("EngineLoop", data.Length, 1, SampleRate, false);
            combustionClip.SetData(data, 0);
            return combustionClip;
        }

        static AudioClip ElectricClip()
        {
            if (electricClip) return electricClip;
            var data = new float[SampleRate];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 660f * t) * 0.08f;
            }
            electricClip = AudioClip.Create("MotorWhine", data.Length, 1, SampleRate, false);
            electricClip.SetData(data, 0);
            return electricClip;
        }
    }
}
