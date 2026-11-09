using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Which imported models replace which placeholders in the town (edited in the Asset Studio's Town tab,
    /// stored in Assets/CarSales/Data so Build Everything keeps it). Several models per kind = variety.
    /// </summary>
    [CreateAssetMenu(menuName = "CarSales/Placeholder Replacements", fileName = "PlaceholderReplacements")]
    public class PlaceholderReplacements : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public PlaceholderKind kind;
            public List<string> assetIds = new List<string>();   // AssetRegistry record ids
        }

        public List<Entry> entries = new List<Entry>();

        public Entry For(PlaceholderKind kind)
        {
            var e = entries.Find(x => x != null && x.kind == kind);
            if (e == null) { e = new Entry { kind = kind }; entries.Add(e); }
            return e;
        }

        public bool Any() => entries.Exists(e => e != null && e.assetIds.Count > 0);
    }
}
