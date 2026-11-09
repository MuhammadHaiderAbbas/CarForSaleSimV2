using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CarSales.Foundation
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value. Keeps a reference to the source text so <see cref="Raw"/> returns the exact original text
    /// of this value (used to pass module data through the save envelope untouched).
    /// </summary>
    public sealed class JsonNode
    {
        readonly string source;
        readonly int start, length;

        public JsonKind Kind { get; }
        public string Str { get; }
        public double Num { get; }
        public bool Bool { get; }
        public List<JsonNode> Items { get; }
        public List<KeyValuePair<string, JsonNode>> Fields { get; }

        internal JsonNode(JsonKind kind, string source, int start, int length,
                          string str = null, double num = 0, bool b = false,
                          List<JsonNode> items = null, List<KeyValuePair<string, JsonNode>> fields = null)
        {
            Kind = kind; this.source = source; this.start = start; this.length = length;
            Str = str; Num = num; Bool = b; Items = items; Fields = fields;
        }

        public string Raw => source.Substring(start, length);

        /// <summary>Field of an object, or null.</summary>
        public JsonNode this[string key]
        {
            get
            {
                if (Fields == null) return null;
                foreach (var f in Fields) if (f.Key == key) return f.Value;
                return null;
            }
        }

        public bool Has(string key) => this[key] != null;
        public string AsString(string fallback = null) => Kind == JsonKind.String ? Str : fallback;
        public int AsInt(int fallback = 0) => Kind == JsonKind.Number && !double.IsNaN(Num) && !double.IsInfinity(Num) ? (int)Math.Round(Num) : fallback;
        public long AsLong(long fallback = 0) => Kind == JsonKind.Number && !double.IsNaN(Num) && !double.IsInfinity(Num) ? (long)Math.Round(Num) : fallback;
        public bool AsBool(bool fallback = false) => Kind == JsonKind.Bool ? Bool : fallback;
    }

    /// <summary>Small, dependency-free JSON reader/writer helpers (the save envelope must not depend on Unity).</summary>
    public static class MiniJson
    {
        /// <summary>Parses a complete JSON document. Throws <see cref="FormatException"/> on invalid input.</summary>
        public static JsonNode Parse(string text)
        {
            if (text == null) throw new FormatException("No JSON text.");
            int i = 0;
            var node = ParseValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException($"Unexpected text after JSON at {i}.");
            return node;
        }

        public static bool TryParse(string text, out JsonNode node, out string error)
        {
            try { node = Parse(text); error = null; return true; }
            catch (FormatException e) { node = null; error = e.Message; return false; }
        }

        const int MaxDepth = 256;

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static JsonNode ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply.");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            int start = i;
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"':
                    string str = ParseString(s, ref i);
                    return new JsonNode(JsonKind.String, s, start, i - start, str: str);
                case 't': Expect(s, ref i, "true"); return new JsonNode(JsonKind.Bool, s, start, 4, b: true);
                case 'f': Expect(s, ref i, "false"); return new JsonNode(JsonKind.Bool, s, start, 5, b: false);
                case 'n': Expect(s, ref i, "null"); return new JsonNode(JsonKind.Null, s, start, 4);
                // Not standard JSON, but Unity's JsonUtility can write these for broken float values.
                case 'N': Expect(s, ref i, "NaN"); return new JsonNode(JsonKind.Number, s, start, 3, num: double.NaN);
                case 'I': Expect(s, ref i, "Infinity"); return new JsonNode(JsonKind.Number, s, start, 8, num: double.PositiveInfinity);
                default:
                    if (c == '-' && i + 1 < s.Length && s[i + 1] == 'I')
                    {
                        i++;
                        Expect(s, ref i, "Infinity");
                        return new JsonNode(JsonKind.Number, s, start, 9, num: double.NegativeInfinity);
                    }
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
                    throw new FormatException($"Unexpected character '{c}' at {i}.");
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException($"Expected '{word}' at {i}.");
            i += word.Length;
        }

        static JsonNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(start, i - start);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new FormatException($"Invalid number '{num}' at {start}.");
            return new JsonNode(JsonKind.Number, s, start, i - start, num: v);
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            StringBuilder sb = null;
            int runStart = i;
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string.");
                char c = s[i];
                if (c == '"')
                {
                    string result = sb == null ? s.Substring(runStart, i - runStart) : sb.Append(s, runStart, i - runStart).ToString();
                    i++;
                    return result;
                }
                if (c == '\\')
                {
                    sb = sb ?? new StringBuilder();
                    sb.Append(s, runStart, i - runStart);
                    i++;
                    if (i >= s.Length) throw new FormatException("Unterminated escape.");
                    char e = s[i];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= s.Length) throw new FormatException("Bad \\u escape.");
                            sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException($"Bad escape '\\{e}' at {i}.");
                    }
                    i++;
                    runStart = i;
                    continue;
                }
                i++;
            }
        }

        static JsonNode ParseArray(string s, ref int i, int depth)
        {
            int start = i;
            i++;
            var items = new List<JsonNode>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JsonNode(JsonKind.Array, s, start, i - start, items: items); }
            while (true)
            {
                items.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return new JsonNode(JsonKind.Array, s, start, i - start, items: items); }
                throw new FormatException($"Expected ',' or ']' at {i}.");
            }
        }

        static JsonNode ParseObject(string s, ref int i, int depth)
        {
            int start = i;
            i++;
            var fields = new List<KeyValuePair<string, JsonNode>>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JsonNode(JsonKind.Object, s, start, i - start, fields: fields); }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"Expected a field name at {i}.");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"Expected ':' at {i}.");
                i++;
                fields.Add(new KeyValuePair<string, JsonNode>(key, ParseValue(s, ref i, depth + 1)));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return new JsonNode(JsonKind.Object, s, start, i - start, fields: fields); }
                throw new FormatException($"Expected ',' or '}}' at {i}.");
            }
        }

        /// <summary>JSON string literal including quotes.</summary>
        public static string Quote(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Number(long v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
