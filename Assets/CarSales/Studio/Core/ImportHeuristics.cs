using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CarSales.Studio
{
    /// <summary>What an imported asset is used for. Saved as numbers: only add new values at the end.</summary>
    public enum AssetCategory { Vehicle = 0, Building = 1, Prop = 2, Character = 3, Interior = 4, Environment = 5, Material = 6, Sky = 7 }

    public enum Axis { X, Y, Z }

    /// <summary>Numbers measured from a model (metres, before any scaling).</summary>
    public struct ModelStats
    {
        public float SizeX, SizeY, SizeZ;
        public int Triangles, Vertices, Renderers, Materials, SkinnedMeshes;
        public int MaterialsWithoutTexture, UnreadableMeshes;
        public bool HasLodGroup, HasAnimations;
    }

    public struct ScaleSuggestion
    {
        public float Factor;              // multiply the model by this
        public bool RotateLengthToZ;      // the model is longer along X: turn it 90° so its length runs along +Z
        public bool LooksZUp;             // probably exported Z-up (Blender/3ds Max): rotate -90° around X
        public string Reason;
    }

    /// <summary>Guesses scale and orientation of an imported model from its size and category.</summary>
    public static class ScaleHeuristics
    {
        /// <summary>Typical size in metres along the measured axis (vehicle length, character height...), with a sane range.</summary>
        public static bool Expected(AssetCategory c, out float target, out float min, out float max)
        {
            switch (c)
            {
                case AssetCategory.Vehicle: target = 4.5f; min = 3f; max = 7f; return true;
                case AssetCategory.Character: target = 1.75f; min = 1.4f; max = 2.1f; return true;
                case AssetCategory.Building: target = 12f; min = 3f; max = 120f; return true;
                case AssetCategory.Interior: target = 1f; min = 0.2f; max = 6f; return true;
                case AssetCategory.Prop: target = 1f; min = 0.05f; max = 10f; return true;
                default: target = 1f; min = 0.01f; max = 1000f; return false;
            }
        }

        // Unit mix-ups that happen in practice: cm, mm, inches, decimetres, 10x/100x exports.
        static readonly float[] UnitFactors = { 1f, 0.01f, 0.001f, 0.0254f, 0.1f, 10f, 100f, 2.54f };

        public static ScaleSuggestion Suggest(AssetCategory category, float sizeX, float sizeY, float sizeZ)
        {
            var s = new ScaleSuggestion { Factor = 1f, Reason = "Size looks right." };
            sizeX = Math.Abs(sizeX); sizeY = Math.Abs(sizeY); sizeZ = Math.Abs(sizeZ);
            if (sizeX <= 0f && sizeY <= 0f && sizeZ <= 0f) { s.Reason = "Empty model."; return s; }

            float measured;
            if (category == AssetCategory.Vehicle)
            {
                // A car is long, not tall. If Y is the longest side it was probably exported Z-up.
                if (sizeY > Math.Max(sizeX, sizeZ) * 1.05f)
                {
                    s.LooksZUp = true;
                    float oldY = sizeY; sizeY = sizeZ; sizeZ = oldY;
                }
                s.RotateLengthToZ = sizeX > sizeZ * 1.1f;
                measured = Math.Max(sizeX, sizeZ);
            }
            else if (category == AssetCategory.Character)
            {
                if (sizeZ > sizeY * 1.5f && sizeZ > sizeX) { s.LooksZUp = true; measured = sizeZ; }
                else measured = sizeY;
            }
            else measured = Math.Max(sizeX, Math.Max(sizeY, sizeZ));

            if (!Expected(category, out float target, out float min, out float max) || measured <= 0f) return Finish(s);
            if (measured >= min && measured <= max) return Finish(s);

            // Prefer a "unit mix-up" factor that lands in range and closest to the typical size.
            float best = -1f, bestErr = float.MaxValue;
            foreach (var f in UnitFactors)
            {
                float size = measured * f;
                if (size < min || size > max) continue;
                float err = Math.Abs((float)Math.Log(size / target));
                if (err < bestErr) { bestErr = err; best = f; }
            }
            if (best > 0f)
            {
                s.Factor = best;
                s.Reason = $"Measured {measured:0.###} m; looks like a unit mix-up, scaled x{best:0.####} to {measured * best:0.##} m.";
            }
            else
            {
                s.Factor = target / measured;
                s.Reason = $"Measured {measured:0.###} m; scaled to a typical {target:0.##} m.";
            }
            return Finish(s);
        }

        static ScaleSuggestion Finish(ScaleSuggestion s)
        {
            if (s.LooksZUp) s.Reason += " Looks Z-up: rotated to Y-up.";
            if (s.RotateLengthToZ) s.Reason += " Turned 90° so the length runs along +Z.";
            return s;
        }
    }

    public enum TextureSlot { Unknown, BaseColor, Normal, Metallic, Roughness, Smoothness, MetallicSmoothness, Occlusion, Emission, Height, Opacity }

    /// <summary>Guesses what a texture is from its file name (most stores use these suffixes).</summary>
    public static class TextureSlots
    {
        static readonly (Regex re, TextureSlot slot)[] Rules =
        {
            (new Regex(@"(metallic[_\- ]?smoothness|metalsmooth|_ms$)", RegexOptions.IgnoreCase), TextureSlot.MetallicSmoothness),
            (new Regex(@"(normal|nrm|_nor|_n$|_nm$|normalgl|normaldx)", RegexOptions.IgnoreCase), TextureSlot.Normal),
            (new Regex(@"(roughness|rough|_rgh|_r$)", RegexOptions.IgnoreCase), TextureSlot.Roughness),
            (new Regex(@"(smoothness|gloss|_s$)", RegexOptions.IgnoreCase), TextureSlot.Smoothness),
            (new Regex(@"(metallic|metalness|metal|_met|_m$)", RegexOptions.IgnoreCase), TextureSlot.Metallic),
            (new Regex(@"(ambient[_\- ]?occlusion|occlusion|_ao$|_ao_|ambientocclusion)", RegexOptions.IgnoreCase), TextureSlot.Occlusion),
            (new Regex(@"(emissive|emission|emit|_e$|glow)", RegexOptions.IgnoreCase), TextureSlot.Emission),
            (new Regex(@"(height|displacement|disp|bump|_h$)", RegexOptions.IgnoreCase), TextureSlot.Height),
            (new Regex(@"(opacity|alpha|transparency|_mask$)", RegexOptions.IgnoreCase), TextureSlot.Opacity),
            (new Regex(@"(base[_\- ]?colou?r|albedo|diffuse|diff|_col$|_color$|_bc$|_d$|basecolor|_a$)", RegexOptions.IgnoreCase), TextureSlot.BaseColor),
        };

        public static TextureSlot Classify(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName ?? "");
            foreach (var (re, slot) in Rules) if (re.IsMatch(name)) return slot;
            return TextureSlot.Unknown;
        }
    }

    public enum IssueLevel { Info, Warning, Error }

    public struct ValidationIssue
    {
        public IssueLevel Level;
        public string Message;
        public override string ToString() => $"{Level}: {Message}";
    }

    /// <summary>Checks an import against the budgets in spec §11 and basic sanity rules.</summary>
    public static class ImportValidation
    {
        public static int TriangleBudget(AssetCategory c)
        {
            switch (c)
            {
                case AssetCategory.Vehicle: return 150000;
                case AssetCategory.Building: return 300000;
                case AssetCategory.Character: return 60000;
                case AssetCategory.Interior: return 30000;
                case AssetCategory.Environment: return 50000;
                default: return 20000;
            }
        }

        /// <param name="finalSize">Size after scaling (length for vehicles, height for characters, largest side otherwise).</param>
        public static List<ValidationIssue> Validate(AssetCategory category, ModelStats stats, float finalSize, LicenceInfo licence,
                                                     bool vehicleWheelsAssigned, int paintRenderers)
        {
            var list = new List<ValidationIssue>();
            void Add(IssueLevel l, string m) => list.Add(new ValidationIssue { Level = l, Message = m });

            if (stats.Renderers == 0) Add(IssueLevel.Error, "The model has no visible meshes.");
            int budget = TriangleBudget(category);
            if (stats.Triangles > budget * 2) Add(IssueLevel.Error, $"{stats.Triangles:N0} triangles - more than twice the {budget:N0} budget for a {category}. Use a lower-detail version.");
            else if (stats.Triangles > budget) Add(IssueLevel.Warning, $"{stats.Triangles:N0} triangles - over the {budget:N0} budget for a {category}; performance may suffer.");

            if (ScaleHeuristics.Expected(category, out _, out float min, out float max) && (finalSize < min || finalSize > max))
                Add(IssueLevel.Error, $"Size {finalSize:0.##} m is outside {min:0.##}-{max:0.##} m for a {category}. Fix the scale.");

            if (stats.Materials > 16) Add(IssueLevel.Warning, $"{stats.Materials} materials - many draw calls. Consider combining materials.");
            if (stats.MaterialsWithoutTexture > 0 && stats.MaterialsWithoutTexture == stats.Materials && stats.Materials > 1)
                Add(IssueLevel.Info, "No material has a texture (plain colours). If the model came with textures, assign them in the Materials step.");
            if (stats.UnreadableMeshes > 0 && category == AssetCategory.Vehicle)
                Add(IssueLevel.Info, $"{stats.UnreadableMeshes} mesh(es) are not readable; the Studio enables Read/Write so crash dents can show.");

            if (category == AssetCategory.Vehicle)
            {
                if (!vehicleWheelsAssigned) Add(IssueLevel.Warning, "Wheels not assigned: the car will drive on invisible wheels. Assign the 4 wheels.");
                if (paintRenderers == 0) Add(IssueLevel.Warning, "No paint material chosen: the car can't be painted in different colours.");
                if (stats.SkinnedMeshes > 0) Add(IssueLevel.Warning, "The car has skinned meshes; wheels inside a skinned mesh can't spin.");
            }
            if (category == AssetCategory.Character && stats.SkinnedMeshes == 0)
                Add(IssueLevel.Warning, "No skinned mesh: this character can't be animated.");

            var verdict = LicencePolicy.Check(licence);
            if (verdict.BlocksRelease) Add(IssueLevel.Warning, "Licence: " + verdict.Reason);
            return list;
        }

        public static bool HasErrors(List<ValidationIssue> issues) => issues.Exists(i => i.Level == IssueLevel.Error);
    }

    /// <summary>File-name helpers for imported assets.</summary>
    public static class StudioNames
    {
        public static readonly string[] ModelExtensions = { ".fbx", ".obj", ".glb", ".gltf", ".dae", ".blend", ".3ds", ".dxf" };

        public static bool IsModelFile(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Array.IndexOf(ModelExtensions, ext) >= 0;
        }

        /// <summary>"Red Coupe (2019) v2.fbx" → "RedCoupe2019V2" - safe for folders, asset names and ids.</summary>
        public static string SafeName(string name)
        {
            string n = Path.GetFileNameWithoutExtension(name ?? "");
            var sb = new System.Text.StringBuilder();
            bool upper = true;
            foreach (char ch in n)
            {
                if (char.IsLetterOrDigit(ch) && ch < 128)
                {
                    sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
                    upper = false;
                }
                else upper = true;
            }
            string s = sb.ToString();
            if (s.Length == 0) s = "Asset";
            if (char.IsDigit(s[0])) s = "A" + s;
            return s.Length > 48 ? s.Substring(0, 48) : s;
        }

        /// <summary>Stable lowercase id for a car definition: "imp_redcoupe".</summary>
        public static string CarId(string safeName) => "imp_" + SafeName(safeName).ToLowerInvariant();
    }
}
