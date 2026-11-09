using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Studio;
using UnityEngine;

namespace CarSales
{
    /// <summary>One asset brought in through the Asset Studio.</summary>
    [Serializable]
    public class AssetRecord
    {
        public string id = "";                 // safe name, unique
        public string displayName = "";
        public AssetCategory category;
        public string sourcePath = "";         // model file inside Assets/CarSales/Art/.../Source
        public GameObject prefab;              // the game-ready prefab
        public CarDefinition carDefinition;    // vehicles only
        public UnityEngine.Object mainAsset;   // materials and skies (no prefab)
        public bool useInGame = true;          // characters: appear as customers
        public LicenceInfo licence = new LicenceInfo();
        public string importedAtUtc = "";
        public int version = 1;
        public string notes = "";
        public string jobJson = "";            // Asset Studio settings, to re-open and re-run the setup
    }

    /// <summary>
    /// Every imported asset with its licence (spec §12). Lives in Assets/CarSales/Library/Resources so the game
    /// (credits screen) and the build check can find it. Edited by Tools > Car Sales > Asset Studio.
    /// </summary>
    [CreateAssetMenu(menuName = "CarSales/Asset Registry", fileName = ResourceName)]
    public class AssetRegistry : ScriptableObject
    {
        public const string ResourceName = "CarSalesAssetRegistry";

        public List<AssetRecord> records = new List<AssetRecord>();

        public static AssetRegistry Load() => Resources.Load<AssetRegistry>(ResourceName);

        public AssetRecord Find(string id) => records.Find(r => r != null && r.id == id);

        static List<GameObject> customerModels;

        /// <summary>A random imported character to use as a customer, or null (keeps the built-in capsule look).</summary>
        public static GameObject RandomCustomerModel()
        {
            if (customerModels == null)
            {
                var reg = Load();
                customerModels = reg ? reg.records.Where(r => r != null && r.category == AssetCategory.Character && r.useInGame && r.prefab)
                                                  .Select(r => r.prefab).ToList()
                                     : new List<GameObject>();
            }
            return customerModels.Count == 0 ? null : customerModels[UnityEngine.Random.Range(0, customerModels.Count)];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => customerModels = null;

        /// <summary>The credits text for all imported assets.</summary>
        public string CreditsText(string gameName = "Car For Sale Simulator") =>
            CreditsBuilder.Build(gameName, records.Where(r => r != null)
                                                  .Select(r => new KeyValuePair<string, LicenceInfo>(r.displayName, r.licence)));

        /// <summary>Records whose licence doesn't allow a commercial release yet.</summary>
        public IEnumerable<AssetRecord> ReleaseBlockers() =>
            records.Where(r => r != null && LicencePolicy.Check(r.licence).BlocksRelease);
    }
}
