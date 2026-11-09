using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarSales.EditorTools
{
    /// <summary>
    /// One-click generator: Tools > Car Sales > 2. Build Everything
    /// Creates materials, car models + data, prefabs, the dealership world and both scenes inside
    /// Assets/CarSales/Generated. The UI is NOT generated here: it builds itself from code at runtime.
    /// Safe to run again (it rebuilds the Generated folder).
    /// </summary>
    public static partial class CarSalesSetupWizard
    {
        const string Root = "Assets/CarSales";
        const string Gen = Root + "/Generated";
        const string InputPath = Root + "/Input/CarSalesControls.inputactions";
        const string ScenePath = Gen + "/Scenes/DealershipTest.unity";
        public const string GameScenePath = ScenePath;
        const string MenuScenePath = Gen + "/Scenes/MainMenu.unity";
        const int InteractLayer = 20;
        const int StudioLayer = 21;
        const int MinimapLayer = 22;

        static string colorProperty = "_BaseColor";
        static Shader litShader;

        // ------------------------------------------------------------------ MENU
        [MenuItem("Tools/Car Sales/2. Build Everything", priority = 1)]
        public static void BuildEverything()
        {
            if (!EditorUtility.DisplayDialog("Car Sales Simulator",
                    "This will (re)create everything inside\n\n" + Gen +
                    "\n\n(materials, car models, data, prefabs, the dealership world and the scenes).\n" +
                    "Anything you changed inside that folder will be replaced.\n\nContinue?",
                    "Build", "Cancel")) return;

            if (!CheckTextMeshPro()) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                Progress("Project settings", 0.02f);
                if (!SetupLayers()) return;
                var actions = LoadActions();
                if (actions == null) return;

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (AssetDatabase.IsValidFolder(Gen)) AssetDatabase.DeleteAsset(Gen);
                foreach (var f in new[] { "Materials", "Data/Cars", "Prefabs/CarModels", "Scenes", "Resources" })
                    EnsureFolder(Gen + "/" + f);

                Progress("Materials", 0.08f);
                var mats = CreateMaterials();

                Progress("Car models & data", 0.18f);
                var db = CreateCars(mats);

                Progress("Prefabs", 0.3f);
                var carDisplay = CreateCarDisplayPrefab();
                var customer = CreateCustomerPrefab(mats);

                Progress("Dealership world", 0.45f);
                BuildGameScene(mats, db, carDisplay, customer, actions);

                Progress("Main menu", 0.95f);
                BuildMainMenuScene(mats, db);

                Progress("Test track", 0.97f);
                BuildTestTrackScene(mats, db, actions);
                EditorSceneManager.OpenScene(ScenePath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Car Sales Simulator",
                    "Done!\n\nGame scene: " + ScenePath + "\nMain menu: " + MenuScenePath +
                    "\nTest track: " + TestTrackScenePath +
                    "\n\nPress PLAY here to jump straight in, or open the MainMenu scene for the full flow.\n\n" +
                    "E interact | F drive | Tab inventory | M map | P phone | Esc menu\nSleep at home to end the day.\nF1 +$10,000 (debug) | F5/F9 save/load | F12 delete save",
                    "Let's go");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Car Sales Simulator", "Build failed:\n" + e.Message + "\n\nSee the Console for details.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/Car Sales/3. Open Test Scene", priority = 2)]
        static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Car Sales", "Run 'Build Everything' first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Car Sales/4. Open Main Menu Scene", priority = 3)]
        static void OpenMenuScene()
        {
            if (!File.Exists(MenuScenePath)) { EditorUtility.DisplayDialog("Car Sales", "Run 'Build Everything' first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MenuScenePath);
        }

        // Field name on our components -> action name in CarSalesControls
        static readonly Dictionary<string, string> ActionFieldMap = new Dictionary<string, string>
        {
            { "moveAction", "Move" }, { "lookAction", "Look" }, { "jumpAction", "Jump" }, { "sprintAction", "Sprint" },
            { "interactAction", "Interact" }, { "toggleShopAction", "ToggleShop" }, { "closeAction", "CloseMenu" },
            { "brakeAction", "Jump" }, { "exitAction", "Interact" }, { "inventoryAction", "Inventory" },
            { "vehicleAction", "Vehicle" }, { "mapAction", "Map" }, { "phoneAction", "Phone" }, { "cameraAction", "CameraView" },
            { "gearUpAction", "GearUp" }, { "gearDownAction", "GearDown" },
        };

        /// <summary>Re-links every empty/broken InputActionReference in open scenes and Car Sales prefabs.</summary>
        [MenuItem("Tools/Car Sales/Re-link Input Actions", priority = 10)]
        public static void RelinkInputActions()
        {
            AssetDatabase.ImportAsset(InputPath, ImportAssetOptions.ForceUpdate);
            var refs = AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
                .Where(r => r.action != null).ToDictionary(r => r.action.name, r => r);
            if (refs.Count == 0) { EditorUtility.DisplayDialog("Car Sales", "No input actions found in " + InputPath, "OK"); return; }

            int fixedCount = 0;
            int Relink(MonoBehaviour mb)
            {
                if (!mb || mb.GetType().Namespace != "CarSales") return 0;
                var so = new SerializedObject(mb);
                int n = 0;
                foreach (var pair in ActionFieldMap)
                {
                    var p = so.FindProperty(pair.Key);
                    if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var current = p.objectReferenceValue as InputActionReference;
                    if (current != null && current.action != null) continue;
                    if (!refs.TryGetValue(pair.Value, out var r)) continue;
                    p.objectReferenceValue = r;
                    n++;
                }
                if (n > 0) so.ApplyModifiedPropertiesWithoutUndo();
                return n;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                int before = fixedCount;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true)) fixedCount += Relink(mb);
                if (fixedCount > before) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var contents = PrefabUtility.LoadPrefabContents(path);
                int n = 0;
                foreach (var mb in contents.GetComponentsInChildren<MonoBehaviour>(true)) n += Relink(mb);
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                fixedCount += n;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Car Sales", $"Re-linked {fixedCount} input action field(s).", "OK");
        }

        [MenuItem("Tools/Car Sales/Delete All Saves", priority = 20)]
        static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("Car Sales", "Delete ALL save slots (and their backups)?", "Delete", "Cancel")) return;
            int deleted = 0;
            var store = new CarSales.Foundation.SaveStore(SaveManager.SavesRoot);
            for (int s = 1; s <= CarSales.Foundation.SaveStore.SlotCount; s++)
                if (store.Exists(s)) { store.Delete(s); deleted++; }
            if (File.Exists(SaveManager.LegacyFilePath)) { File.Delete(SaveManager.LegacyFilePath); deleted++; }
            Debug.Log(deleted > 0 ? $"[CarSales] Deleted {deleted} save(s) in {Application.persistentDataPath}" : "[CarSales] No saves to delete.");
        }

        [MenuItem("Tools/Car Sales/Show Save Folder", priority = 21)]
        static void ShowSaveFolder()
        {
            Directory.CreateDirectory(SaveManager.SavesRoot);
            EditorUtility.RevealInFinder(SaveManager.SavesRoot);
        }

        // ------------------------------------------------------------------ CHECKS
        static bool CheckTextMeshPro()
        {
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null && TMP_Settings.defaultFontAsset != null) return true;
            EditorUtility.DisplayDialog("Car Sales Simulator",
                "TextMeshPro Essential Resources are not imported yet.\n\n" +
                "The TMP importer will open now: click 'Import TMP Essentials', wait for it to finish, " +
                "then run Tools > Car Sales > 2. Build Everything again.", "OK");
            if (!EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources"))
                Debug.LogWarning("[CarSales] Open Window > TextMeshPro > Import TMP Essential Resources manually.");
            return false;
        }

        static bool SetupLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            var wanted = new Dictionary<int, string> { { InteractLayer, "Interactable" }, { StudioLayer, "CarSalesStudio" }, { MinimapLayer, "CarSalesMinimap" } };
            foreach (var pair in wanted)
            {
                var layer = layers.GetArrayElementAtIndex(pair.Key);
                if (layer.stringValue == pair.Value) continue;
                if (!string.IsNullOrEmpty(layer.stringValue) &&
                    !EditorUtility.DisplayDialog($"Layer {pair.Key} is in use",
                        $"User Layer {pair.Key} is named '{layer.stringValue}'. Car Sales needs Layer {pair.Key} = '{pair.Value}'.\nRename it?",
                        "Rename", "Cancel")) return false;
                layer.stringValue = pair.Value;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        class ActionRefs { public InputActionReference move, look, jump, sprint, interact, toggleShop, closeMenu, inventory, vehicle, map, phone, cameraView, gearUp, gearDown; }

        static ActionRefs LoadActions()
        {
            AssetDatabase.ImportAsset(InputPath, ImportAssetOptions.ForceUpdate);
            var all = AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>().ToArray();
            InputActionReference Find(string n) => all.FirstOrDefault(r => r.action != null && r.action.name == n);
            var refs = new ActionRefs
            {
                move = Find("Move"), look = Find("Look"), jump = Find("Jump"), sprint = Find("Sprint"),
                interact = Find("Interact"), toggleShop = Find("ToggleShop"), closeMenu = Find("CloseMenu"), inventory = Find("Inventory"),
                vehicle = Find("Vehicle"), map = Find("Map"), phone = Find("Phone"), cameraView = Find("CameraView"),
                gearUp = Find("GearUp"), gearDown = Find("GearDown")
            };
            if (refs.move && refs.look && refs.interact) return refs;   // the rest fall back to defaults at runtime
            EditorUtility.DisplayDialog("Car Sales Simulator",
                "Could not load the input actions from\n" + InputPath +
                "\n\nMake sure the Input System package is installed and the file imported without errors.", "OK");
            return null;
        }

        // ------------------------------------------------------------------ MATERIALS
        class Mats
        {
            public Material paint, glass, tire, headlight, taillight, trim;
            public Material ground, asphalt, line, yellowLine, sidewalk, floor, wall, roof, frame, windowGlass;
            public Material accentPanel, signWhite, desk, screen, customer, signBoard, metal;
            public Material trunk, leaves, lampGlow, ceilingLight, sofa, pot, bin, cone, flagRed, flagBlue, flagWhite;
            public Material concrete, liftYellow, toolRed, brushBlue, barrel, platform, ringGlow;
            public Material[] buildings; public Material windowsGlow;
        }

        static bool loadMaterialsOnly;

        /// <summary>The town's materials as already saved by Build Everything (made if missing). Used by the World Builder.</summary>
        static Mats LoadMats()
        {
            loadMaterialsOnly = true;
            try { return CreateMaterials(); }
            finally { loadMaterialsOnly = false; }
        }

        static Material M(string name, Color c, float smooth = 0.3f, bool emissive = false, float emission = 2f, float metallic = 0f)
        {
            if (loadMaterialsOnly)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Material>($"{Gen}/Materials/{name}.mat");
                if (existing) return existing;
            }
            var m = new Material(litShader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(m, $"{Gen}/Materials/{name}.mat");
            return m;
        }

        static Material Transparent(Material m, float alpha)
        {
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = alpha;
            if (m.HasProperty("_Surface"))   // URP / HDRP Lit
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (m.HasProperty("_Mode"))  // Built-in Standard
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mats CreateMaterials()
        {
            litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            colorProperty = litShader.name == "Standard" ? "_Color" : "_BaseColor";

            return new Mats
            {
                paint = M("M_CarPaint", Color.white, 0.88f, metallic: 0.3f),
                glass = M("M_CarGlass", new Color(0.06f, 0.08f, 0.11f), 0.95f),
                tire = M("M_Tire", new Color(0.05f, 0.05f, 0.05f), 0.15f),
                headlight = M("M_Headlight", new Color(1f, 0.96f, 0.8f), 0.9f, true, 2.5f),
                taillight = M("M_Taillight", new Color(0.85f, 0.05f, 0.05f), 0.9f, true, 2f),
                trim = M("M_CarTrim", new Color(0.09f, 0.09f, 0.1f), 0.5f),
                ground = M("M_Grass", new Color(0.28f, 0.38f, 0.22f), 0.05f),
                asphalt = M("M_Asphalt", new Color(0.16f, 0.16f, 0.17f), 0.35f),
                line = M("M_LotLine", new Color(0.95f, 0.95f, 0.92f), 0.3f),
                yellowLine = M("M_YellowLine", new Color(0.95f, 0.75f, 0.1f), 0.3f),
                sidewalk = M("M_Sidewalk", new Color(0.62f, 0.62f, 0.6f), 0.15f),
                floor = M("M_ShowroomFloor", new Color(0.78f, 0.79f, 0.8f), 0.9f),
                wall = M("M_Wall", new Color(0.92f, 0.92f, 0.94f), 0.2f),
                roof = M("M_Roof", new Color(0.2f, 0.21f, 0.24f), 0.2f),
                frame = M("M_WindowFrame", new Color(0.12f, 0.13f, 0.15f), 0.6f, metallic: 0.6f),
                windowGlass = Transparent(M("M_ShopGlass", new Color(0.55f, 0.75f, 0.9f), 0.95f), 0.25f),
                accentPanel = M("M_SignPanel", new Color(0.05f, 0.12f, 0.3f), 0.6f),
                signWhite = M("M_SignWhite", Color.white, 0.5f, true, 1.2f),
                desk = M("M_Wood", new Color(0.42f, 0.27f, 0.15f), 0.45f),
                screen = M("M_Screen", new Color(0.12f, 0.35f, 0.75f), 0.9f, true, 1.5f),
                customer = M("M_Customer", new Color(0.7f, 0.7f, 0.7f), 0.2f),
                signBoard = M("M_SignBoard", new Color(0.08f, 0.08f, 0.1f), 0.5f),
                metal = M("M_Metal", new Color(0.35f, 0.36f, 0.38f), 0.7f, metallic: 0.8f),
                trunk = M("M_TreeTrunk", new Color(0.33f, 0.22f, 0.13f), 0.1f),
                leaves = M("M_Leaves", new Color(0.2f, 0.45f, 0.18f), 0.1f),
                lampGlow = M("M_LampGlow", new Color(1f, 0.9f, 0.7f), 0.5f, true, 3f),
                ceilingLight = M("M_CeilingLight", Color.white, 0.5f, true, 2.5f),
                sofa = M("M_Sofa", new Color(0.18f, 0.19f, 0.22f), 0.3f),
                pot = M("M_Pot", new Color(0.85f, 0.85f, 0.85f), 0.4f),
                bin = M("M_Bin", new Color(0.15f, 0.25f, 0.2f), 0.4f),
                cone = M("M_Cone", new Color(1f, 0.42f, 0.05f), 0.4f),
                flagRed = M("M_FlagRed", new Color(0.8f, 0.1f, 0.12f), 0.3f),
                flagBlue = M("M_FlagBlue", new Color(0.1f, 0.25f, 0.7f), 0.3f),
                flagWhite = M("M_FlagWhite", new Color(0.92f, 0.92f, 0.95f), 0.3f),
                concrete = M("M_Concrete", new Color(0.5f, 0.5f, 0.52f), 0.25f),
                liftYellow = M("M_LiftYellow", new Color(0.95f, 0.75f, 0.1f), 0.5f),
                toolRed = M("M_ToolRed", new Color(0.75f, 0.1f, 0.1f), 0.6f),
                brushBlue = M("M_WashBrush", new Color(0.15f, 0.35f, 0.9f), 0.2f),
                barrel = M("M_Barrel", new Color(0.1f, 0.35f, 0.6f), 0.5f),
                platform = M("M_DisplayPlatform", new Color(0.12f, 0.12f, 0.14f), 0.95f),
                ringGlow = M("M_RingGlow", new Color(0.2f, 0.55f, 1f), 0.5f, true, 3f),
                windowsGlow = M("M_BuildingWindows", new Color(0.55f, 0.7f, 0.85f), 0.9f, true, 0.8f),
                buildings = new[]
                {
                    M("M_BuildingA", new Color(0.55f, 0.58f, 0.62f), 0.3f),
                    M("M_BuildingB", new Color(0.42f, 0.45f, 0.52f), 0.3f),
                    M("M_BuildingC", new Color(0.65f, 0.6f, 0.55f), 0.3f),
                },
            };
        }

        // ------------------------------------------------------------------ CARS
        struct CarSpec
        {
            public string id, name, brand, fuel, gearbox, desc;
            public CarCategory cat;
            public int year, price;
            public float pop;
            public CarSpec(string id, string name, string brand, CarCategory cat, int year, int price, float pop, string fuel, string gearbox, string desc)
            { this.id = id; this.name = name; this.brand = brand; this.cat = cat; this.year = year; this.price = price; this.pop = pop; this.fuel = fuel; this.gearbox = gearbox; this.desc = desc; }
        }

        // Fictional brands - replace with your own models later.
        static readonly CarSpec[] Specs =
        {
            new CarSpec("hatch_velo", "Velo", "Kestrel", CarCategory.Hatchback, 2016, 12000, 1.0f, "Petrol", "Manual", "Nimble city hatchback. Cheap to run and easy to park."),
            new CarSpec("hatch_metro", "Metro S", "Kestrel", CarCategory.Hatchback, 2019, 15500, 1.1f, "Petrol", "Automatic", "Sporty hatch with a modern infotainment system."),
            new CarSpec("sedan_avenue", "Avenue", "Orion", CarCategory.Sedan, 2014, 18000, 1.0f, "Diesel", "Automatic", "Comfortable family sedan with a big boot."),
            new CarSpec("sedan_avenuegt", "Avenue GT", "Orion", CarCategory.Sedan, 2020, 26000, 1.1f, "Petrol", "Automatic", "The sporty version of the Avenue. Popular with young professionals."),
            new CarSpec("suv_ridge", "Ridge", "Titan", CarCategory.SUV, 2017, 32000, 1.2f, "Diesel", "Automatic", "Rugged 4x4 SUV. Families love it."),
            new CarSpec("sports_stallion", "Stallion R", "Vortex", CarCategory.Sports, 2018, 55000, 1.3f, "Petrol", "Manual", "Two-door sports coupe. Fast, loud and very desirable."),
            new CarSpec("pickup_hauler", "Hauler", "Titan", CarCategory.Pickup, 2015, 28000, 1.0f, "Diesel", "Manual", "Workhorse pickup truck. Tradespeople buy these all day."),
            new CarSpec("lux_regent", "Regent", "Aurelia", CarCategory.Luxury, 2019, 70000, 1.2f, "Hybrid", "Automatic", "Executive luxury saloon with every option ticked."),
        };

        static CarDatabase CreateCars(Mats m)
        {
            var db = ScriptableObject.CreateInstance<CarDatabase>();
            foreach (var s in Specs)
            {
                var model = CreateCarModel(s, m);
                var def = ScriptableObject.CreateInstance<CarDefinition>();
                def.carId = s.id;
                def.displayName = s.name;
                def.brand = s.brand;
                def.category = s.cat;
                def.year = s.year;
                def.basePrice = s.price;
                def.popularity = s.pop;
                def.fuelType = s.fuel;
                def.transmission = s.gearbox;
                def.description = s.desc;
                def.displayPrefab = model;
                // Tuned handling (Vehicle Studio) lives outside Generated, so it survives a rebuild.
                def.physicsOverride = AssetDatabase.LoadAssetAtPath<VehiclePhysicsProfileAsset>($"{VehicleStudioWindow.ProfileFolder}/Physics_{s.id}.asset");
                AssetDatabase.CreateAsset(def, $"{Gen}/Data/Cars/Car_{s.id}.asset");
                db.cars.Add(def);
            }
            // Cars imported with the Asset Studio live in Assets/CarSales/Data/Cars and are kept on every rebuild.
            if (AssetDatabase.IsValidFolder(Root + "/Data/Cars"))
                foreach (var g in AssetDatabase.FindAssets("t:CarDefinition", new[] { Root + "/Data/Cars" }))
                {
                    var imported = AssetDatabase.LoadAssetAtPath<CarDefinition>(AssetDatabase.GUIDToAssetPath(g));
                    if (imported && imported.displayPrefab && !db.cars.Contains(imported)) db.cars.Add(imported);
                }
            AssetDatabase.CreateAsset(db, $"{Gen}/Resources/{CarDatabase.ResourceName}.asset");   // in Resources: scenes can find it even without a reference
            return db;
        }

        /// <summary>Builds a simple low-poly car out of primitives. Front = +Z.</summary>
        static GameObject CreateCarModel(CarSpec s, Mats m)
        {
            Vector3 body, cabin;
            float cabinZ;
            switch (s.cat)
            {
                case CarCategory.Hatchback: body = new Vector3(1.75f, 0.62f, 3.9f); cabin = new Vector3(1.58f, 0.55f, 2.0f); cabinZ = -0.35f; break;
                case CarCategory.SUV:       body = new Vector3(1.95f, 0.85f, 4.7f); cabin = new Vector3(1.8f, 0.7f, 3.0f);  cabinZ = -0.45f; break;
                case CarCategory.Sports:    body = new Vector3(1.95f, 0.45f, 4.4f); cabin = new Vector3(1.5f, 0.38f, 1.9f); cabinZ = -0.3f; break;
                case CarCategory.Pickup:    body = new Vector3(1.98f, 0.8f, 5.4f);  cabin = new Vector3(1.85f, 0.72f, 1.9f); cabinZ = 0.75f; break;
                case CarCategory.Luxury:    body = new Vector3(1.92f, 0.62f, 5.1f); cabin = new Vector3(1.7f, 0.52f, 2.8f); cabinZ = -0.2f; break;
                default:                    body = new Vector3(1.82f, 0.6f, 4.6f);  cabin = new Vector3(1.6f, 0.5f, 2.3f);  cabinZ = -0.15f; break;
            }
            bool big = s.cat == CarCategory.SUV || s.cat == CarCategory.Pickup;
            float wheelR = big ? 0.42f : 0.35f;
            float bottom = wheelR * 0.85f;

            var root = new GameObject("Model_" + s.id);
            var t = root.transform;
            var bodyGo = Prim(PrimitiveType.Cube, "Body", t, new Vector3(0, bottom + body.y / 2f, 0), body, m.paint);
            Prim(PrimitiveType.Cube, "Cabin", t, new Vector3(0, bottom + body.y + cabin.y / 2f, cabinZ), cabin, m.glass);
            var roof = Prim(PrimitiveType.Cube, "Roof", t, new Vector3(0, bottom + body.y + cabin.y + 0.03f, cabinZ),
                            new Vector3(cabin.x + 0.04f, 0.06f, cabin.z * 0.92f), m.paint);
            Prim(PrimitiveType.Cube, "BumperFront", t, new Vector3(0, bottom + 0.12f, body.z / 2f + 0.06f), new Vector3(body.x * 0.98f, 0.22f, 0.14f), m.trim);
            Prim(PrimitiveType.Cube, "BumperRear", t, new Vector3(0, bottom + 0.12f, -body.z / 2f - 0.06f), new Vector3(body.x * 0.98f, 0.22f, 0.14f), m.trim);
            Prim(PrimitiveType.Cube, "Grille", t, new Vector3(0, bottom + body.y * 0.55f, body.z / 2f + 0.015f), new Vector3(body.x * 0.4f, body.y * 0.3f, 0.03f), m.trim);
            var mirrors = new List<Renderer>();
            foreach (float side in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Cube, "Sill", t, new Vector3(side * (body.x / 2f + 0.01f), bottom + 0.06f, 0f), new Vector3(0.04f, 0.12f, body.z * 0.6f), m.trim);
                var mirror = Prim(PrimitiveType.Cube, "Mirror", t, new Vector3(side * (cabin.x / 2f + 0.14f), bottom + body.y + 0.08f, cabinZ + cabin.z / 2f - 0.15f),
                                  new Vector3(0.18f, 0.1f, 0.12f), m.paint);
                mirrors.Add(mirror.GetComponent<Renderer>());
            }
            if (s.cat == CarCategory.Pickup)
                Prim(PrimitiveType.Cube, "BedFloor", t, new Vector3(0, bottom + body.y + 0.02f, -1.2f), new Vector3(body.x - 0.2f, 0.04f, 2.4f), m.trim);

            float wx = body.x / 2f - 0.05f, wz = body.z / 2f - 0.85f;
            var wheelPositions = new[] { new Vector3(wx, wheelR, wz), new Vector3(-wx, wheelR, wz), new Vector3(wx, wheelR, -wz), new Vector3(-wx, wheelR, -wz) };
            for (int i = 0; i < wheelPositions.Length; i++)
            {
                var w = Prim(PrimitiveType.Cylinder, "Wheel_" + i, t, wheelPositions[i], new Vector3(wheelR * 2f, 0.13f, wheelR * 2f), m.tire);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var hub = Prim(PrimitiveType.Cylinder, "Hub_" + i, t, wheelPositions[i] + new Vector3(Mathf.Sign(wheelPositions[i].x) * 0.07f, 0f, 0f),
                               new Vector3(wheelR * 1.1f, 0.02f, wheelR * 1.1f), m.metal);
                hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            float lightY = bottom + body.y * 0.6f;
            foreach (float side in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Cube, "Headlight", t, new Vector3(side * (body.x / 2f - 0.32f), lightY, body.z / 2f + 0.01f), new Vector3(0.42f, 0.13f, 0.04f), m.headlight);
                Prim(PrimitiveType.Cube, "Taillight", t, new Vector3(side * (body.x / 2f - 0.3f), lightY, -body.z / 2f - 0.01f), new Vector3(0.36f, 0.12f, 0.04f), m.taillight);
            }

            var visual = root.AddComponent<CarVisual>();
            var paintRenderers = new List<Object> { bodyGo.GetComponent<Renderer>(), roof.GetComponent<Renderer>() };
            paintRenderers.AddRange(mirrors);
            SetArray(visual, "paintRenderers", paintRenderers.ToArray());
            SetString(visual, "colorProperty", colorProperty);
            return SavePrefab(root, $"{Gen}/Prefabs/CarModels/Model_{s.id}.prefab");
        }

        // ------------------------------------------------------------------ PREFABS
        static CarDisplay CreateCarDisplayPrefab()
        {
            var root = new GameObject("CarDisplay") { layer = InteractLayer };
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(2.1f, 1.7f, 5.5f);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;

            var visualRoot = Child("VisualRoot", root.transform, Vector3.zero);
            var browse = Child("BrowsePoint", root.transform, new Vector3(0f, 0f, 3.6f));
            var tag = Text3D("PriceTag", root.transform, new Vector3(0f, 2.5f, 0f), "$0", 6f, new Color(1f, 0.85f, 0.2f));
            tag.gameObject.AddComponent<Billboard>();
            tag.gameObject.SetActive(false);

            var display = root.AddComponent<CarDisplay>();
            Set(display, "visualRoot", visualRoot);
            Set(display, "browsePoint", browse);
            Set(display, "priceTag", tag);
            return SavePrefab(root, $"{Gen}/Prefabs/CarDisplay.prefab").GetComponent<CarDisplay>();
        }

        static CustomerAI CreateCustomerPrefab(Mats m)
        {
            var root = new GameObject("Customer") { layer = InteractLayer };
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = 1.6f;
            agent.angularSpeed = 360f;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0.4f;

            var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.85f, 0.6f), m.customer, keepCollider: true);
            body.layer = InteractLayer;
            Prim(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.9f, 0f), new Vector3(0.36f, 0.4f, 0.36f), m.pot);
            Prim(PrimitiveType.Cube, "Eyes", root.transform, new Vector3(0f, 1.93f, 0.16f), new Vector3(0.2f, 0.06f, 0.06f), m.signBoard);

            var bubble = Text3D("Bubble", root.transform, new Vector3(0f, 2.5f, 0f), "", 8f, Color.white);
            bubble.gameObject.AddComponent<Billboard>();
            bubble.gameObject.SetActive(false);

            var ai = root.AddComponent<CustomerAI>();
            Set(ai, "bubble", bubble);
            Set(ai, "bodyRenderer", body.GetComponent<Renderer>());
            return SavePrefab(root, $"{Gen}/Prefabs/Customer.prefab").GetComponent<CustomerAI>();
        }

        // ------------------------------------------------------------------ GAME SCENE
        static void BuildGameScene(Mats m, CarDatabase db, CarDisplay carDisplayPrefab, CustomerAI customerPrefab, ActionRefs a)
        {
            var scene = SceneManager.GetActiveScene();
            RenderSettings.sun = CreateSun(new Vector3(50f, -30f, 0f), 1.3f);

            var env = new GameObject("Town");
            var town = BuildTown(env.transform, m, db, carDisplayPrefab, customerPrefab);
            BuildLayoutSites(env.transform, m, carDisplayPrefab, customerPrefab, town.surfaces);   // phase 8: your World Builder sites

            Progress("Systems", 0.8f);
            var systems = BuildSystems(db, a, carDisplayPrefab);
            BuildPlayer(a, town.playerStart);

            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            PrefabUtility.SaveAsPrefabAssetAndConnect(systems, $"{Gen}/Prefabs/CarSalesSystems.prefab", InteractionMode.AutomatedAction);
            int swapped = PlaceholderReplacer.ApplyToOpenScene(false);   // your imported buildings/props (Asset Studio > Town)
            if (swapped > 0) Debug.Log($"[CarSales] {swapped} placeholders replaced with your imported models.");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Progress("Baking NavMesh", 0.9f);
            int n = 0;
            foreach (var surface in town.surfaces)
            {
                try
                {
                    surface.BuildNavMesh();
                    if (surface.navMeshData != null)
                    {
                        AssetDatabase.CreateAsset(surface.navMeshData, $"{Gen}/Scenes/NavMesh_{n++}.asset");
                        EditorUtility.SetDirty(surface);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[CarSales] Editor NavMesh bake failed - it will be baked at runtime instead. " + e.Message);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != MenuScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            list.Insert(1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Light CreateSun(Vector3 euler, float intensity)
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(euler);
            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky) RenderSettings.skybox = sky;
            return light;
        }

        static ParkingSpot CreateSpot(Transform parent, int index, Vector3 pos, float yRot, Mats m, bool lines = true)
        {
            var go = new GameObject($"Spot_{index:00}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            var spot = go.AddComponent<ParkingSpot>();
            spot.index = index;
            if (lines)
            {
                Prim(PrimitiveType.Cube, "Line_L", go.transform, new Vector3(-2f, 0.005f, 0f), new Vector3(0.12f, 0.01f, 5.2f), m.line);
                Prim(PrimitiveType.Cube, "Line_R", go.transform, new Vector3(2f, 0.005f, 0f), new Vector3(0.12f, 0.01f, 5.2f), m.line);
                Prim(PrimitiveType.Cube, "Line_Back", go.transform, new Vector3(0f, 0.005f, -2.6f), new Vector3(4.12f, 0.01f, 0.12f), m.line);
            }
            return spot;
        }

        static void BuildPlayer(ActionRefs a, Transform start)
        {
            var player = new GameObject("Player");
            player.transform.position = start ? start.position + Vector3.up * 0.1f : new Vector3(0f, 0.1f, 14.5f);
            if (start) player.transform.rotation = start.rotation;
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var pivot = Child("CameraPivot", player.transform, new Vector3(0f, 1.65f, 0f));
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(pivot, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 70f;
            cam.cullingMask = ~((1 << StudioLayer) | (1 << MinimapLayer));
            camGo.AddComponent<AudioListener>();
            SetUpLooks(cam);

            var fpc = player.AddComponent<SimpleFirstPersonController>();
            Set(fpc, "cameraPivot", pivot);
            Set(fpc, "moveAction", a.move);
            Set(fpc, "lookAction", a.look);
            Set(fpc, "jumpAction", a.jump);
            Set(fpc, "sprintAction", a.sprint);

            var interactor = player.AddComponent<PlayerInteractor>();
            Set(interactor, "cam", cam);
            Set(interactor, "interactAction", a.interact);
            SetInt(interactor, "interactMask", 1 << InteractLayer);

            var blocker = player.AddComponent<MenuInputBlocker>();
            SetArray(blocker, "disableWhileMenuOpen", new Object[] { fpc });
        }

        static GameObject BuildSystems(CarDatabase db, ActionRefs a, CarDisplay carDisplayPrefab)
        {
            var systems = new GameObject("CarSalesSystems");
            Set(systems.AddComponent<VehicleRegistry>(), "carDisplayPrefab", carDisplayPrefab);
            systems.AddComponent<PropertyManager>();
            systems.AddComponent<TowService>();
            systems.AddComponent<AuctionManager>();
            systems.AddComponent<WorkshopManager>();
            systems.AddComponent<MarketManager>();
            systems.AddComponent<StaffManager>();
            systems.AddComponent<CrmManager>();
            systems.AddComponent<SourcingManager>();
            systems.AddComponent<WorldManager>();
            systems.AddComponent<BusinessManager>();
            systems.AddComponent<AmbienceManager>();
            Set(systems.AddComponent<CarSalesBootstrap>(), "database", db);
            systems.AddComponent<EconomyManager>();
            systems.AddComponent<InventoryManager>();
            systems.AddComponent<MarketplaceManager>();
            Set(systems.AddComponent<DayCycleManager>(), "toggleShopAction", a.toggleShop);
            systems.AddComponent<SaveManager>();
            systems.AddComponent<UpgradeManager>();
            systems.AddComponent<BankManager>();
            systems.AddComponent<ProgressManager>();
            systems.AddComponent<MessageManager>();
            systems.AddComponent<AudioManager>();
            systems.AddComponent<DayNightLighting>();
            systems.AddComponent<ThumbnailService>();
            var drive = systems.AddComponent<TestDriveManager>();
            Set(drive, "moveAction", a.move);
            Set(drive, "brakeAction", a.jump);
            Set(drive, "exitAction", a.interact);
            Set(drive, "vehicleAction", a.vehicle);
            Set(drive, "cameraAction", a.cameraView);
            Set(drive, "gearUpAction", a.gearUp);
            Set(drive, "gearDownAction", a.gearDown);
            systems.AddComponent<DebugCheats>();

            // The UI builds itself at runtime (UIManager.Awake).
            var canvasGo = new GameObject("CarSalesUI", typeof(RectTransform)) { layer = 5 };
            canvasGo.transform.SetParent(systems.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var ui = canvasGo.AddComponent<UIManager>();
            Set(ui, "closeAction", a.closeMenu);
            Set(ui, "inventoryAction", a.inventory);
            Set(ui, "mapAction", a.map);
            Set(ui, "phoneAction", a.phone);
            return systems;
        }

        // ------------------------------------------------------------------ MAIN MENU SCENE
        static void BuildMainMenuScene(Mats m, CarDatabase db)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.sun = CreateSun(new Vector3(35f, -40f, 0f), 1.2f);

            Prim(PrimitiveType.Plane, "Ground", null, Vector3.zero, new Vector3(8f, 1f, 8f), m.floor);
            Prim(PrimitiveType.Cylinder, "Turntable", null, new Vector3(1.8f, 0.05f, 0f), new Vector3(7f, 0.05f, 7f), m.platform);
            Prim(PrimitiveType.Cylinder, "TurntableRing", null, new Vector3(1.8f, 0.03f, 0f), new Vector3(7.3f, 0.03f, 7.3f), m.ringGlow);
            Prim(PrimitiveType.Cube, "BackWall", null, new Vector3(0f, 4f, 9f), new Vector3(40f, 8f, 0.3f), m.accentPanel);
            var brand = Text3D("Brand", null, new Vector3(4f, 5.2f, 8.8f), "AUTO WORLD", 26f, Color.white);
            brand.rectTransform.sizeDelta = new Vector2(20f, 4f);
            for (int i = 0; i < 4; i++)
            {
                var lightGo = new GameObject("Spot " + i);
                lightGo.transform.position = new Vector3(1.8f + Mathf.Cos(i * 1.57f) * 4f, 5f, Mathf.Sin(i * 1.57f) * 4f);
                lightGo.transform.LookAt(new Vector3(1.8f, 0.5f, 0f));
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 12f;
                l.spotAngle = 55f;
                l.intensity = 3f;
            }

            var showcase = new GameObject("Showcase");
            showcase.transform.position = new Vector3(1.8f, 0.1f, 0f);
            var sports = db.cars.FirstOrDefault(c => c.category == CarCategory.Sports) ?? db.cars[0];
            var model = (GameObject)PrefabUtility.InstantiatePrefab(sports.displayPrefab);
            model.transform.SetParent(showcase.transform, false);
            showcase.AddComponent<MenuShowcase>();

            // A few more cars on the stage for the cinematic shots.
            var others = db.cars.Where(c => c && c != sports && c.displayPrefab).Take(2).ToList();
            var slots = new[] { (new Vector3(-6.5f, 0.02f, 4.5f), 35f), (new Vector3(10f, 0.02f, 5f), -40f) };
            for (int i = 0; i < others.Count; i++)
            {
                var extra = (GameObject)PrefabUtility.InstantiatePrefab(others[i].displayPrefab);
                extra.name = "StageCar_" + others[i].displayName;
                extra.transform.SetPositionAndRotation(slots[i].Item1, Quaternion.Euler(0f, slots[i].Item2, 0f));
            }
            AddReflectionProbe(null, "StageReflections", new Vector3(1.8f, 3f, 0f), new Vector3(30f, 8f, 20f));

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(-0.6f, 1.9f, -8.5f);
            camGo.transform.LookAt(new Vector3(0.4f, 0.7f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.cullingMask = ~((1 << StudioLayer) | (1 << MinimapLayer));
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCinematic>().target = showcase.transform;
            SetUpLooks(cam);

            new GameObject("Audio").AddComponent<AudioManager>();
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("MenuUI", typeof(RectTransform)) { layer = 5 };
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var menu = canvasGo.AddComponent<MainMenuUI>();
            SetString(menu, "gameScene", Path.GetFileNameWithoutExtension(ScenePath));

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        // ------------------------------------------------------------------ HELPERS
        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale,
                               Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Transform Child(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        static TextMeshPro Text3D(string name, Transform parent, Vector3 localPos, string text, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = TMP_Settings.defaultFontAsset;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
            return t;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static SerializedProperty Prop(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null) throw new System.Exception($"Field '{name}' not found on {so.targetObject.GetType().Name}");
            return p;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            Prop(so, field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            Prop(so, field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            Prop(so, field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = Prop(so, field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Progress(string step, float t) => EditorUtility.DisplayProgressBar("Car Sales Simulator", step, t);
    }
}
