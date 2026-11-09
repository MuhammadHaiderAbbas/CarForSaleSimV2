using System.Collections.Generic;

namespace CarSales.Foundation
{
    /// <summary>
    /// Phase 10 strings table: a flat JSON object of key -> text (Resources/Localization/&lt;language&gt;.json).
    /// Engine-free so the table can be tested. Missing keys fall back to the text given at the call site.
    /// </summary>
    public sealed class Strings
    {
        readonly Dictionary<string, string> table = new Dictionary<string, string>();
        public string Language { get; private set; } = "en";
        public int Count => table.Count;
        public IEnumerable<string> Keys => table.Keys;

        /// <summary>Loads a table. Returns false (and keeps it empty) if the JSON is not an object of strings.</summary>
        public bool LoadJson(string json, string language = "en")
        {
            table.Clear();
            Language = language;
            if (string.IsNullOrEmpty(json) || !MiniJson.TryParse(json, out var root, out _) || root.Kind != JsonKind.Object) return false;
            foreach (var f in root.Fields)
                if (f.Value.Kind == JsonKind.String) table[f.Key] = f.Value.Str;
            return true;
        }

        public bool Has(string key) => key != null && table.ContainsKey(key);

        /// <summary>Text for a key, or the fallback (or the key itself) when the table has none.</summary>
        public string T(string key, string fallback = null)
        {
            if (key != null && table.TryGetValue(key, out var s)) return s;
            return fallback ?? key;
        }

        /// <summary>Text with {0}, {1}... filled in.</summary>
        public string Format(string key, string fallback, params object[] args)
        {
            string s = T(key, fallback);
            try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, s, args); }
            catch (System.FormatException) { return s; }
        }

        /// <summary>Keys that appear more than once in the raw JSON (the parser keeps the last one).</summary>
        public static List<string> DuplicateKeys(string json)
        {
            var dups = new List<string>();
            if (!MiniJson.TryParse(json, out var root, out _) || root.Kind != JsonKind.Object) return dups;
            var seen = new HashSet<string>();
            foreach (var f in root.Fields) if (!seen.Add(f.Key) && !dups.Contains(f.Key)) dups.Add(f.Key);
            return dups;
        }
    }
}
