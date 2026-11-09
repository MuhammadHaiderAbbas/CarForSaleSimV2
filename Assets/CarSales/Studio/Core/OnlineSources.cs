using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarSales.Foundation;

namespace CarSales.Studio
{
    /// <summary>One model from a Sketchfab search.</summary>
    public class SketchfabModel
    {
        public string Uid = "", Name = "", ViewerUrl = "", Author = "", AuthorUrl = "", LicenceLabel = "", ThumbnailUrl = "";
        public LicenceType Licence;
        public int Faces;
        public bool Downloadable;
    }

    public class SketchfabSearchPage
    {
        public readonly List<SketchfabModel> Models = new List<SketchfabModel>();
        public string Next = "";
    }

    /// <summary>
    /// Sketchfab Data API v3 (https://sketchfab.com/developers/data-api/v3): URLs and response parsing.
    /// Searching is public; downloading needs the user's API token (sketchfab.com → Settings → Password &amp; API).
    /// </summary>
    public static class SketchfabApi
    {
        public const string Base = "https://api.sketchfab.com/v3";

        public static string SearchUrl(string query, bool carsOnly, int count = 24)
        {
            string url = $"{Base}/search?type=models&downloadable=true&count={Math.Max(1, Math.Min(count, 24))}&q={Uri.EscapeDataString(query ?? "")}";
            if (carsOnly) url += "&categories=cars-vehicles";
            return url;
        }

        public static string ModelUrl(string uid) => $"{Base}/models/{Uri.EscapeDataString(uid)}";
        public static string DownloadUrl(string uid) => $"{Base}/models/{Uri.EscapeDataString(uid)}/download";
        public static string AuthHeader(string token) => "Token " + (token ?? "").Trim();

        public static SketchfabSearchPage ParseSearch(string json)
        {
            var page = new SketchfabSearchPage();
            var root = MiniJson.Parse(json);
            page.Next = root["next"]?.AsString("") ?? "";
            var results = root["results"];
            if (results?.Items == null) return page;
            foreach (var r in results.Items) page.Models.Add(ParseModel(r));
            return page;
        }

        public static SketchfabModel ParseModelJson(string json) => ParseModel(MiniJson.Parse(json));

        static SketchfabModel ParseModel(JsonNode r)
        {
            var m = new SketchfabModel
            {
                Uid = r["uid"]?.AsString("") ?? "",
                Name = r["name"]?.AsString("") ?? "",
                ViewerUrl = r["viewerUrl"]?.AsString("") ?? "",
                Faces = r["faceCount"]?.AsInt() ?? 0,
                Downloadable = r["isDownloadable"]?.AsBool(true) ?? true,
            };
            var user = r["user"];
            if (user != null)
            {
                m.Author = user["displayName"]?.AsString(null) ?? user["username"]?.AsString("") ?? "";
                m.AuthorUrl = user["profileUrl"]?.AsString("") ?? "";
            }
            var lic = r["license"];
            if (lic != null)
            {
                if (lic.Kind == JsonKind.String) { m.LicenceLabel = lic.Str; m.Licence = LicenceFromText(lic.Str); }
                else
                {
                    string slug = lic["slug"]?.AsString(null);
                    m.LicenceLabel = lic["label"]?.AsString(slug ?? "") ?? "";
                    m.Licence = slug != null ? LicencePolicy.FromSketchfabSlug(slug) : LicenceFromText(m.LicenceLabel);
                }
            }
            var images = r["thumbnails"]?["images"]?.Items;
            if (images != null)
            {
                // The smallest image at least 200 px wide.
                int best = int.MaxValue;
                foreach (var img in images)
                {
                    int w = img["width"]?.AsInt() ?? 0;
                    if (w >= 200 && w < best) { best = w; m.ThumbnailUrl = img["url"]?.AsString("") ?? ""; }
                }
                if (m.ThumbnailUrl.Length == 0 && images.Count > 0) m.ThumbnailUrl = images[0]["url"]?.AsString("") ?? "";
            }
            return m;
        }

        /// <summary>
        /// Licence from a label ("CC Attribution-NonCommercial") or a URL (".../licenses/by-nc/4.0/").
        /// Order matters: the most specific names first.
        /// </summary>
        public static LicenceType LicenceFromText(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (t.Length == 0) return LicenceType.Unknown;
            if (t.Contains("cc0") || t.Contains("public domain") || t.Contains("publicdomain")) return LicenceType.CC0;
            if (t.Contains("editorial")) return LicenceType.Editorial;
            if (t.Contains("standard")) return LicenceType.RoyaltyFreePurchased;
            bool nc = t.Contains("noncommercial") || t.Contains("non-commercial") || Regex.IsMatch(t, @"by-nc");
            bool sa = t.Contains("sharealike") || t.Contains("share-alike") || Regex.IsMatch(t, @"(by|nc)-sa");
            bool nd = t.Contains("noderiv") || t.Contains("no-deriv") || Regex.IsMatch(t, @"(by|nc)-nd");
            bool by = t.Contains("attribution") || Regex.IsMatch(t, @"(^|/|\s)by(-|/|$)");
            if (!by && !nc && !sa && !nd) return LicenceType.Unknown;
            if (nc) return sa ? LicenceType.CCBYNCSA : nd ? LicenceType.CCBYNCND : LicenceType.CCBYNC;
            if (sa) return LicenceType.CCBYSA;
            if (nd) return LicenceType.CCBYND;
            return LicenceType.CCBY;
        }

        /// <summary>From the download endpoint: the GLB url if offered, else the glTF zip url. isZip tells which.</summary>
        public static string ParseDownload(string json, out bool isZip)
        {
            var root = MiniJson.Parse(json);
            string glb = root["glb"]?["url"]?.AsString(null);
            if (!string.IsNullOrEmpty(glb)) { isZip = false; return glb; }
            isZip = true;
            return root["gltf"]?["url"]?.AsString(null);
        }
    }

    public class PolyHavenAsset
    {
        public string Id = "", Name = "", Authors = "", Thumbnail = "";
        public List<string> Categories = new List<string>();
    }

    /// <summary>Poly Haven public API (https://api.polyhaven.com) - every asset is CC0.</summary>
    public static class PolyHavenApi
    {
        public const string Base = "https://api.polyhaven.com";
        public const string UserAgent = "CarSalesSimulator-AssetStudio";

        public static string ListUrl(bool hdris) => $"{Base}/assets?t={(hdris ? "hdris" : "textures")}";
        public static string FilesUrl(string id) => $"{Base}/files/{Uri.EscapeDataString(id)}";
        /// <summary>
        /// Fallback thumbnail (the list normally gives `thumbnail_url`). Always PNG: Unity can't decode the WebP default.
        /// </summary>
        public static string ThumbnailUrl(string id) => $"https://cdn.polyhaven.com/asset_img/thumbs/{Uri.EscapeDataString(id)}.png?width=256&height=256&format=png";

        public static string AsPng(string url) =>
            string.IsNullOrEmpty(url) ? url : Regex.Replace(url, @"format=webp", "format=png", RegexOptions.IgnoreCase);
        public static string PageUrl(string id) => $"https://polyhaven.com/a/{Uri.EscapeDataString(id)}";

        public static List<PolyHavenAsset> ParseList(string json)
        {
            var list = new List<PolyHavenAsset>();
            var root = MiniJson.Parse(json);
            if (root.Fields == null) return list;
            foreach (var f in root.Fields)
            {
                var a = new PolyHavenAsset { Id = f.Key, Name = f.Value["name"]?.AsString(f.Key) ?? f.Key };
                var cats = f.Value["categories"]?.Items;   // deprecated, still sent
                if (cats != null) foreach (var c in cats) if (c.Kind == JsonKind.String) a.Categories.Add(c.Str);
                string category = f.Value["category"]?.AsString(null);
                if (!string.IsNullOrEmpty(category) && !a.Categories.Contains(category)) a.Categories.Add(category);
                a.Thumbnail = AsPng(f.Value["thumbnail_url"]?.AsString(null)) ?? ThumbnailUrl(a.Id);
                var authors = f.Value["authors"]?.Fields;
                if (authors != null) a.Authors = string.Join(", ", authors.ConvertAll(x => x.Key));
                list.Add(a);
            }
            list.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The .hdr url of an HDRI at a resolution ("1k", "2k", "4k"), falling back to a smaller one.</summary>
        public static string HdriUrl(string filesJson, string resolution)
        {
            var hdri = MiniJson.Parse(filesJson)["hdri"];
            foreach (var res in Fallbacks(resolution))
            {
                string url = hdri?[res]?["hdr"]?["url"]?.AsString(null);
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }

        /// <summary>Texture maps of a material (slot → url), preferring jpg, falling back to png.</summary>
        public static Dictionary<TextureSlot, string> TextureUrls(string filesJson, string resolution)
        {
            var result = new Dictionary<TextureSlot, string>();
            var root = MiniJson.Parse(filesJson);
            var map = new Dictionary<string, TextureSlot>(StringComparer.OrdinalIgnoreCase)
            {
                { "Diffuse", TextureSlot.BaseColor }, { "nor_gl", TextureSlot.Normal }, { "Rough", TextureSlot.Roughness },
                { "Metal", TextureSlot.Metallic }, { "AO", TextureSlot.Occlusion }, { "Displacement", TextureSlot.Height },
            };
            if (root.Fields == null) return result;
            foreach (var f in root.Fields)
            {
                string key = f.Key.ToLowerInvariant();
                if (key == "nor_dx" || key == "arm" || key == "blend" || key == "gltf" || key == "mtlx") continue;   // DirectX normals, packed maps, scene files
                if (!map.TryGetValue(f.Key, out var slot))
                {
                    slot = TextureSlots.Classify(f.Key);   // map names aren't documented: fall back to the usual naming
                    if (slot == TextureSlot.Unknown || result.ContainsKey(slot)) continue;
                }
                foreach (var res in Fallbacks(resolution))
                {
                    var r = f.Value[res];
                    string url = r?["jpg"]?["url"]?.AsString(null) ?? r?["png"]?["url"]?.AsString(null);
                    if (string.IsNullOrEmpty(url)) continue;
                    result[slot] = url;
                    break;
                }
            }
            return result;
        }

        static IEnumerable<string> Fallbacks(string wanted)
        {
            yield return wanted;
            foreach (var r in new[] { "2k", "1k", "4k" }) if (r != wanted) yield return r;
        }
    }

    public enum ClipRole { None, Idle, Walk, Run }

    /// <summary>Naming conventions in imported models.</summary>
    public static class ModelNaming
    {
        static readonly Regex Lod = new Regex(@"[_\-\.\s]?lod[_\-\s]?(\d+)$", RegexOptions.IgnoreCase);

        /// <summary>LOD level from a name ("Body_LOD1" → 1), or -1.</summary>
        public static int LodIndex(string name)
        {
            var m = Lod.Match(name ?? "");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        /// <summary>What an animation clip is for, from its name (Mixamo, Unity and most packs).</summary>
        public static ClipRole Role(string clipName)
        {
            string n = (clipName ?? "").ToLowerInvariant();
            if (n.Contains("__preview__")) return ClipRole.None;
            if (n.Contains("idle") || n.Contains("stand") || n.Contains("breath")) return ClipRole.Idle;
            if (n.Contains("run") || n.Contains("jog") || n.Contains("sprint")) return ClipRole.Run;
            if (n.Contains("walk")) return ClipRole.Walk;
            return ClipRole.None;
        }

        /// <summary>LOD screen heights for 1..4 levels (the last one culls tiny far objects).</summary>
        public static float[] LodHeights(int levels)
        {
            switch (levels)
            {
                case 1: return new[] { 0.02f };
                case 2: return new[] { 0.3f, 0.02f };
                case 3: return new[] { 0.45f, 0.15f, 0.02f };
                default: return new[] { 0.5f, 0.25f, 0.08f, 0.02f };
            }
        }
    }
}
