using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Swaps the town's placeholder primitives (backdrop buildings, trees, street lamps, benches) for models you
    /// imported with the Asset Studio (spec Phase 4 step 5). The choice is stored in
    /// Assets/CarSales/Data/PlaceholderReplacements.asset and re-applied every time Build Everything runs.
    /// </summary>
    public static class PlaceholderReplacer
    {
        public const string SettingsPath = "Assets/CarSales/Data/PlaceholderReplacements.asset";

        /// <summary>Which Asset Studio categories may replace a kind of placeholder.</summary>
        public static bool Accepts(PlaceholderKind kind, CarSales.Studio.AssetCategory category)
        {
            switch (kind)
            {
                case PlaceholderKind.Building: return category == CarSales.Studio.AssetCategory.Building;
                case PlaceholderKind.Tree: return category == CarSales.Studio.AssetCategory.Prop || category == CarSales.Studio.AssetCategory.Environment;
                default: return category == CarSales.Studio.AssetCategory.Prop;
            }
        }

        public static PlaceholderReplacements LoadSettings(bool create)
        {
            var s = AssetDatabase.LoadAssetAtPath<PlaceholderReplacements>(SettingsPath);
            if (s || !create) return s;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            if (!AssetDatabase.IsValidFolder("Assets/CarSales/Data")) AssetDatabase.CreateFolder("Assets/CarSales", "Data");
            s = ScriptableObject.CreateInstance<PlaceholderReplacements>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        /// <summary>Applies the stored choices to every PlaceholderSlot in the open scenes. Returns how many were replaced.</summary>
        public static int ApplyToOpenScene(bool rebakeNavMesh)
        {
            var settings = LoadSettings(false);
            var reg = AssetRegistry.Load();
            int replaced = 0;
            var models = new Dictionary<PlaceholderKind, List<GameObject>>();
            foreach (PlaceholderKind kind in System.Enum.GetValues(typeof(PlaceholderKind)))
            {
                var list = new List<GameObject>();
                if (settings && reg)
                    foreach (var id in settings.For(kind).assetIds)
                    {
                        var r = reg.Find(id);
                        if (r != null && r.prefab && Accepts(kind, r.category)) list.Add(r.prefab);
                    }
                models[kind] = list;
            }

            foreach (var slot in Object.FindObjectsByType<PlaceholderSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var list = models[slot.kind];
                if (Replace(slot, list.Count > 0 ? list[slot.StableHash() % list.Count] : null)) replaced++;
            }

            if (rebakeNavMesh)
                foreach (var surface in Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
                {
                    try
                    {
                        string oldPath = surface.navMeshData ? AssetDatabase.GetAssetPath(surface.navMeshData) : "";
                        surface.BuildNavMesh();
                        if (surface.navMeshData && !string.IsNullOrEmpty(oldPath))
                        {
                            AssetDatabase.DeleteAsset(oldPath);   // the surface now holds the new data
                            AssetDatabase.CreateAsset(surface.navMeshData, oldPath);
                        }
                        EditorUtility.SetDirty(surface);
                    }
                    catch (System.Exception e) { Debug.LogWarning("[CarSales] NavMesh re-bake failed (it bakes at runtime instead): " + e.Message); }
                }
            return replaced;
        }

        /// <summary>Puts <paramref name="prefab"/> in the slot (null = show the placeholder again). True if a model is in place.</summary>
        public static bool Replace(PlaceholderSlot slot, GameObject prefab)
        {
            var old = slot.Replacement;
            if (old) Object.DestroyImmediate(old.gameObject);
            SetPlaceholderVisible(slot, prefab == null, true);
            if (!prefab) return false;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot.transform);
            go.name = PlaceholderSlot.ReplacementName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            var b = WorldBounds(go);
            if (b.size.sqrMagnitude < 1e-6f) { Debug.LogWarning($"[CarSales] {prefab.name} has no visible mesh; skipped."); Object.DestroyImmediate(go); SetPlaceholderVisible(slot, true, true); return false; }
            Vector3 lossy = slot.transform.lossyScale;
            Vector3 target = Vector3.Scale(slot.footprint, lossy);
            float k;
            if (slot.kind == PlaceholderKind.Building)
            {
                // Footprint in the slot's own axes (the model may be turned 180°: same extents).
                float sx = target.x / Mathf.Max(0.01f, b.size.x), sz = target.z / Mathf.Max(0.01f, b.size.z);
                k = Mathf.Min(sx, sz);
            }
            else k = target.y / Mathf.Max(0.01f, b.size.y);
            go.transform.localScale = Vector3.one * k;

            // Stand it on the ground at the slot's centre.
            b = WorldBounds(go);
            Vector3 p = slot.transform.position;
            go.transform.position += new Vector3(p.x - b.center.x, p.y - b.min.y, p.z - b.center.z);

            // Imported colliders take over from the placeholder's boxes; without any, keep the boxes for collisions.
            bool hasColliders = go.GetComponentsInChildren<Collider>(true).Length > 0;
            SetPlaceholderVisible(slot, false, !hasColliders);
            return true;
        }

        static void SetPlaceholderVisible(PlaceholderSlot slot, bool renderers, bool colliders)
        {
            foreach (Transform child in slot.transform)
            {
                if (child.name == PlaceholderSlot.ReplacementName) continue;
                foreach (var r in child.GetComponentsInChildren<Renderer>(true)) r.enabled = renderers;
                foreach (var c in child.GetComponentsInChildren<Collider>(true)) c.enabled = colliders;
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Opens the town scene, applies the choices, re-bakes the NavMesh and saves.</summary>
        public static string ApplyToTownScene()
        {
            string path = CarSalesSetupWizard.GameScenePath;
            if (!File.Exists(path)) return "Run Tools > Car Sales > Build Everything first.";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int n = ApplyToOpenScene(true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            int total = Object.FindObjectsByType<PlaceholderSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            return $"Town updated: {n} of {total} placeholders now use your models.";
        }

        public static Dictionary<PlaceholderKind, int> CountSlotsInOpenScene()
        {
            var d = new Dictionary<PlaceholderKind, int>();
            foreach (var s in Object.FindObjectsByType<PlaceholderSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                d[s.kind] = d.TryGetValue(s.kind, out var n) ? n + 1 : 1;
            return d;
        }
    }
}
