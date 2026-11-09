using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 10: replaceable sound slots. Create one with Assets > Create > Car Sales > Audio Library, name it
    /// "CarSalesAudioLibrary" and put it in any Resources folder. Any empty slot keeps the built-in generated sound.
    /// </summary>
    [CreateAssetMenu(menuName = "Car Sales/Audio Library", fileName = "CarSalesAudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        public const string ResourceName = "CarSalesAudioLibrary";

        [Tooltip("Replaces interface / effect sounds (click, coin, purchase...).")]
        public List<AudioManager.SfxOverride> sfx = new List<AudioManager.SfxOverride>();

        [Header("Ambience loops (seamless loops work best)")]
        public AudioClip street;
        public AudioClip showroom;
        public AudioClip workshop;
        public AudioClip auctionHall;
        public AudioClip rain;

        [Header("Music (optional, plays quietly in the background)")]
        public AudioClip music;

        static AudioLibrary cached;
        static bool looked;

        public static AudioLibrary Load()
        {
            if (!looked) { cached = Resources.Load<AudioLibrary>(ResourceName); looked = true; }
            return cached;
        }

        public AudioClip For(AmbienceZone zone)
        {
            switch (zone)
            {
                case AmbienceZone.Showroom: return showroom;
                case AmbienceZone.Workshop: return workshop;
                case AmbienceZone.AuctionHall: return auctionHall;
                default: return street;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cached = null; looked = false; }
    }
}
