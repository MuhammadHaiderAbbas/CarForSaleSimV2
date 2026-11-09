using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CarSales.Foundation
{
    /// <summary>One system's saved data. <see cref="Data"/> is a complete JSON value (usually an object).</summary>
    public sealed class SaveModule
    {
        public string Key;
        public int Version;
        public string Data;

        public SaveModule(string key, int version, string data) { Key = key; Version = version; Data = data; }
    }

    /// <summary>
    /// The save file: a small header (shown in the load menu) plus one JSON block per system ("module").
    /// A missing module simply loads defaults, so new systems never break old saves (spec §6.2).
    /// </summary>
    public sealed class SaveEnvelope
    {
        public const string Format = "CARSALES-SAVE";
        public const int CurrentFormatVersion = 5;

        public int FormatVersion = CurrentFormatVersion;
        public string GameVersion = "";
        public string SavedAtUtc = "";
        public int Day;
        public int Money;
        public string Label = "";
        public readonly List<SaveModule> Modules = new List<SaveModule>();

        /// <summary>True when this was converted from a v4 (or older) single-object save file.</summary>
        public bool IsLegacy { get; private set; }

        public SaveModule Get(string key)
        {
            foreach (var m in Modules) if (m.Key == key) return m;
            return null;
        }

        public void Set(string key, int version, string data)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Module key is empty.");
            if (string.IsNullOrEmpty(data)) throw new ArgumentException($"Module '{key}' has no data.");
            var m = Get(key);
            if (m != null) { m.Version = version; m.Data = data; }
            else Modules.Add(new SaveModule(key, version, data));
        }

        public DateTime SavedAt =>
            DateTime.TryParse(SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? d : DateTime.MinValue;

        public string ComputeChecksum()
        {
            var sb = new StringBuilder();
            foreach (var m in Modules) sb.Append(m.Key).Append('\n').Append(m.Version).Append('\n').Append(m.Data).Append('\n');
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\n");
            sb.Append("  \"format\": ").Append(MiniJson.Quote(Format)).Append(",\n");
            sb.Append("  \"version\": ").Append(CurrentFormatVersion).Append(",\n");
            sb.Append("  \"gameVersion\": ").Append(MiniJson.Quote(GameVersion ?? "")).Append(",\n");
            sb.Append("  \"savedAtUtc\": ").Append(MiniJson.Quote(SavedAtUtc ?? "")).Append(",\n");
            sb.Append("  \"day\": ").Append(Day).Append(",\n");
            sb.Append("  \"money\": ").Append(Money).Append(",\n");
            sb.Append("  \"label\": ").Append(MiniJson.Quote(Label ?? "")).Append(",\n");
            sb.Append("  \"checksum\": ").Append(MiniJson.Quote(ComputeChecksum())).Append(",\n");
            sb.Append("  \"modules\": [");
            for (int i = 0; i < Modules.Count; i++)
            {
                var m = Modules[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"key\": ").Append(MiniJson.Quote(m.Key))
                  .Append(", \"version\": ").Append(m.Version)
                  .Append(", \"data\": ").Append(m.Data).Append(" }");
            }
            sb.Append(Modules.Count > 0 ? "\n  ]\n}" : "]\n}");
            return sb.ToString();
        }

        /// <summary>
        /// Reads a save file. Also accepts a v4 save (one JSON object with "money" and "day"), which becomes a
        /// "game" module. Throws <see cref="FormatException"/> if the text is not a save file.
        /// </summary>
        public static SaveEnvelope Parse(string text, out bool checksumOk, string legacyModuleKey = "game")
        {
            var root = MiniJson.Parse(text);
            if (root.Kind != JsonKind.Object) throw new FormatException("A save file must be a JSON object.");

            var env = new SaveEnvelope();
            if (root["format"]?.AsString() != Format)
            {
                if (!root.Has("money") || !root.Has("day")) throw new FormatException("Not a Car Sales save file.");
                env.IsLegacy = true;
                env.FormatVersion = root["version"]?.AsInt(1) ?? 1;
                env.Day = root["day"].AsInt(1);
                env.Money = root["money"].AsInt();
                env.Label = $"Day {env.Day}";
                env.Modules.Add(new SaveModule(legacyModuleKey, env.FormatVersion, root.Raw));
                checksumOk = true;
                return env;
            }

            env.FormatVersion = root["version"]?.AsInt(CurrentFormatVersion) ?? CurrentFormatVersion;
            env.GameVersion = root["gameVersion"]?.AsString("") ?? "";
            env.SavedAtUtc = root["savedAtUtc"]?.AsString("") ?? "";
            env.Day = root["day"]?.AsInt(1) ?? 1;
            env.Money = root["money"]?.AsInt() ?? 0;
            env.Label = root["label"]?.AsString("") ?? "";
            var modules = root["modules"];
            if (modules == null || modules.Kind != JsonKind.Array) throw new FormatException("Save file has no modules.");
            foreach (var m in modules.Items)
            {
                string key = m["key"]?.AsString();
                var data = m["data"];
                if (string.IsNullOrEmpty(key) || data == null) throw new FormatException("Save module without key or data.");
                env.Modules.Add(new SaveModule(key, m["version"]?.AsInt(1) ?? 1, data.Raw));
            }
            string stored = root["checksum"]?.AsString("") ?? "";
            checksumOk = stored.Length > 0 && string.Equals(stored, env.ComputeChecksum(), StringComparison.OrdinalIgnoreCase);
            return env;
        }
    }
}
