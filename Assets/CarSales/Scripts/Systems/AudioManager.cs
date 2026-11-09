using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Sound effects generated in code (no audio files needed).
    /// To use real sounds, assign clips in the "Overrides" list.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [System.Serializable]
        public class SfxOverride { public Sfx sfx; public AudioClip clip; }

        [SerializeField] List<SfxOverride> overrides = new List<SfxOverride>();
        [SerializeField, Range(0f, 1f)] float sfxVolume = 0.7f;

        const int Rate = 44100;
        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        AudioSource source;

        void Awake()
        {
            CarSalesServices.Audio = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            GameSettings.Apply();
            BuildClips();
        }

        public void Play(Sfx sfx, float volume = 1f)
        {
            if (clips.TryGetValue(sfx, out var clip) && clip) source.PlayOneShot(clip, volume * sfxVolume * GameSettings.GroupVolume(GroupOf(sfx)));
        }

        // Phase 10 sound groups (Settings > Audio): 0 music, 1 sound effects, 2 ambience, 3 vehicles, 4 interface.
        public const int GroupMusic = 0, GroupSfx = 1, GroupAmbience = 2, GroupVehicles = 3, GroupInterface = 4;

        /// <summary>Clicks, notifications and errors are interface sounds; the rest are sound effects.</summary>
        public static int GroupOf(Sfx sfx) => sfx == Sfx.Click || sfx == Sfx.Notify || sfx == Sfx.Error ? GroupInterface : GroupSfx;

        /// <summary>Raised when a group slider moves, so looping sources (engines, ambience) pick up the new level.</summary>
        public static event System.Action OnGroupVolumesChanged;
        public static void ApplyGroupVolumes() => OnGroupVolumesChanged?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnGroupVolumesChanged = null;

        void BuildClips()
        {
            clips[Sfx.Click]     = Notes("Click", new[] { 1400f }, 0.035f, 0.35f, 0);
            clips[Sfx.Coin]      = Notes("Coin", new[] { 988f, 1319f }, 0.09f, 0.5f, 1);
            clips[Sfx.Purchase]  = Notes("Purchase", new[] { 523f, 659f, 784f }, 0.08f, 0.5f, 1);
            clips[Sfx.Error]     = Notes("Error", new[] { 220f, 165f }, 0.14f, 0.45f, 2);
            clips[Sfx.Notify]    = Notes("Notify", new[] { 880f, 1175f }, 0.07f, 0.35f, 0);
            clips[Sfx.DoorChime] = Notes("DoorChime", new[] { 659f, 523f }, 0.35f, 0.4f, 3);
            clips[Sfx.Success]   = Notes("Success", new[] { 523f, 659f, 784f, 1047f }, 0.1f, 0.5f, 1);
            clips[Sfx.Crash]     = Noise("Crash", 0.35f, 0.6f);
            foreach (var o in overrides) if (o.clip) clips[o.sfx] = o.clip;
            // Phase 10: Resources/CarSalesAudioLibrary replaces any sound without touching the scene.
            var lib = AudioLibrary.Load();
            if (lib != null) foreach (var o in lib.sfx) if (o != null && o.clip) clips[o.sfx] = o.clip;
        }

        // wave: 0 sine, 1 sine+harmonics (bright), 2 square-ish (buzz), 3 bell (long decay)
        static AudioClip Notes(string name, float[] freqs, float noteLength, float volume, int wave)
        {
            int perNote = Mathf.CeilToInt(noteLength * Rate);
            int tail = wave == 3 ? Rate / 2 : Rate / 20;
            var data = new float[perNote * freqs.Length + tail];
            for (int n = 0; n < freqs.Length; n++)
            {
                int length = perNote + (n == freqs.Length - 1 ? tail : perNote / 2);
                for (int i = 0; i < length && n * perNote + i < data.Length; i++)
                {
                    float t = i / (float)Rate;
                    float phase = 2f * Mathf.PI * freqs[n] * t;
                    float s;
                    switch (wave)
                    {
                        case 1: s = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.2f + Mathf.Sin(phase * 3f) * 0.1f; break;
                        case 2: s = Mathf.Sign(Mathf.Sin(phase)) * 0.5f; break;
                        case 3: s = Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 2.76f) * 0.2f; break;
                        default: s = Mathf.Sin(phase); break;
                    }
                    float attack = Mathf.Clamp01(t / 0.005f);
                    float decay = Mathf.Exp(-t * (wave == 3 ? 4f : 18f));
                    data[n * perNote + i] += s * attack * decay * volume;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Noise(string name, float length, float volume)
        {
            var data = new float[Mathf.CeilToInt(length * Rate)];
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                low = Mathf.Lerp(low, Random.Range(-1f, 1f), 0.15f);   // low-pass "thump"
                data[i] = low * Mathf.Exp(-t * 9f) * volume * 2f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
