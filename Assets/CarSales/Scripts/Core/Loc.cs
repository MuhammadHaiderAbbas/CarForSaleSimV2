using CarSales.Foundation;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 10 localisation: <c>Loc.T("key", "English text")</c>. Loads Resources/Localization/&lt;GameSettings.Language&gt;.json,
    /// falling back to English and then to the text written at the call site, so a missing file never breaks a screen.
    /// Screens built after a language change use the new text.
    /// </summary>
    public static class Loc
    {
        static Strings strings;
        static Strings english;
        public static event System.Action OnLanguageChanged;

        static Strings Table
        {
            get
            {
                if (strings == null) Reload();
                return strings;
            }
        }

        public static string Language => Table.Language;

        public static void Reload()
        {
            english = Load("en");
            string lang = GameSettings.Language;
            strings = lang == "en" ? english : Load(lang);
            if (strings.Count == 0) strings = english;
            OnLanguageChanged?.Invoke();
        }

        static Strings Load(string lang)
        {
            var s = new Strings();
            var asset = Resources.Load<TextAsset>("Localization/" + lang);
            if (asset != null) s.LoadJson(asset.text, lang);
            return s;
        }

        public static string T(string key, string fallback = null)
        {
            var t = Table;
            if (t.Has(key)) return t.T(key);
            if (english != null && english.Has(key)) return english.T(key);
            return fallback ?? key;
        }

        public static string F(string key, string fallback, params object[] args) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, T(key, fallback), args);

        /// <summary>Languages that have a file (codes), always including English.</summary>
        public static string[] Available()
        {
            var list = new System.Collections.Generic.List<string> { "en" };
            foreach (var a in Resources.LoadAll<TextAsset>("Localization"))
                if (a.name != "en" && !list.Contains(a.name)) list.Add(a.name);
            return list.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { strings = null; english = null; OnLanguageChanged = null; }
    }
}
