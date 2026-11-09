using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CarSales.Sim;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarSales.Studio.EditorTools
{
    public static class StudioPaths
    {
        public const string ArtRoot = "Assets/CarSales/Art";
        public const string DropFolder = ArtRoot + "/_Drop";
        public const string LibraryRoot = "Assets/CarSales/Library";
        public const string RegistryPath = LibraryRoot + "/Resources/" + AssetRegistry.ResourceName + ".asset";
        public const string CarsFolder = "Assets/CarSales/Data/Cars";
        public const string CreditsPath = LibraryRoot + "/CREDITS.txt";

        public static string AssetFolder(AssetCategory c, string safeName) => $"{ArtRoot}/{c}/{safeName}";

        public static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            if (Directory.Exists(path)) { AssetDatabase.ImportAsset(path); return; }   // made on disk (download): just import it
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static AssetRegistry Registry()
        {
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(RegistryPath);
            if (reg) return reg;
            Ensure(Path.GetDirectoryName(RegistryPath).Replace('\\', '/'));
            reg = ScriptableObject.CreateInstance<AssetRegistry>();
            AssetDatabase.CreateAsset(reg, RegistryPath);
            AssetDatabase.SaveAssets();
            return reg;
        }
    }

    /// <summary>Everything the Asset Studio knows about the model being set up.</summary>
    [Serializable]
    public class ImportJob
    {
        public string modelPath = "";
        public AssetCategory category = AssetCategory.Vehicle;
        public string displayName = "";
        public LicenceInfo licence = new LicenceInfo();

        public float scale = 1f;
        public int yaw;                 // 0, 90, 180, 270
        public bool zUpFix;
        public string suggestion = "";

        // Vehicle
        public string[] wheelPaths = new string[4];   // FL, FR, RL, RR (transform paths inside the model)
        public List<string> paintMaterials = new List<string>();
        public string brand = "Imported";
        public CarCategory carCategory = CarCategory.Sedan;
        public int year = 2018;
        public int basePrice = 20000;
        public string fuelType = "Petrol";
        public string transmission = "Automatic";

        // Character
        public string idleClipPath = "", idleClipName = "", walkClipPath = "", walkClipName = "";
        public bool useAsCustomer = true;

        // Building
        public BuildingFunction buildingFunction = BuildingFunction.Decorative;

        // Results of the last analysis (not edited by the user)
        [NonSerialized] public ModelStats stats;
        [NonSerialized] public Vector3 finalSize;
        [NonSerialized] public List<string> rendererPaths = new List<string>();
        [NonSerialized] public List<string> materialNames = new List<string>();
        [NonSerialized] public List<ValidationIssue> issues = new List<ValidationIssue>();

        public GameObject Model => AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        public string SafeName => StudioNames.SafeName(string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(modelPath) : displayName);
        public bool WheelsAssigned => wheelPaths != null && wheelPaths.Length == 4 && wheelPaths.All(p => p != null);
        public float MeasuredSize => category == AssetCategory.Vehicle ? finalSize.z
                                   : category == AssetCategory.Character ? finalSize.y
                                   : Mathf.Max(finalSize.x, finalSize.y, finalSize.z);

        public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(zUpFix ? -90f : 0f, 0f, 0f);
    }

    /// <summary>The import steps (spec §7.1): ingest, configure, analyse, normalise, materials, components, prefab, registry.</summary>
    public static partial class AssetPipeline
    {
        static readonly Regex PaintName = new Regex("(paint|body|carpaint|car_paint|exterior|shell|chassis)", RegexOptions.IgnoreCase);
        static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr", ".bmp" };

        // ------------------------------------------------------------------ 1. INGEST
        /// <summary>
        /// Copies a model file from anywhere on disk (and the textures next to it, plus a "textures" folder) into
        /// Art/&lt;Category&gt;/&lt;Name&gt;/Source and imports it. Returns the asset path of the copied model.
        /// </summary>
        public static string Ingest(string externalPath, AssetCategory category)
        {
            if (!File.Exists(externalPath)) throw new FileNotFoundException("File not found", externalPath);
            string safe = StudioNames.SafeName(externalPath);
            string folder = StudioPaths.AssetFolder(category, safe) + "/Source";
            StudioPaths.Ensure(folder);
            string srcDir = Path.GetDirectoryName(externalPath);
            string target = $"{folder}/{Path.GetFileName(externalPath)}";
            File.Copy(externalPath, target, true);

            // .gltf files reference .bin and textures next to them; FBX/OBJ often have textures alongside.
            foreach (var f in Directory.GetFiles(srcDir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (f == externalPath) continue;
                if (TextureExtensions.Contains(ext) || ext == ".bin" || ext == ".mtl") File.Copy(f, $"{folder}/{Path.GetFileName(f)}", true);
            }
            foreach (var sub in Directory.GetDirectories(srcDir))
            {
                string name = Path.GetFileName(sub).ToLowerInvariant();
                if (name != "textures" && name != "texture" && name != "maps" && name != "images") continue;
                string dst = $"{folder}/{Path.GetFileName(sub)}";
                Directory.CreateDirectory(dst);
                foreach (var f in Directory.GetFiles(sub))
                    if (TextureExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) File.Copy(f, $"{dst}/{Path.GetFileName(f)}", true);
            }
            AssetDatabase.Refresh();
            Configure(target, category);
            return target;
        }

        // ------------------------------------------------------------------ 2. IMPORT SETTINGS
        public static void Configure(string modelPath, AssetCategory category)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter mi)) return;   // glTF and prefabs have their own importers
            bool changed = false;
            void SetIf<T>(T current, T wanted, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { set(wanted); changed = true; } }
            SetIf(mi.isReadable, category == AssetCategory.Vehicle, v => mi.isReadable = v);         // dents need readable meshes
            SetIf(mi.importAnimation, category == AssetCategory.Character, v => mi.importAnimation = v);
            SetIf(mi.importCameras, false, v => mi.importCameras = v);
            SetIf(mi.importLights, false, v => mi.importLights = v);
            changed |= EnableMeshLods(mi, category);
            if (changed) mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ 3. ANALYSE
        /// <summary>Measures the model with the job's current scale/rotation and lists its meshes and materials.</summary>
        public static void Analyse(ImportJob job, bool suggestScale, bool detectParts = false)
        {
            var model = job.Model;
            job.issues = new List<ValidationIssue>();
            if (!model) { job.issues.Add(new ValidationIssue { Level = IssueLevel.Error, Message = "No model loaded." }); return; }

            if (suggestScale)
            {
                var raw = Measure(model, Quaternion.identity, 1f, out _);
                var s = ScaleHeuristics.Suggest(job.category, raw.size.x, raw.size.y, raw.size.z);
                job.scale = s.Factor;
                job.zUpFix = s.LooksZUp;
                job.yaw = s.RotateLengthToZ ? 90 : 0;
                job.suggestion = s.Reason;
            }

            var m = Measure(model, job.Rotation, job.scale, out var instanceInfo);
            job.finalSize = m.size;
            job.stats = instanceInfo.stats;
            job.rendererPaths = instanceInfo.rendererPaths;
            job.materialNames = instanceInfo.materials;
            if ((suggestScale || detectParts) && job.category == AssetCategory.Vehicle)
            {
                DetectWheels(job, instanceInfo.wheelCandidates);
                job.paintMaterials = job.materialNames.Where(n => PaintName.IsMatch(n)).ToList();
            }
            int paintRenderers = job.paintMaterials.Count;
            job.issues = ImportValidation.Validate(job.category, job.stats, job.MeasuredSize, job.licence, job.WheelsAssigned, paintRenderers);
        }

        class InstanceInfo
        {
            public ModelStats stats;
            public List<string> rendererPaths = new List<string>();
            public List<string> materials = new List<string>();
            public List<(string path, WheelCandidate c)> wheelCandidates = new List<(string, WheelCandidate)>();
        }

        static Bounds Measure(GameObject model, Quaternion rotation, float scale, out InstanceInfo info)
        {
            info = new InstanceInfo();
            var root = new GameObject("StudioMeasure") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var inst = (GameObject)Object.Instantiate(model, root.transform);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.localRotation = rotation * inst.transform.localRotation;
                inst.transform.localScale *= scale;
                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                var bounds = new Bounds();
                bool any = false;
                var mats = new HashSet<Material>();
                var stats = new ModelStats();
                foreach (var r in renderers)
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    stats.Renderers++;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        stats.SkinnedMeshes++;
                        CountMesh(smr.sharedMesh, ref stats);
                    }
                    else if (r.TryGetComponent<MeshFilter>(out var mf)) CountMesh(mf.sharedMesh, ref stats);
                    foreach (var mat in r.sharedMaterials) if (mat) mats.Add(mat);
                    info.rendererPaths.Add(AnimationUtility.CalculateTransformPath(r.transform, inst.transform));
                }
                stats.Materials = mats.Count;
                stats.MaterialsWithoutTexture = mats.Count(m => !HasBaseTexture(m));
                stats.HasLodGroup = inst.GetComponentInChildren<LODGroup>(true);
                info.materials = mats.Select(m => m.name).Distinct().OrderBy(n => n).ToList();

                // Wheel candidates, positions relative to the (future) ground centre.
                Vector3 offset = any ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : Vector3.zero;
                foreach (var r in renderers)
                {
                    if (!WheelDetection.LooksLikeWheel(r.name) && !WheelDetection.LooksLikeWheel(r.transform.parent ? r.transform.parent.name : "")) continue;
                    var c = r.bounds.center - offset;
                    info.wheelCandidates.Add((AnimationUtility.CalculateTransformPath(r.transform, inst.transform),
                        new WheelCandidate { Id = info.wheelCandidates.Count, Name = r.name, X = c.x, Y = c.y, Z = c.z, Radius = r.bounds.extents.y }));
                }
                info.stats = stats;
                return any ? bounds : new Bounds();
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void CountMesh(Mesh mesh, ref ModelStats stats)
        {
            if (!mesh) return;
            stats.Vertices += mesh.vertexCount;
            for (int i = 0; i < mesh.subMeshCount; i++) stats.Triangles += (int)(mesh.GetIndexCount(i) / 3);
            if (!mesh.isReadable) stats.UnreadableMeshes++;
        }

        static bool HasBaseTexture(Material m) =>
            (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex")) ||
            (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture"));

        static void DetectWheels(ImportJob job, List<(string path, WheelCandidate c)> candidates)
        {
            job.wheelPaths = new string[4];
            var picked = WheelDetection.AssignCorners(candidates.Select(c => c.c).ToList());
            if (picked == null) return;
            for (int i = 0; i < 4; i++)
            {
                // Use the wheel's parent if it groups tyre + rim (e.g. "Wheel_FL/Tyre", "Wheel_FL/Rim").
                string path = candidates[picked[i]].path;
                int slash = path.LastIndexOf('/');
                string parent = slash > 0 ? path.Substring(0, slash) : null;
                bool parentIsWheel = parent != null && WheelDetection.LooksLikeWheel(parent.Substring(parent.LastIndexOf('/') + 1));
                job.wheelPaths[i] = parentIsWheel ? parent : path;
            }
        }

        // ------------------------------------------------------------------ 4. MATERIALS
        /// <summary>True for shaders that render pink or wrong in URP (Built-in Standard, legacy...).</summary>
        public static bool NeedsConversion(Material m)
        {
            if (!m || !m.shader) return true;
            string n = m.shader.name;
            if (n == "Hidden/InternalErrorShader") return true;
            if (n.StartsWith("Universal Render Pipeline/") || n.StartsWith("Shader Graphs/") || n.Contains("glTF") || n.StartsWith("HDRP/")) return false;
            return n == "Standard" || n == "Standard (Specular setup)" || n.StartsWith("Legacy Shaders/") || n == "Autodesk Interactive" ||
                   n.StartsWith("Mobile/") || n == "Diffuse" || n == "Bumped Diffuse";
        }

        /// <summary>
        /// Makes every material of the model URP-compatible. Materials inside a model file are extracted to
        /// editable .mat assets first. Returns how many were converted.
        /// </summary>
        public static int FixMaterials(ImportJob job)
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (!urp) return 0;
            int converted = 0;
            string matFolder = StudioPaths.AssetFolder(job.category, job.SafeName) + "/Materials";

            // Materials embedded in a model file are read-only: extract the ones that need work.
            var embedded = AssetDatabase.LoadAllAssetsAtPath(job.modelPath).OfType<Material>().Where(NeedsConversion).ToList();
            if (embedded.Count > 0)
            {
                StudioPaths.Ensure(matFolder);
                foreach (var m in embedded)
                {
                    string error = AssetDatabase.ExtractAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{matFolder}/{StudioNames.SafeName(m.name)}.mat"));
                    if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[Asset Studio] Could not extract material {m.name}: {error}");
                }
                AssetDatabase.WriteImportSettingsIfDirty(job.modelPath);
                AssetDatabase.ImportAsset(job.modelPath, ImportAssetOptions.ForceUpdate);
            }

            var model = job.Model;
            if (!model) return 0;
            var textures = SourceTextures(job);
            foreach (var mat in model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(x => x).Distinct())
            {
                if (!AssetDatabase.IsMainAsset(mat)) continue;   // still embedded (nothing to fix) or built-in
                bool changed = false;
                if (NeedsConversion(mat)) { ToUrpLit(mat, urp); changed = true; converted++; }
                if (mat.HasProperty("_BaseMap") && !mat.GetTexture("_BaseMap")) changed |= FillFromSource(mat, textures);
                if (changed) EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            return converted;
        }

        static void ToUrpLit(Material m, Shader urp)
        {
            Texture Tex(string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
            Color Col(string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
            float F(string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;
            var baseMap = Tex("_MainTex"); var normal = Tex("_BumpMap"); var metal = Tex("_MetallicGlossMap");
            var occlusion = Tex("_OcclusionMap"); var emission = Tex("_EmissionMap");
            var color = Col("_Color", Color.white); var emissionColor = Col("_EmissionColor", Color.black);
            float metallic = F("_Metallic", 0f), smooth = F("_Glossiness", 0.5f);

            m.shader = urp;
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (metal) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            if (occlusion) { m.SetTexture("_OcclusionMap", occlusion); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (emission || emissionColor.maxColorComponent > 0.01f)
            {
                m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
            }
        }

        static List<Texture2D> SourceTextures(ImportJob job)
        {
            string dir = Path.GetDirectoryName(job.modelPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir)) return new List<Texture2D>();
            return AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t).ToList();
        }

        /// <summary>Assigns textures whose file name contains the material's name (e.g. "Body" → Body_BaseColor.png, Body_Normal.png).</summary>
        static bool FillFromSource(Material m, List<Texture2D> textures)
        {
            string key = StudioNames.SafeName(m.name).ToLowerInvariant();
            var mine = textures.Where(t => StudioNames.SafeName(t.name).ToLowerInvariant().Contains(key)).ToList();
            if (mine.Count == 0 && textures.Count <= 6) mine = textures;   // one small texture set for the whole model
            bool changed = false;
            foreach (var t in mine)
            {
                switch (TextureSlots.Classify(AssetDatabase.GetAssetPath(t)))
                {
                    case TextureSlot.BaseColor when !m.GetTexture("_BaseMap"):
                        m.SetTexture("_BaseMap", t); changed = true; break;
                    case TextureSlot.Normal when m.HasProperty("_BumpMap") && !m.GetTexture("_BumpMap"):
                        MarkNormalMap(t);
                        m.SetTexture("_BumpMap", t); m.EnableKeyword("_NORMALMAP"); changed = true; break;
                    case TextureSlot.Occlusion when m.HasProperty("_OcclusionMap") && !m.GetTexture("_OcclusionMap"):
                        m.SetTexture("_OcclusionMap", t); m.EnableKeyword("_OCCLUSIONMAP"); changed = true; break;
                }
            }
            return changed;
        }

        static void MarkNormalMap(Texture2D t)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ 5. PREFAB
        /// <summary>Builds (or rebuilds) the game-ready prefab and its registry record. Returns the record.</summary>
        public static AssetRecord Approve(ImportJob job)
        {
            var model = job.Model;
            if (!model) throw new InvalidOperationException("No model.");
            string safe = job.SafeName;
            string folder = StudioPaths.AssetFolder(job.category, safe);
            StudioPaths.Ensure(folder);
            string prefabPath = $"{folder}/{safe}.prefab";
            var registry = StudioPaths.Registry();
            var record = registry.Find(safe);

            // Keep the previous version for rollback.
            if (File.Exists(prefabPath) && record != null)
            {
                StudioPaths.Ensure(folder + "/History");
                AssetDatabase.CopyAsset(prefabPath, $"{folder}/History/{safe}_v{record.version}.prefab");
            }

            var root = new GameObject(safe);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = job.Rotation * inst.transform.localRotation;
                inst.transform.localScale *= job.scale;
                // Sit on the ground, centred.
                var b = RendererBounds(root);
                inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);

                switch (job.category)
                {
                    case AssetCategory.Vehicle: SetUpVehicle(job, root, inst); break;
                    case AssetCategory.Building: SetUpStatic(root); SetUpBuilding(job, root); break;
                    case AssetCategory.Environment: SetUpStatic(root); break;
                    case AssetCategory.Character: SetUpCharacter(root); SetUpAnimation(job, root); break;
                    default: SetUpProp(root); break;
                }
                ApplyLodGroup(root);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (record == null)
                {
                    record = new AssetRecord { id = safe };
                    registry.records.Add(record);
                }
                else record.version++;
                record.displayName = string.IsNullOrWhiteSpace(job.displayName) ? safe : job.displayName.Trim();
                record.category = job.category;
                record.sourcePath = job.modelPath;
                record.prefab = prefab;
                record.licence = job.licence;
                record.importedAtUtc = DateTime.UtcNow.ToString("o");
                record.jobJson = JsonUtility.ToJson(job);   // to reopen the job later
                if (job.category == AssetCategory.Vehicle) record.carDefinition = CreateCarDefinition(job, prefab);
                record.useInGame = job.category != AssetCategory.Character || job.useAsCustomer;
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                return record;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Bounds RendererBounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void SetUpVehicle(ImportJob job, GameObject root, GameObject model)
        {
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);   // the game adds its own

            var rig = root.AddComponent<VehicleRig>();
            var wheels = new Transform[4];
            for (int i = 0; i < 4; i++)
                if (!string.IsNullOrEmpty(job.wheelPaths[i])) wheels[i] = model.transform.Find(job.wheelPaths[i]);
            rig.wheelFrontLeft = wheels[0]; rig.wheelFrontRight = wheels[1]; rig.wheelRearLeft = wheels[2]; rig.wheelRearRight = wheels[3];

            var visual = root.AddComponent<CarVisual>();
            var renderers = new List<Object>();
            var slots = new List<int>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && job.paintMaterials.Contains(mats[i].name)) { renderers.Add(r); slots.Add(i); }
            }
            var so = new SerializedObject(visual);
            var pr = so.FindProperty("paintRenderers");
            pr.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++) pr.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            var pi = so.FindProperty("paintMaterialIndices");
            pi.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++) pi.GetArrayElementAtIndex(i).intValue = slots[i];
            so.FindProperty("colorProperty").stringValue = "_BaseColor";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetUpStatic(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (!mf.GetComponent<Collider>() && mf.sharedMesh) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                                     StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                                                     StaticEditorFlags.ReflectionProbeStatic);
        }

        static void SetUpProp(GameObject root)
        {
            if (root.GetComponentInChildren<Collider>(true)) return;
            var b = RendererBounds(root);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }

        static void SetUpCharacter(GameObject root)
        {
            var b = RendererBounds(root);
            var cap = root.AddComponent<CapsuleCollider>();
            cap.height = b.size.y;
            cap.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.5f, 0.2f, 0.5f);
            cap.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            if (!root.GetComponentInChildren<Animator>(true)) root.AddComponent<Animator>();
        }

        static CarDefinition CreateCarDefinition(ImportJob job, GameObject prefab)
        {
            StudioPaths.Ensure(StudioPaths.CarsFolder);
            string id = StudioNames.CarId(job.SafeName);
            string path = $"{StudioPaths.CarsFolder}/Car_{id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<CarDefinition>(path);
            bool created = !def;
            if (created) def = ScriptableObject.CreateInstance<CarDefinition>();
            def.carId = id;
            def.displayName = string.IsNullOrWhiteSpace(job.displayName) ? job.SafeName : job.displayName.Trim();
            def.brand = job.brand;
            def.category = job.carCategory;
            def.year = job.year;
            def.basePrice = Mathf.Max(500, job.basePrice);
            def.fuelType = job.fuelType;
            def.transmission = job.transmission;
            def.displayPrefab = prefab;
            if (string.IsNullOrEmpty(def.description)) def.description = $"Imported with the Asset Studio ({job.licence.source}).";
            if (created) AssetDatabase.CreateAsset(def, path);
            EditorUtility.SetDirty(def);

            // Add it to the game's car list now (Build Everything also picks up everything in Data/Cars).
            foreach (var g in AssetDatabase.FindAssets("t:CarDatabase"))
            {
                var db = AssetDatabase.LoadAssetAtPath<CarDatabase>(AssetDatabase.GUIDToAssetPath(g));
                if (db && !db.cars.Contains(def)) { db.cars.Add(def); EditorUtility.SetDirty(db); }
            }
            return def;
        }

        // ------------------------------------------------------------------ ROLLBACK / REMOVE
        public static string LatestHistory(AssetRecord r)
        {
            if (r?.prefab == null) return null;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(r.prefab))?.Replace('\\', '/') + "/History";
            string path = $"{folder}/{r.id}_v{r.version - 1}.prefab";
            return File.Exists(path) ? path : null;
        }

        /// <summary>Puts the previous prefab version back (same asset, same GUID, so every reference keeps working).</summary>
        public static bool Rollback(AssetRecord r)
        {
            string history = LatestHistory(r);
            if (history == null) return false;
            string prefabPath = AssetDatabase.GetAssetPath(r.prefab);
            File.Copy(history, prefabPath, true);
            AssetDatabase.DeleteAsset(history);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            r.version--;
            EditorUtility.SetDirty(StudioPaths.Registry());
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>Removes the prefab, car definition and record. The source files stay in Art/.../Source.</summary>
        public static void Remove(AssetRecord r)
        {
            var reg = StudioPaths.Registry();
            if (r.carDefinition)
            {
                foreach (var g in AssetDatabase.FindAssets("t:CarDatabase"))
                {
                    var db = AssetDatabase.LoadAssetAtPath<CarDatabase>(AssetDatabase.GUIDToAssetPath(g));
                    if (db && db.cars.Remove(r.carDefinition)) EditorUtility.SetDirty(db);
                }
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(r.carDefinition));
            }
            if (r.prefab) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(r.prefab));
            reg.records.Remove(r);
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
        }
    }
}
