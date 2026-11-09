using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarSales.Studio.EditorTools
{
    /// <summary>Phase 3b steps: characters, buildings, LODs, preview scene, materials and skies.</summary>
    public static partial class AssetPipeline
    {
        // ------------------------------------------------------------------ LODs
        /// <summary>
        /// Unity 6.2+ can generate mesh LODs on import. The setting's name isn't in older Unity versions, so it is set
        /// by reflection when it exists (props, buildings, environment; not characters or cars, which need exact shapes).
        /// </summary>
        static bool EnableMeshLods(ModelImporter mi, AssetCategory category)
        {
            if (category == AssetCategory.Character || category == AssetCategory.Vehicle) return false;
            var prop = typeof(ModelImporter).GetProperty("generateMeshLods", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite || (bool)prop.GetValue(mi)) return false;
            prop.SetValue(mi, true);
            return true;
        }

        /// <summary>
        /// Models that ship with LOD meshes ("Body_LOD0", "Body_LOD1"...) get a LODGroup. Meshes without a LOD suffix
        /// (wheels, small parts) stay visible in every level.
        /// </summary>
        static void ApplyLodGroup(GameObject root)
        {
            if (root.GetComponentInChildren<LODGroup>(true)) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var byLevel = new SortedDictionary<int, List<Renderer>>();
            var always = new List<Renderer>();
            foreach (var r in renderers)
            {
                int lod = ModelNaming.LodIndex(r.name);
                if (lod < 0 && r.transform.parent) lod = ModelNaming.LodIndex(r.transform.parent.name);
                if (lod < 0) { always.Add(r); continue; }
                if (!byLevel.TryGetValue(lod, out var list)) byLevel[lod] = list = new List<Renderer>();
                list.Add(r);
            }
            if (byLevel.Count < 2) return;
            var heights = ModelNaming.LodHeights(Math.Min(4, byLevel.Count));
            var lods = new List<LOD>();
            int i = 0;
            foreach (var level in byLevel.Values)
            {
                if (i >= heights.Length) { foreach (var r in level) r.gameObject.SetActive(false); continue; }   // more than 4 levels: drop the extra
                lods.Add(new LOD(heights[i], level.Concat(always).ToArray()));
                i++;
            }
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        // ------------------------------------------------------------------ BUILDINGS
        static void SetUpBuilding(ImportJob job, GameObject root)
        {
            var b = RendererBounds(root);
            var info = root.AddComponent<BuildingInfo>();
            info.function = job.buildingFunction;
            info.size = b.size;
            var entrance = new GameObject("Entrance").transform;
            entrance.SetParent(root.transform, false);
            entrance.localPosition = new Vector3(0f, 0f, b.max.z + 0.5f);   // in front of the building (+Z), on the ground
            info.entrance = entrance;
        }

        // ------------------------------------------------------------------ CHARACTERS
        public static bool IsHumanoid(string modelPath) =>
            AssetImporter.GetAtPath(modelPath) is ModelImporter mi && mi.animationType == ModelImporterAnimationType.Human;

        /// <summary>Sets the model's rig to Humanoid (needed to share Mixamo / store animations between characters).</summary>
        public static void MakeHumanoid(string modelPath)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter mi)) return;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
        }

        public static AnimationClip LoadClip(string path, string name) =>
            string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        /// <summary>Finds idle and walk clips in the model and in other files next to it (e.g. Mixamo downloads).</summary>
        public static void FindClips(ImportJob job)
        {
            string dir = Path.GetDirectoryName(job.modelPath)?.Replace('\\', '/');
            var files = new List<string> { job.modelPath };
            if (!string.IsNullOrEmpty(dir))
                files.AddRange(AssetDatabase.FindAssets("t:AnimationClip t:Model", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).Distinct());
            foreach (var f in files.Distinct())
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>())
                {
                    var role = ModelNaming.Role(clip.name);
                    if (role == ClipRole.None) role = ModelNaming.Role(Path.GetFileNameWithoutExtension(f));   // "Walking.fbx" / "mixamo.com"
                    if (role == ClipRole.Idle && string.IsNullOrEmpty(job.idleClipName)) { job.idleClipPath = f; job.idleClipName = clip.name; }
                    if (role == ClipRole.Walk && string.IsNullOrEmpty(job.walkClipName)) { job.walkClipPath = f; job.walkClipName = clip.name; }
                }
        }

        /// <summary>Animator with a "Speed" parameter: Idle ⇄ Walk (CustomerAI sets Speed from its NavMeshAgent).</summary>
        static void SetUpAnimation(ImportJob job, GameObject root)
        {
            var idle = LoadClip(job.idleClipPath, job.idleClipName);
            var walk = LoadClip(job.walkClipPath, job.walkClipName);
            if (!idle && !walk) return;
            MakeLooping(job.idleClipPath, job.idleClipName);
            MakeLooping(job.walkClipPath, job.walkClipName);
            idle = LoadClip(job.idleClipPath, job.idleClipName);
            walk = LoadClip(job.walkClipPath, job.walkClipName);

            string folder = StudioPaths.AssetFolder(job.category, job.SafeName);
            string path = $"{folder}/{job.SafeName}_Animator.controller";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idle ? idle : walk;
            sm.defaultState = idleState;
            if (walk)
            {
                var walkState = sm.AddState("Walk");
                walkState.motion = walk;
                var go = idleState.AddTransition(walkState);
                go.hasExitTime = false; go.duration = 0.15f;
                go.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
                var stop = walkState.AddTransition(idleState);
                stop.hasExitTime = false; stop.duration = 0.2f;
                stop.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
            }
            AssetDatabase.SaveAssets();

            var animator = root.GetComponentInChildren<Animator>(true) ?? root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            if (!animator.avatar)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(job.modelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar) animator.avatar = avatar;
            }
        }

        /// <summary>Idle and walk must loop. For clips inside a model file this is an import setting.</summary>
        static void MakeLooping(string path, string clipName)
        {
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter mi)) return;
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
                if ((c.name == clipName || clips.Length == 1) && !c.loopTime) { c.loopTime = true; changed = true; }
            if (!changed) return;
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ PREVIEW SCENE
        /// <summary>Opens a new, unsaved scene with the prefab, a ground grid and a 1.80 m person for scale.</summary>
        public static void OpenPreviewScene(GameObject prefab)
        {
            if (!prefab || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (10 m tiles)";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var b = RendererBounds(inst);
            var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            person.name = "Person 1.80 m (scale reference)";
            person.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            person.transform.position = new Vector3(b.min.x - 1.2f, 0.9f, b.center.z);
            Selection.activeGameObject = inst;
            if (SceneView.lastActiveSceneView)
            {
                b.Encapsulate(person.GetComponent<Renderer>().bounds);
                SceneView.lastActiveSceneView.Frame(b, false);
            }
        }

        // ------------------------------------------------------------------ MATERIALS & SKIES (Poly Haven)
        /// <summary>
        /// Builds a URP Lit material from downloaded maps. Roughness (and metalness) are packed into URP's
        /// metallic-smoothness texture (R = metal, A = 1 - roughness).
        /// </summary>
        public static Material CreateMaterial(string folder, string name, IDictionary<TextureSlot, string> files)
        {
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            Texture2D Load(TextureSlot s) => files.TryGetValue(s, out var p) ? AssetDatabase.LoadAssetAtPath<Texture2D>(p) : null;
            var baseMap = Load(TextureSlot.BaseColor);
            if (baseMap) { mat.SetTexture("_BaseMap", baseMap); mat.SetTexture("_MainTex", baseMap); }
            if (files.TryGetValue(TextureSlot.Normal, out var normalPath))
            {
                if (AssetImporter.GetAtPath(normalPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.SaveAndReimport();
                }
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                mat.EnableKeyword("_NORMALMAP");
            }
            var ao = Load(TextureSlot.Occlusion);
            if (ao) { mat.SetTexture("_OcclusionMap", ao); mat.EnableKeyword("_OCCLUSIONMAP"); }

            files.TryGetValue(TextureSlot.Roughness, out var roughPath);
            files.TryGetValue(TextureSlot.Metallic, out var metalPath);
            string packed = PackMetallicSmoothness(folder, name, metalPath, roughPath);
            if (packed != null)
            {
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packed));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f);
            }
            else mat.SetFloat("_Smoothness", 0.3f);
            mat.SetFloat("_Metallic", 0f);

            string path = $"{folder}/{name}.mat";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static string PackMetallicSmoothness(string folder, string name, string metalPath, string roughPath)
        {
            if (string.IsNullOrEmpty(roughPath)) return null;
            var rough = new Texture2D(2, 2);
            if (!rough.LoadImage(File.ReadAllBytes(roughPath))) return null;
            Texture2D metal = null;
            if (!string.IsNullOrEmpty(metalPath))
            {
                metal = new Texture2D(2, 2);
                if (!metal.LoadImage(File.ReadAllBytes(metalPath)) || metal.width != rough.width || metal.height != rough.height) metal = null;
            }
            var r = rough.GetPixels32();
            var m = metal ? metal.GetPixels32() : null;
            var outPixels = new Color32[r.Length];
            for (int i = 0; i < r.Length; i++)
            {
                byte metalValue = m != null ? m[i].r : (byte)0;
                outPixels[i] = new Color32(metalValue, metalValue, metalValue, (byte)(255 - r[i].r));
            }
            var tex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPixels);
            tex.Apply();
            string path = $"{folder}/{name}_MetallicSmoothness.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rough);
            if (metal) Object.DestroyImmediate(metal);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.sRGBTexture)
            {
                ti.sRGBTexture = false;   // data, not colour
                ti.SaveAndReimport();
            }
            return path;
        }

        /// <summary>A Skybox/Panoramic material from an .hdr file.</summary>
        public static Material CreateSky(string folder, string name, string hdrPath)
        {
            AssetDatabase.ImportAsset(hdrPath);
            if (AssetImporter.GetAtPath(hdrPath) is TextureImporter ti)
            {
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("Skybox/Panoramic");
            if (!shader) throw new InvalidOperationException("Shader 'Skybox/Panoramic' not found.");
            var mat = new Material(shader) { name = name };
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(hdrPath));
            string path = $"{folder}/{name}.mat";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static void UseAsSky(Material sky)
        {
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Adds or updates a registry record for an asset without a prefab (material, sky).</summary>
        public static AssetRecord RegisterAsset(AssetCategory category, string id, string displayName, Object mainAsset, LicenceInfo licence)
        {
            var reg = StudioPaths.Registry();
            var r = reg.Find(id);
            if (r == null) { r = new AssetRecord { id = id }; reg.records.Add(r); }
            else r.version++;
            r.displayName = displayName;
            r.category = category;
            r.mainAsset = mainAsset;
            r.sourcePath = AssetDatabase.GetAssetPath(mainAsset);
            r.licence = licence;
            r.importedAtUtc = DateTime.UtcNow.ToString("o");
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            return r;
        }
    }
}
