using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarSales.Sim;
using UnityEditor;
using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Tools > Car Sales > Vehicle Studio: every car model with its handling class, powertrain and simulated
    /// performance (0-100 km/h, top speed, braking). "Make editable" creates a physics profile asset pre-filled
    /// from the preset and assigns it to the car, so you can tune it in the Inspector.
    /// </summary>
    public class VehicleStudioWindow : EditorWindow
    {
        /// <summary>Outside Generated/, so Build Everything keeps tuned profiles (and re-links them by car id).</summary>
        public const string ProfileFolder = "Assets/CarSales/Data/Physics";

        Vector2 scroll;
        List<CarDefinition> cars = new List<CarDefinition>();
        readonly Dictionary<CarDefinition, PerformanceResult> results = new Dictionary<CarDefinition, PerformanceResult>();
        GUIStyle bold;

        [MenuItem("Tools/Car Sales/Vehicle Studio", priority = 30)]
        static void Open() => GetWindow<VehicleStudioWindow>("Vehicle Studio").Refresh();

        void OnEnable() => Refresh();
        void OnProjectChange() => Refresh();

        void Refresh()
        {
            cars = AssetDatabase.FindAssets("t:CarDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<CarDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c).OrderBy(c => c.category).ThenBy(c => c.basePrice).ToList();
            results.Clear();
            foreach (var c in cars) results[c] = PerformanceEstimator.Run(VehicleProfileResolver.For(c));
            Repaint();
        }

        void OnGUI()
        {
            if (bold == null) bold = new GUIStyle(EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Simulated straight-line performance of each car with its physics profile. " +
                                    "Real driving also depends on grip, weight transfer and the car's condition. " +
                                    "Press F3 while driving in Play mode for live telemetry.", UnityEditor.MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Recalculate", GUILayout.Width(120))) Refresh();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{cars.Count} car models");
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Car", bold, GUILayout.Width(170));
                GUILayout.Label("Class / powertrain", bold, GUILayout.Width(200));
                GUILayout.Label("Mass", bold, GUILayout.Width(60));
                GUILayout.Label("0-100", bold, GUILayout.Width(60));
                GUILayout.Label("Top", bold, GUILayout.Width(70));
                GUILayout.Label("100-0", bold, GUILayout.Width(60));
                GUILayout.Label("Profile", bold);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var c in cars)
            {
                var p = VehicleProfileResolver.For(c);
                results.TryGetValue(c, out var r);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(c.displayName, EditorStyles.linkLabel, GUILayout.Width(170))) Selection.activeObject = c;
                    GUILayout.Label($"{p.vehicleClass} / {p.powertrain} / {p.drivetrain}", GUILayout.Width(200));
                    GUILayout.Label($"{p.chassis.massKg:0} kg", GUILayout.Width(60));
                    GUILayout.Label(r.ZeroTo100Seconds > 0f ? $"{r.ZeroTo100Seconds:0.0} s" : "-", GUILayout.Width(60));
                    GUILayout.Label($"{r.TopSpeedKmh:0} km/h", GUILayout.Width(70));
                    GUILayout.Label($"{r.StoppingDistance100:0} m", GUILayout.Width(60));
                    if (c.physicsOverride)
                    {
                        if (GUILayout.Button("Edit", GUILayout.Width(60))) Selection.activeObject = c.physicsOverride;
                        if (GUILayout.Button("Use preset", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(c, "Use preset physics");
                            c.physicsOverride = null;
                            EditorUtility.SetDirty(c);
                            Refresh();
                        }
                    }
                    else if (GUILayout.Button("Make editable", GUILayout.Width(110))) MakeEditable(c);
                    if (GUILayout.Button("Drive", GUILayout.Width(60))) DriveOnTestTrack(c);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        public static void DriveOnTestTrack(CarDefinition car)
        {
            if (!File.Exists(CarSalesSetupWizard.TestTrackScenePath))
            {
                EditorUtility.DisplayDialog("Car Sales", "Run Tools > Car Sales > 2. Build Everything first (it creates the test track).", "OK");
                return;
            }
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PlayerPrefs.SetString(TestTrackController.CarPrefKey, car.carId);
            PlayerPrefs.Save();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(CarSalesSetupWizard.TestTrackScenePath);
            EditorApplication.isPlaying = true;
        }

        void MakeEditable(CarDefinition car)
        {
            Directory.CreateDirectory(ProfileFolder);
            var asset = CreateInstance<VehiclePhysicsProfileAsset>();
            asset.profile = VehicleProfileResolver.For(car);
            asset.profile.name = car.displayName;
            string path = $"{ProfileFolder}/Physics_{car.carId}.asset";
            if (File.Exists(path)) { car.physicsOverride = AssetDatabase.LoadAssetAtPath<VehiclePhysicsProfileAsset>(path); EditorUtility.SetDirty(car); Refresh(); return; }
            AssetDatabase.CreateAsset(asset, path);
            Undo.RecordObject(car, "Make physics editable");
            car.physicsOverride = asset;
            EditorUtility.SetDirty(car);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Refresh();
        }
    }
}
