using UnityEngine;

namespace CarSales
{
    public enum AmbienceZone { Street = 0, Showroom = 1, Workshop = 2, AuctionHall = 3 }

    /// <summary>
    /// Phase 10 ambience: works out where the player is (street, a dealership showroom, the workshop, the auction hall),
    /// crossfades a looping background bed for that place and lays rain on top when the weather is wet and you are
    /// outdoors. Uses clips from <see cref="AudioLibrary"/> where set, otherwise loops generated in code.
    /// Volume follows Settings > Audio > Ambience (and Music for the optional music slot).
    /// </summary>
    public class AmbienceManager : MonoBehaviour
    {
        const int Rate = 22050;
        const float Fade = 1.5f;
        AudioSource bedA, bedB, rainSource, musicSource;
        bool aIsCurrent = true;
        AmbienceZone zone = (AmbienceZone)(-1);
        float checkTimer;
        readonly AudioClip[] generated = new AudioClip[4];
        AudioClip generatedRain;
        Transform listener;

        public AmbienceZone Zone => zone;
        public static AmbienceManager Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            bedA = MakeSource("AmbienceA");
            bedB = MakeSource("AmbienceB");
            rainSource = MakeSource("Rain");
            musicSource = MakeSource("Music");
            var lib = AudioLibrary.Load();
            if (lib != null && lib.music) { musicSource.clip = lib.music; musicSource.Play(); }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        AudioSource MakeSource(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            return s;
        }

        void Update()
        {
            checkTimer -= Time.unscaledDeltaTime;
            if (checkTimer <= 0f)
            {
                checkTimer = 0.5f;
                var z = Detect();
                if (z != zone) SwitchTo(z);
            }
            float amb = GameSettings.GroupVolume(AudioManager.GroupAmbience);
            float k = Mathf.Clamp01(Time.unscaledDeltaTime / Fade);
            var cur = aIsCurrent ? bedA : bedB;
            var old = aIsCurrent ? bedB : bedA;
            cur.volume = Mathf.MoveTowards(cur.volume, BedLevel(zone) * amb, k);
            old.volume = Mathf.MoveTowards(old.volume, 0f, k);
            if (old.volume <= 0f && old.isPlaying) old.Stop();

            bool wet = CarSalesServices.World && CarSales.Sim.WorldSim.IsWet(CarSalesServices.World.Weather);
            float rainTarget = wet ? (zone == AmbienceZone.Street ? 0.35f : 0.1f) * amb : 0f;   // muffled indoors
            if (rainTarget > 0f && !rainSource.isPlaying)
            {
                var lib = AudioLibrary.Load();
                rainSource.clip = lib != null && lib.rain ? lib.rain : (generatedRain ? generatedRain : (generatedRain = Generate(-1)));
                rainSource.Play();
            }
            rainSource.volume = Mathf.MoveTowards(rainSource.volume, rainTarget, k);
            if (rainSource.volume <= 0f && rainSource.isPlaying) rainSource.Stop();

            if (musicSource.clip) musicSource.volume = 0.25f * GameSettings.GroupVolume(AudioManager.GroupMusic);
        }

        static float BedLevel(AmbienceZone z) => z == AmbienceZone.AuctionHall ? 0.3f : z == AmbienceZone.Workshop ? 0.22f : z == AmbienceZone.Showroom ? 0.12f : 0.18f;

        void SwitchTo(AmbienceZone z)
        {
            zone = z;
            aIsCurrent = !aIsCurrent;
            var next = aIsCurrent ? bedA : bedB;
            var lib = AudioLibrary.Load();
            var clip = lib != null ? lib.For(z) : null;
            if (!clip) clip = generated[(int)z] ? generated[(int)z] : (generated[(int)z] = Generate((int)z));
            next.clip = clip;
            next.volume = 0f;
            next.time = 0f;
            next.Play();
        }

        AmbienceZone Detect()
        {
            if (!listener)
            {
                var p = FindFirstObjectByType<PlayerInteractor>();
                listener = p ? p.transform : (Camera.main ? Camera.main.transform : null);
                if (!listener) return AmbienceZone.Street;
            }
            Vector3 pos = listener.position;
            if (AuctionHall.Instance && AuctionHall.Instance.Contains(pos)) return AmbienceZone.AuctionHall;
            foreach (var w in WorkStation.All)
                if (w && (w.transform.position - pos).sqrMagnitude < 14f * 14f) return AmbienceZone.Workshop;
            var zoneHere = ServiceZone.At(pos);
            if (zoneHere && zoneHere.type == ServiceType.Workshop) return AmbienceZone.Workshop;
            foreach (var d in DealershipManager.All)
                if (d && (d.transform.position - pos).sqrMagnitude < 22f * 22f) return AmbienceZone.Showroom;
            return AmbienceZone.Street;
        }

        /// <summary>
        /// Simple generated loops (4 s, ends faded into the start so they loop without a click).
        /// -1 rain, 0 street (low rumble), 1 showroom (soft room tone), 2 workshop (hum and the odd clank), 3 auction (murmur).
        /// </summary>
        static AudioClip Generate(int kind)
        {
            int n = Rate * 4;
            var data = new float[n];
            var rng = new System.Random(100 + kind);
            float low = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.02f;
                band += (white - band) * 0.2f;
                float s;
                switch (kind)
                {
                    case -1: s = (white - band) * 0.5f + band * 0.15f; break;
                    case 0: s = low * 2.2f + Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.03f; break;
                    case 1: s = low * 0.9f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.02f; break;
                    case 2:
                        float clankT = t % 1.7f;
                        float clank = clankT < 0.15f ? Mathf.Sin(2f * Mathf.PI * 1250f * clankT) * Mathf.Exp(-clankT * 30f) * 0.35f : 0f;
                        s = low * 1.4f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.05f + clank; break;
                    default:
                        float murmur = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.7f * t) * Mathf.Sin(2f * Mathf.PI * 1.3f * t);
                        s = band * 0.6f * murmur + low * 0.8f; break;
                }
                data[i] = Mathf.Clamp(s, -1f, 1f);
            }
            // Crossfade the last half second into the first so the loop is seamless.
            int fade = Rate / 2;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, trimmed.Length);
            var clip = AudioClip.Create("Ambience" + kind, trimmed.Length, 1, Rate, false);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
