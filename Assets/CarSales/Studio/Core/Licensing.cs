using System;
using System.Collections.Generic;
using System.Text;

namespace CarSales.Studio
{
    /// <summary>Licence of an imported asset. Saved as numbers: only add new values at the end.</summary>
    public enum LicenceType
    {
        Unknown = 0,
        CC0 = 1,
        CCBY = 2,
        CCBYSA = 3,
        CCBYND = 4,
        CCBYNC = 5,
        CCBYNCSA = 6,
        CCBYNCND = 7,
        AssetStoreStandard = 8,   // Unity Asset Store Standard EULA
        RoyaltyFreePurchased = 9, // KitBash3D, CGTrader, Fab, TurboSquid... (check the store's terms)
        OwnWork = 10,
        Editorial = 11,           // editorial-only (e.g. some CGTrader/TurboSquid models): not for games
    }

    /// <summary>Where an asset came from and under which terms (spec §12). Stored with every imported asset.</summary>
    [Serializable]
    public class LicenceInfo
    {
        public string source = "";       // "Sketchfab", "Unity Asset Store", "KitBash3D", "Local file"...
        public string sourceUrl = "";
        public string author = "";
        public string authorUrl = "";
        public LicenceType licence = LicenceType.Unknown;
        public string licenceUrl = "";
        public string attributionText = "";   // optional: exact text the author asks for
        public string purchaseRef = "";       // order / invoice number for paid assets
        public string dateAcquired = "";      // yyyy-MM-dd
        public string notes = "";
    }

    public enum LicenceStatus { Allowed, AllowedWithAttribution, NeedsReview, Blocked }

    public struct LicenceVerdict
    {
        public LicenceStatus Status;
        public string Reason;
        public bool BlocksRelease => Status == LicenceStatus.Blocked || Status == LicenceStatus.NeedsReview;
    }

    public static class LicencePolicy
    {
        public static string DisplayName(LicenceType t)
        {
            switch (t)
            {
                case LicenceType.CC0: return "CC0 (public domain)";
                case LicenceType.CCBY: return "CC BY 4.0";
                case LicenceType.CCBYSA: return "CC BY-SA 4.0";
                case LicenceType.CCBYND: return "CC BY-ND 4.0";
                case LicenceType.CCBYNC: return "CC BY-NC 4.0";
                case LicenceType.CCBYNCSA: return "CC BY-NC-SA 4.0";
                case LicenceType.CCBYNCND: return "CC BY-NC-ND 4.0";
                case LicenceType.AssetStoreStandard: return "Unity Asset Store EULA";
                case LicenceType.RoyaltyFreePurchased: return "Royalty-free (purchased)";
                case LicenceType.OwnWork: return "Own work";
                case LicenceType.Editorial: return "Editorial use only";
                default: return "Unknown";
            }
        }

        public static string DefaultUrl(LicenceType t)
        {
            switch (t)
            {
                case LicenceType.CC0: return "https://creativecommons.org/publicdomain/zero/1.0/";
                case LicenceType.CCBY: return "https://creativecommons.org/licenses/by/4.0/";
                case LicenceType.CCBYSA: return "https://creativecommons.org/licenses/by-sa/4.0/";
                case LicenceType.CCBYND: return "https://creativecommons.org/licenses/by-nd/4.0/";
                case LicenceType.CCBYNC: return "https://creativecommons.org/licenses/by-nc/4.0/";
                case LicenceType.CCBYNCSA: return "https://creativecommons.org/licenses/by-nc-sa/4.0/";
                case LicenceType.CCBYNCND: return "https://creativecommons.org/licenses/by-nc-nd/4.0/";
                case LicenceType.AssetStoreStandard: return "https://unity.com/legal/as-terms";
                default: return "";
            }
        }

        /// <summary>
        /// Can this asset ship in a commercial release build? (Game-design policy, not legal advice:
        /// always read the actual licence.)
        /// </summary>
        public static LicenceVerdict Check(LicenceInfo info)
        {
            if (info == null) return new LicenceVerdict { Status = LicenceStatus.Blocked, Reason = "No licence record." };
            switch (info.licence)
            {
                case LicenceType.CC0:
                case LicenceType.OwnWork:
                    return new LicenceVerdict { Status = LicenceStatus.Allowed, Reason = "Free to use." };
                case LicenceType.AssetStoreStandard:
                    return new LicenceVerdict { Status = LicenceStatus.Allowed, Reason = "Allowed in games; don't redistribute the source files." };
                case LicenceType.RoyaltyFreePurchased:
                    return string.IsNullOrWhiteSpace(info.purchaseRef)
                        ? new LicenceVerdict { Status = LicenceStatus.NeedsReview, Reason = "Paid asset without an order/invoice number - add proof of purchase." }
                        : new LicenceVerdict { Status = LicenceStatus.Allowed, Reason = "Purchased; follow the store's terms." };
                case LicenceType.CCBY:
                    return string.IsNullOrWhiteSpace(info.author)
                        ? new LicenceVerdict { Status = LicenceStatus.NeedsReview, Reason = "CC BY needs the author's name for the credits." }
                        : new LicenceVerdict { Status = LicenceStatus.AllowedWithAttribution, Reason = "Allowed; the author is credited automatically." };
                case LicenceType.CCBYSA:
                    return new LicenceVerdict { Status = LicenceStatus.NeedsReview, Reason = "Share-alike: changes to this asset must be shared under the same licence. Review before release." };
                case LicenceType.CCBYND:
                    return new LicenceVerdict { Status = LicenceStatus.NeedsReview, Reason = "No-derivatives: re-texturing, cutting or optimising the model may not be allowed." };
                case LicenceType.CCBYNC:
                case LicenceType.CCBYNCSA:
                case LicenceType.CCBYNCND:
                    return new LicenceVerdict { Status = LicenceStatus.Blocked, Reason = "Non-commercial licence: not allowed in a game you sell." };
                case LicenceType.Editorial:
                    return new LicenceVerdict { Status = LicenceStatus.Blocked, Reason = "Editorial-only assets can't be used in games." };
                default:
                    return new LicenceVerdict { Status = LicenceStatus.Blocked, Reason = "Unknown licence: fine for prototyping, blocked for release." };
            }
        }

        public static bool NeedsAttribution(LicenceType t) =>
            t == LicenceType.CCBY || t == LicenceType.CCBYSA || t == LicenceType.CCBYND ||
            t == LicenceType.CCBYNC || t == LicenceType.CCBYNCSA || t == LicenceType.CCBYNCND;

        /// <summary>Maps Sketchfab's licence slugs ("by", "by-sa", "cc0", "st", "ed"...) to <see cref="LicenceType"/>.</summary>
        public static LicenceType FromSketchfabSlug(string slug)
        {
            switch ((slug ?? "").Trim().ToLowerInvariant())
            {
                case "cc0": return LicenceType.CC0;
                case "by": return LicenceType.CCBY;
                case "by-sa": return LicenceType.CCBYSA;
                case "by-nd": return LicenceType.CCBYND;
                case "by-nc": return LicenceType.CCBYNC;
                case "by-nc-sa": return LicenceType.CCBYNCSA;
                case "by-nc-nd": return LicenceType.CCBYNCND;
                case "st": return LicenceType.RoyaltyFreePurchased;   // Sketchfab Store "Standard" licence
                case "ed": return LicenceType.Editorial;
                default: return LicenceType.Unknown;
            }
        }

        /// <summary>The credit line for one asset, e.g. "“Red Coupe” by Jane Doe (url), licensed under CC BY 4.0 (url)".</summary>
        public static string Attribution(string assetName, LicenceInfo info)
        {
            if (info == null) return assetName;
            if (!string.IsNullOrWhiteSpace(info.attributionText)) return info.attributionText.Trim();
            var sb = new StringBuilder();
            sb.Append('“').Append(string.IsNullOrWhiteSpace(assetName) ? "Untitled" : assetName.Trim()).Append('”');
            if (!string.IsNullOrWhiteSpace(info.author)) sb.Append(" by ").Append(info.author.Trim());
            if (!string.IsNullOrWhiteSpace(info.sourceUrl)) sb.Append(" (").Append(info.sourceUrl.Trim()).Append(')');
            sb.Append(", ").Append(NeedsAttribution(info.licence) || info.licence == LicenceType.CC0 ? "licensed under " : "")
              .Append(DisplayName(info.licence));
            string url = string.IsNullOrWhiteSpace(info.licenceUrl) ? DefaultUrl(info.licence) : info.licenceUrl.Trim();
            if (url.Length > 0 && NeedsAttribution(info.licence)) sb.Append(" (").Append(url).Append(')');
            return sb.ToString();
        }
    }

    /// <summary>Builds the CREDITS text from every imported asset.</summary>
    public static class CreditsBuilder
    {
        public static string Build(string gameName, IEnumerable<KeyValuePair<string, LicenceInfo>> assets)
        {
            var required = new List<string>();
            var thanks = new List<string>();
            foreach (var a in assets)
            {
                if (a.Value == null) continue;
                string line = LicencePolicy.Attribution(a.Key, a.Value);
                if (LicencePolicy.NeedsAttribution(a.Value.licence)) required.Add(line);
                else if (a.Value.licence != LicenceType.OwnWork) thanks.Add(line);
            }
            required.Sort(StringComparer.OrdinalIgnoreCase);
            thanks.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.Append(gameName).Append(" - Credits\n\n");
            if (required.Count > 0)
            {
                sb.Append("3D assets used under Creative Commons licences:\n");
                foreach (var l in required) sb.Append(" - ").Append(l).Append('\n');
                sb.Append('\n');
            }
            if (thanks.Count > 0)
            {
                sb.Append("Other assets:\n");
                foreach (var l in thanks) sb.Append(" - ").Append(l).Append('\n');
            }
            if (required.Count == 0 && thanks.Count == 0) sb.Append("No third-party assets.\n");
            return sb.ToString();
        }
    }
}
