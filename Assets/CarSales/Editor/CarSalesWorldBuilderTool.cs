using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarSales.Sim;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace CarSales.EditorTools
{
    /// <summary>
    /// World Builder (spec §7.3, Phase 8): add working dealership sites to the town (optionally inside an imported
    /// building), keep them in Data/WorldLayout.asset so Build Everything rebuilds them, and validate every site.
    /// </summary>
    public static partial class CarSalesSetupWizard
    {
        public const string LayoutPath = "Assets/CarSales/Data/WorldLayout.asset";
        public const string DefaultLayoutPath = "Assets/CarSales/Data/Layout_DefaultTown.asset";

        public static WorldLayout LoadLayout(bool create)
        {
            var l = AssetDatabase.LoadAssetAtPath<WorldLayout>(LayoutPath);
            if (l || !create) return l;
            if (!AssetDatabase.IsValidFolder("Assets/CarSales/Data")) AssetDatabase.CreateFolder("Assets/CarSales", "Data");
            l = ScriptableObject.CreateInstance<WorldLayout>();
            AssetDatabase.CreateAsset(l, LayoutPath);
            return l;
        }

        static GameObject BuildingPrefab(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return null;
            var reg = AssetRegistry.Load();
            var r = reg ? reg.Find(assetId) : null;
            return r != null ? r.prefab : null;
        }

        /// <summary>Builds one World Builder site into the town (called by Build Everything and by the window).</summary>
        static GameObject BuildUserSite(Transform env, Mats m, SiteRecord rec, CarDisplay displayPrefab, CustomerAI customerPrefab, List<NavMeshSurface> surfaces)
        {
            GameObject site;
            SiteFunction function;
            switch (rec.template)
            {
                case SiteTemplate.Dealership:
                case SiteTemplate.PremiumDealership:
                {
                    var refs = new TownRefs();
                    bool premium = rec.template == SiteTemplate.PremiumDealership;
                    site = BuildDealershipSite(env, m, refs, displayPrefab, customerPrefab, rec.id, rec.name, rec.price > 0 ? rec.id : "", rec.position,
                                               false, premium ? 1.5f : 1f, premium, BuildingPrefab(rec.buildingAssetId), computer: true, yaw: rec.yaw);
                    surfaces?.AddRange(refs.surfaces);
                    var interior = BuildingPrefab(rec.interiorAssetId);
                    if (interior) FitBuilding(site.transform, interior, new Vector3(0f, 0.05f, 17f), new Vector3(21f, 4.5f, 11f), turn: false, colliders: false);
                    function = SiteFunction.Dealership;
                    break;
                }
                default:
                {
                    var g = Site(env, "Site_" + rec.id, rec.position, rec.yaw);
                    site = g.gameObject;
                    function = SiteFunction.Other;
                    switch (rec.template)
                    {
                        case SiteTemplate.Workshop: BuildWorkshop(g, m, CarDatabase.LoadDefault()); function = SiteFunction.Workshop; break;
                        case SiteTemplate.CarWash: BuildCarWashCenter(g, m); function = SiteFunction.CarWash; break;
                        case SiteTemplate.FuelStation: BuildFuelStation(g, m); function = SiteFunction.FuelStation; break;
                        case SiteTemplate.PartsShop: BuildPartsShop(g, m); function = SiteFunction.PartsShop; break;
                        case SiteTemplate.House: BuildHome(g, m, rec.id, rec.name, 1); function = SiteFunction.House; break;
                        case SiteTemplate.Apartments: BuildInvestment(g, m, rec.id, rec.name, new Vector3(16f, 13f, 10f), "APARTMENTS"); function = SiteFunction.Investment; break;
                        default:
                            var b = BuildingPrefab(rec.buildingAssetId);
                            if (b) FitBuilding(g, b, Vector3.zero, new Vector3(20f, 20f, 16f));
                            else Solid(g, "Building", new Vector3(0f, 6f, 8f), new Vector3(20f, 12f, 16f), m.buildings[0]);
                            break;
                    }
                    var place = site.GetComponent<MapPlace>();
                    if (place) place.placeName = rec.name;
                    break;
                }
            }
            site.name = "Site_" + rec.id;
            var s = site.GetComponent<Site>();
            if (!s) s = site.AddComponent<Site>();
            s.siteId = rec.id;
            s.displayName = rec.name;
            s.function = function;
            if (!s.entrance) s.entrance = site.GetComponent<MapPlace>() ? site.GetComponent<MapPlace>().arrivalPoint : site.transform;
            s.price = rec.price;
            s.dailyCost = rec.dailyCost;
            s.userBuilt = true;
            s.buildingAssetId = rec.buildingAssetId ?? "";
            return site;
        }

        // ------------------------------------------------------------------ ROADS (phase 8b)
        /// <summary>Asphalt, pavements, centre dashes and (optionally) trees and lamps for every road you drew.</summary>
        static void BuildUserRoads(Transform env, Mats m, WorldLayout layout)
        {
            var old = env.Find("UserRoads");
            if (old) Object.DestroyImmediate(old.gameObject);
            if (!layout || layout.userRoads.Count == 0) return;
            var root = Group(env, "UserRoads");
            foreach (var r in layout.userRoads)
            {
                var a = new Vector3(r.a.x, 0f, r.a.z); var b = new Vector3(r.b.x, 0f, r.b.z);
                float len = Vector3.Distance(a, b);
                if (len < 1f) continue;
                var g = Group(root, "Road");
                g.position = (a + b) / 2f;
                g.rotation = Quaternion.LookRotation(b - a);
                Solid(g, "Asphalt", new Vector3(0f, 0.009f, 0f), new Vector3(9f, 0.018f, len + 9f), m.asphalt);
                foreach (float side in new[] { -1f, 1f })
                    Solid(g, "Pavement", new Vector3(side * 5.7f, 0.06f, 0f), new Vector3(2.4f, 0.12f, len), m.sidewalk);
                for (float d = -len / 2f + 3f; d < len / 2f - 3f; d += 12f)
                    Deco(g, "CentreDash", PrimitiveType.Cube, new Vector3(0f, 0.02f, d), new Vector3(0.15f, 0.01f, 4f), m.line);
                var seg = new RoadSegment(r.a, r.b);
                if (r.trees)
                    foreach (var (pt, _) in RoadEditing.Scatter(seg, 24f, 8.5f, 12f)) Tree(root, new Vector3(pt.x, 0f, pt.z), m, 1f);
                if (r.lamps)
                    foreach (var (pt, facing) in RoadEditing.Scatter(seg, 40f, 6.6f, 20f))
                    {
                        var lg = Group(root, "LampSpot");
                        lg.position = new Vector3(pt.x, 0f, pt.z);
                        lg.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z));
                        StreetLamp(lg, Vector3.zero, 0.8f, 1.4f, m);
                    }
            }
        }

        /// <summary>Saves the roads you drew, rebuilds them in the town scene and updates the traffic network.</summary>
        public static string ApplyRoadsToTown()
        {
            if (!File.Exists(GameScenePath)) return "Run Build Everything first.";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var env = GameObject.Find("Town");
            if (!env) return "The town scene has no 'Town' object. Run Build Everything.";
            var layout = LoadLayout(true);
            BuildUserRoads(env.transform, LoadMats(), layout);
            var net = env.GetComponent<RoadNetwork>();
            if (!net) net = env.AddComponent<RoadNetwork>();
            net.segments = layout.Network();
            int swapped = PlaceholderReplacer.ApplyToOpenScene(false);
            EditorUtility.SetDirty(net);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return $"{layout.userRoads.Count} road(s) built; traffic network has {net.segments.Count} segments." + (swapped > 0 ? $" {swapped} placeholders use your models." : "");
        }

        /// <summary>Build Everything: rebuild the sites you added, tag the generated ones, write Layout_DefaultTown.</summary>
        static void BuildLayoutSites(Transform env, Mats m, CarDisplay displayPrefab, CustomerAI customerPrefab, List<NavMeshSurface> surfaces)
        {
            TagDefaultSites(env);
            var layout = LoadLayout(false);
            if (layout)
                foreach (var rec in layout.sites) BuildUserSite(env, m, rec, displayPrefab, customerPrefab, surfaces);

            BuildUserRoads(env, m, layout);
            var net = env.gameObject.GetComponent<RoadNetwork>();
            if (!net) net = env.gameObject.AddComponent<RoadNetwork>();
            net.segments = layout ? layout.Network() : RoadGraph.DefaultTown();

            // The generated v4 town as a layout (for reference and the validator).
            var def = AssetDatabase.LoadAssetAtPath<WorldLayout>(DefaultLayoutPath);
            if (!def) { def = ScriptableObject.CreateInstance<WorldLayout>(); AssetDatabase.CreateAsset(def, DefaultLayoutPath); }
            def.sites = env.GetComponentsInChildren<Site>().Where(x => !x.userBuilt).Select(x => new SiteRecord
            {
                id = x.siteId, name = x.displayName, function = x.function, position = x.transform.position, yaw = x.transform.eulerAngles.y,
            }).ToList();
            def.roads = RoadGraph.DefaultTown();
            EditorUtility.SetDirty(def);
        }

        /// <summary>Gives the generated service sites a Site component (from their map icon).</summary>
        static void TagDefaultSites(Transform env)
        {
            foreach (var place in env.GetComponentsInChildren<MapPlace>())
            {
                if (place.GetComponent<Site>()) continue;
                var s = place.gameObject.AddComponent<Site>();
                s.siteId = place.gameObject.name;
                s.displayName = place.placeName;
                s.entrance = place.arrivalPoint;
                s.function = place.glyph == "S" ? SiteFunction.ServiceCentre : place.glyph == "W" ? SiteFunction.Workshop : place.glyph == "C" ? SiteFunction.CarWash :
                             place.glyph == "F" ? SiteFunction.FuelStation : place.glyph == "A" ? SiteFunction.AuctionCenter : place.glyph == "U" ? SiteFunction.UsedCarMarket :
                             place.glyph == "H" ? SiteFunction.House : place.glyph == "$" ? SiteFunction.Investment : place.glyph == "P" ? SiteFunction.PartsShop :
                             place.glyph == "M" ? SiteFunction.Street : SiteFunction.Other;
            }
        }

        /// <summary>Adds a site to the open town scene and the layout, bakes its NavMesh and saves. Returns a message.</summary>
        public static string AddSiteToTown(SiteRecord rec)
        {
            if (!File.Exists(GameScenePath)) return "Run Build Everything first.";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var env = GameObject.Find("Town");
            if (!env) return "The town scene has no 'Town' object. Run Build Everything.";
            var displayPrefab = AssetDatabase.LoadAssetAtPath<CarDisplay>($"{Gen}/Prefabs/CarDisplay.prefab");
            var customerPrefab = AssetDatabase.LoadAssetAtPath<CustomerAI>($"{Gen}/Prefabs/Customer.prefab");
            if (!displayPrefab || !customerPrefab) return "Generated prefabs are missing. Run Build Everything.";
            var m = LoadMats();
            var surfaces = new List<NavMeshSurface>();
            var old = GameObject.Find("Site_" + rec.id);
            if (old) Object.DestroyImmediate(old);
            BuildUserSite(env.transform, m, rec, displayPrefab, customerPrefab, surfaces);
            foreach (var s in surfaces)
            {
                s.BuildNavMesh();
                if (s.navMeshData) { AssetDatabase.CreateAsset(s.navMeshData, $"{Gen}/Scenes/NavMesh_{rec.id}.asset"); EditorUtility.SetDirty(s); }
            }
            var layout = LoadLayout(true);
            layout.sites.RemoveAll(x => x.id == rec.id);
            layout.sites.Add(rec);
            EditorUtility.SetDirty(layout);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var issues = ValidateSite(GameObject.Find("Site_" + rec.id)?.GetComponent<Site>());
            return issues.Count == 0 ? $"{rec.name} built and saved. Validation: all good." : $"{rec.name} built. Validation: {string.Join(" ", issues)}";
        }

        public static string RemoveSite(string id)
        {
            var layout = LoadLayout(true);
            layout.sites.RemoveAll(x => x.id == id);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            if (File.Exists(GameScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
                var go = GameObject.Find("Site_" + id);
                if (go) Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset($"{Gen}/Scenes/NavMesh_{id}.asset");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            return "Site removed.";
        }

        // ------------------------------------------------------------------ VALIDATION
        /// <summary>
        /// Spec §7.3 checks: entrance on the NavMesh, a road within reach, a map place, and for dealerships enough spots,
        /// customer spawns, an exit and a NavMesh surface. Returns problems (empty = all good).
        /// </summary>
        public static List<string> ValidateSite(Site s)
        {
            var issues = new List<string>();
            if (!s) { issues.Add("Site not found."); return issues; }
            var p = s.EntrancePosition;
            if (s.function == SiteFunction.Other) return issues;
            if (!NavMesh.SamplePosition(p, out _, 4f, NavMesh.AllAreas)) issues.Add("Entrance is not on the NavMesh (bake it, or move the entrance).");
            var lanes = RoadGraph.Build(RoadGraph.DefaultTown());
            var net = Object.FindFirstObjectByType<RoadNetwork>();
            if (net && net.segments.Count > 0) lanes = RoadGraph.Build(net.segments, net.roadWidth);
            float road = lanes.Count == 0 ? 0f : lanes.Min(l => DistanceToLane(l, p));
            if (road > 45f) issues.Add($"No road within 45 m (nearest {road:0} m): customers and tow trucks need a road.");
            if (s.function == SiteFunction.Other) return issues;   // scenery: nothing else to check
            if (!s.GetComponentInChildren<MapPlace>()) issues.Add("No map place (it won't show on the map / GPS).");
            if (s.function == SiteFunction.Dealership)
            {
                var d = s.GetComponent<DealershipManager>();
                if (!d) issues.Add("Dealership without a DealershipManager.");
                else
                {
                    var so = new SerializedObject(d);
                    var spots = so.FindProperty("spots");
                    if (spots == null || spots.arraySize < 4) issues.Add("Fewer than 4 parking spots.");
                    var spawns = so.FindProperty("customerSpawnPoints");
                    if (spawns == null || spawns.arraySize == 0) issues.Add("No customer spawn points.");
                    if (!d.exitPoint) issues.Add("No customer exit point.");
                }
                if (!s.GetComponentInChildren<NavMeshSurface>()) issues.Add("No NavMesh surface for customers.");
                if (!s.GetComponentInChildren<DealerComputer>()) issues.Add("No computer (warning: you can still use the main one).");
            }
            return issues;
        }

        static float DistanceToLane(Lane l, Vector3 p)
        {
            var a = new Vector2(l.start.x, l.start.z); var b = new Vector2(l.end.x, l.end.z); var q = new Vector2(p.x, p.z);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            return Vector2.Distance(q, a + ab * t);
        }
    }

    /// <summary>Tools > Car Sales > World Builder.</summary>
    public class WorldBuilderWindow : EditorWindow
    {
        static readonly string[] Tabs = { "Sites", "Roads", "Validate" };
        int tab;
        Vector2 scroll;
        string status = "";
        string newName = "Riverside Motors";
        int buildingIndex, interiorIndex;
        bool drawing, newTrees = true, newLamps = true;
        Vector3? roadStart;
        SiteTemplate template = SiteTemplate.Dealership;
        int price = 120000, dailyCost = 600;
        Vector3 position = new Vector3(-200f, 0f, 30f);
        float yaw;
        readonly List<(string site, List<string> issues)> results = new List<(string, List<string>)>();

        [MenuItem("Tools/Car Sales/World Builder", priority = 30)]
        public static void Open() => GetWindow<WorldBuilderWindow>("World Builder").minSize = new Vector2(560f, 480f);

        void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(26));
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, UnityEditor.MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawSites(); else if (tab == 1) DrawRoads(); else DrawValidate();
            EditorGUILayout.EndScrollView();
        }

        void DrawSites()
        {
            EditorGUILayout.HelpBox("Add a working dealership to the town: lot with 14 parking spots, showroom (or one of your imported buildings), " +
                                    "computer, customers, sign and map icon. It's kept in Data/WorldLayout.asset, so Build Everything rebuilds it.", UnityEditor.MessageType.None);
            var layout = CarSalesSetupWizard.LoadLayout(false);
            EditorGUILayout.LabelField("Your sites", EditorStyles.boldLabel);
            if (layout == null || layout.sites.Count == 0) EditorGUILayout.LabelField("  None yet.");
            else
                foreach (var r in layout.sites.ToList())
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"{r.name}  ({r.template}, {(r.price > 0 ? $"${r.price:N0}" : "owned")}, at {r.position.x:0}, {r.position.z:0})");
                        if (GUILayout.Button("Rebuild", GUILayout.Width(70))) status = CarSalesSetupWizard.AddSiteToTown(r);
                        if (GUILayout.Button("Remove", GUILayout.Width(70)) && EditorUtility.DisplayDialog("World Builder", $"Remove {r.name} from the town?", "Remove", "Cancel"))
                            status = CarSalesSetupWizard.RemoveSite(r.id);
                    }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("New site", EditorStyles.boldLabel);
            newName = EditorGUILayout.TextField("Name", newName);
            template = (SiteTemplate)EditorGUILayout.EnumPopup("Type", template);
            EditorGUILayout.LabelField(TemplateHelp(template), EditorStyles.wordWrappedMiniLabel);
            var reg = AssetRegistry.Load();
            var buildings = reg ? reg.records.Where(r => r != null && r.prefab && r.category == CarSales.Studio.AssetCategory.Building).ToList() : new List<AssetRecord>();
            var names = new[] { "Glass showroom (standard)" }.Concat(buildings.Select(b => b.displayName)).ToArray();
            bool dealer = template == SiteTemplate.Dealership || template == SiteTemplate.PremiumDealership;
            if (dealer || template == SiteTemplate.Building)
                buildingIndex = EditorGUILayout.Popup("Building", Mathf.Clamp(buildingIndex, 0, names.Length - 1), names);
            var interiors = reg ? reg.records.Where(r => r != null && r.prefab && r.category == CarSales.Studio.AssetCategory.Interior).ToList() : new List<AssetRecord>();
            if (dealer)
                interiorIndex = EditorGUILayout.Popup("Showroom interior", Mathf.Clamp(interiorIndex, 0, interiors.Count), new[] { "None" }.Concat(interiors.Select(i => i.displayName)).ToArray());
            bool property = dealer || template == SiteTemplate.House || template == SiteTemplate.Apartments;
            if (property)
            {
                price = EditorGUILayout.IntField(new GUIContent("Price", "0 = yours from the start; otherwise buy it in Real Estate."), price);
                dailyCost = EditorGUILayout.IntField(template == SiteTemplate.Apartments ? "Income per day" : template == SiteTemplate.House ? "(unused)" : "Rent per day", dailyCost);
            }
            position = EditorGUILayout.Vector3Field("Position (lot centre)", position);
            yaw = EditorGUILayout.Slider("Turn (0 = facing south)", yaw, 0f, 359f);
            if (GUILayout.Button("Use the Scene view's centre")) { var sv = SceneView.lastActiveSceneView; if (sv) position = new Vector3(Mathf.Round(sv.pivot.x), 0f, Mathf.Round(sv.pivot.z)); }
            EditorGUILayout.HelpBox("The lot faces the road in front of it (local -Z): put it about 25 m from a road. The validator checks it.", UnityEditor.MessageType.None);
            if (GUILayout.Button("Build it in the town", GUILayout.Height(32)))
            {
                string id = "site_" + new string(newName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                var rec = new SiteRecord
                {
                    id = id, name = newName, template = template, buildingAssetId = buildingIndex > 0 && buildingIndex <= buildings.Count ? buildings[buildingIndex - 1].id : "",
                    interiorAssetId = interiorIndex > 0 && interiorIndex <= interiors.Count ? interiors[interiorIndex - 1].id : "",
                    position = position, yaw = yaw, price = property ? Mathf.Max(0, price) : 0, dailyCost = Mathf.Max(0, dailyCost), function = SiteFunction.Dealership,
                };
                status = CarSalesSetupWizard.AddSiteToTown(rec);
            }
        }

        static string TemplateHelp(SiteTemplate t)
        {
            switch (t)
            {
                case SiteTemplate.Dealership: return "Lot with 14 spots, showroom (or your building + interior), computer, customers.";
                case SiteTemplate.PremiumDealership: return "As a dealership, with richer customers (+50% budgets).";
                case SiteTemplate.Workshop: return "Mechanic workshop with two service bays (pay-per-repair).";
                case SiteTemplate.CarWash: return "Car wash with a wash bay.";
                case SiteTemplate.FuelStation: return "Fuel pumps and a kiosk.";
                case SiteTemplate.PartsShop: return "A Parts Direct counter (10% cheaper parts).";
                case SiteTemplate.House: return "A house you can buy and live in (wake at 07:00).";
                case SiteTemplate.Apartments: return "Rental apartments that pay you every night once bought.";
                default: return "Scenery: one of your imported buildings (or a plain block).";
            }
        }

        // ------------------------------------------------------------------ ROADS
        void OnEnable() => SceneView.duringSceneGui += OnScene;
        void OnDisable() { SceneView.duringSceneGui -= OnScene; drawing = false; }

        void DrawRoads()
        {
            EditorGUILayout.HelpBox("Draw straight roads in the Scene view (open the town scene first). Ends snap to existing roads within 8 m; a road " +
                                    "ending on another one makes a junction (traffic lights included). Then Apply: the roads, pavements, trees and lamps " +
                                    "are built and traffic uses them. Kept in Data/WorldLayout.asset.", UnityEditor.MessageType.None);
            var layout = CarSalesSetupWizard.LoadLayout(false);
            EditorGUILayout.LabelField("Your roads", EditorStyles.boldLabel);
            if (layout == null || layout.userRoads.Count == 0) EditorGUILayout.LabelField("  None yet.");
            else
                for (int i = 0; i < layout.userRoads.Count; i++)
                {
                    var r = layout.userRoads[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"({r.a.x:0}, {r.a.z:0}) to ({r.b.x:0}, {r.b.z:0})  -  {V2.Distance(r.a, r.b):0} m", GUILayout.Width(300));
                        bool t = GUILayout.Toggle(r.trees, "Trees", GUILayout.Width(60));
                        bool l = GUILayout.Toggle(r.lamps, "Lamps", GUILayout.Width(60));
                        if (t != r.trees || l != r.lamps) { Undo.RecordObject(layout, "Road"); r.trees = t; r.lamps = l; EditorUtility.SetDirty(layout); }
                        if (GUILayout.Button("X", GUILayout.Width(26))) { Undo.RecordObject(layout, "Road"); layout.userRoads.RemoveAt(i); EditorUtility.SetDirty(layout); break; }
                    }
                }
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                newTrees = GUILayout.Toggle(newTrees, "Trees along new roads");
                newLamps = GUILayout.Toggle(newLamps, "Lamps along new roads");
            }
            var label = drawing ? (roadStart.HasValue ? "Click the END point in the Scene view (Esc to cancel)" : "Click the START point in the Scene view") : "Draw a road";
            if (GUILayout.Button(label, GUILayout.Height(30))) { drawing = !drawing; roadStart = null; SceneView.RepaintAll(); }
            if (GUILayout.Button("Apply roads to the town", GUILayout.Height(30))) { AssetDatabase.SaveAssets(); status = CarSalesSetupWizard.ApplyRoadsToTown(); }
        }

        void OnScene(SceneView view)
        {
            var layout = CarSalesSetupWizard.LoadLayout(false);
            if (layout)
            {
                Handles.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                foreach (var r in layout.userRoads) Handles.DrawAAPolyLine(6f, new Vector3(r.a.x, 0.3f, r.a.z), new Vector3(r.b.x, 0.3f, r.b.z));
            }
            if (!drawing) return;
            var e = Event.current;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist)) return;
            var hit = ray.GetPoint(dist);
            var net = layout ? layout.Network() : RoadGraph.DefaultTown();
            var snapped = RoadEditing.Snap(net, new V2(hit.x, hit.z));
            var p = new Vector3(snapped.x, 0.3f, snapped.z);
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(p, Vector3.up, 2f);
            if (roadStart.HasValue) Handles.DrawAAPolyLine(5f, roadStart.Value, p);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { drawing = false; roadStart = null; Repaint(); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (!roadStart.HasValue) roadStart = p;
                else
                {
                    var l = CarSalesSetupWizard.LoadLayout(true);
                    var a = new V2(roadStart.Value.x, roadStart.Value.z);
                    if (V2.Distance(a, snapped) >= RoadEditing.MinLength)
                    {
                        Undo.RecordObject(l, "Draw road");
                        l.userRoads.Add(new UserRoad { a = a, b = snapped, trees = newTrees, lamps = newLamps });
                        EditorUtility.SetDirty(l);
                        status = "Road added. Draw another, or Apply roads to the town.";
                    }
                    roadStart = null;
                    drawing = false;
                    Repaint();
                }
                e.Use();
            }
            view.Repaint();
        }

        void DrawValidate()
        {
            EditorGUILayout.HelpBox("Checks every site in the open scene: entrance on the NavMesh, a road within reach, a map icon; dealerships also " +
                                    "need 4+ parking spots, customer spawns, an exit and a NavMesh surface.", UnityEditor.MessageType.None);
            if (GUILayout.Button("Validate the open scene", GUILayout.Height(30)))
            {
                results.Clear();
                foreach (var s in Object.FindObjectsByType<Site>(FindObjectsSortMode.None).OrderBy(x => x.displayName))
                    results.Add((s.displayName, CarSalesSetupWizard.ValidateSite(s)));
                if (results.Count == 0) status = "No sites in this scene. Open the town scene (Tools > Car Sales > Build Everything first).";
            }
            foreach (var (site, issues) in results)
            {
                if (issues.Count == 0) EditorGUILayout.HelpBox($"{site}: OK", UnityEditor.MessageType.Info);
                else EditorGUILayout.HelpBox($"{site}:\n- " + string.Join("\n- ", issues), issues.Any(i => !i.Contains("warning")) ? UnityEditor.MessageType.Error : UnityEditor.MessageType.Warning);
            }
        }
    }
}
