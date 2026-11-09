using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Generates the test track scene: a 940 m straight with distance boards and a braking zone, a slalom lane,
    /// a bumpy lane, a skid-pad circle with a ramp on the turnaround pad, and knock-over cones.
    /// </summary>
    public static partial class CarSalesSetupWizard
    {
        public const string TestTrackScenePath = Gen + "/Scenes/TestTrack.unity";

        [MenuItem("Tools/Car Sales/5. Open Test Track", priority = 4)]
        public static void OpenTestTrack()
        {
            if (!File.Exists(TestTrackScenePath)) { EditorUtility.DisplayDialog("Car Sales", "Run 'Build Everything' first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(TestTrackScenePath);
        }

        static void BuildTestTrackScene(Mats m, CarDatabase db, ActionRefs a)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.sun = CreateSun(new Vector3(48f, -35f, 0f), 1.3f);
            var root = new GameObject("TestTrack").transform;

            // Ground: grass everywhere, asphalt on top (1 cm higher).
            Prim(PrimitiveType.Cube, "Grass", root, new Vector3(0f, -0.51f, 200f), new Vector3(800f, 1f, 1500f), m.ground, true);
            // Overlapping pieces sit a few millimetres apart so their surfaces don't flicker (z-fighting).
            void Asphalt(string name, Vector3 center, Vector2 size, float top = 0f) =>
                Prim(PrimitiveType.Cube, name, root, new Vector3(center.x, top - 0.1f, center.z), new Vector3(size.x, 0.2f, size.y), m.asphalt, true);

            // Main straight (x = 0), z -320 .. 620.
            Asphalt("Straight", new Vector3(0f, 0f, 150f), new Vector2(24f, 940f));
            for (float z = -316f; z < 616f; z += 12f)
                Prim(PrimitiveType.Cube, "CentreDash", root, new Vector3(0f, 0.004f, z), new Vector3(0.18f, 0.01f, 5f), m.line);
            foreach (float x in new[] { -11.4f, 11.4f })
                Prim(PrimitiveType.Cube, "EdgeLine", root, new Vector3(x, 0.004f, 150f), new Vector3(0.18f, 0.01f, 940f), m.yellowLine);
            Prim(PrimitiveType.Cube, "StartLine", root, new Vector3(0f, 0.005f, -300f), new Vector3(23f, 0.01f, 0.6f), m.line);
            Board(root, m, new Vector3(-15f, 0f, -300f), "START");
            for (int d = 100; d <= 900; d += 100) Board(root, m, new Vector3(15f, 0f, -300f + d), $"{d} m");

            // Braking zone: from 100 km/h you should stop between the cones (z 300..340).
            Board(root, m, new Vector3(-15f, 0f, 290f), "BRAKE ZONE");
            for (float z = 300f; z <= 360f; z += 10f)
                foreach (float x in new[] { -11f, 11f }) Cone(root, m, new Vector3(x, 0f, z));
            for (float z = 300f; z <= 360f; z += 10f)
                Prim(PrimitiveType.Cube, "BrakeMark", root, new Vector3(0f, 0.005f, z), new Vector3(20f, 0.01f, 0.25f), z % 20f == 0f ? m.line : m.yellowLine);

            // Turnaround pad + skid pad circle (radius 35) + ramp.
            Asphalt("TurnaroundPad", new Vector3(0f, 0f, 720f), new Vector2(220f, 200f), -0.003f);
            for (int i = 0; i < 28; i++)
            {
                float ang = i / 28f * Mathf.PI * 2f;
                Cone(root, m, new Vector3(Mathf.Cos(ang) * 35f, 0f, 720f + Mathf.Sin(ang) * 35f));
            }
            Board(root, m, new Vector3(0f, 0f, 680f), "SKID PAD");
            var ramp = Prim(PrimitiveType.Cube, "Ramp", root, new Vector3(75f, 0.62f, 700f), new Vector3(6f, 0.4f, 12f), m.concrete, true);
            ramp.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            Board(root, m, new Vector3(75f, 0f, 688f), "RAMP");

            // Side lanes joined to the straight by pads at both ends.
            Asphalt("StartPad", new Vector3(0f, 0f, -335f), new Vector2(150f, 40f), -0.003f);
            Asphalt("NorthPad", new Vector3(0f, 0f, 330f), new Vector2(150f, 26f), -0.003f);
            Asphalt("SlalomLane", new Vector3(60f, 0f, 0f), new Vector2(16f, 640f), -0.006f);
            Board(root, m, new Vector3(60f, 0f, -300f), "SLALOM");
            int n = 0;
            for (float z = -260f; z <= 300f; z += 18f, n++) Cone(root, m, new Vector3(60f + (n % 2 == 0 ? -2f : 2f), 0f, z));
            Asphalt("BumpLane", new Vector3(-60f, 0f, 0f), new Vector2(16f, 640f), -0.006f);
            Board(root, m, new Vector3(-60f, 0f, -300f), "BUMPS");
            for (float z = -250f; z <= 300f; z += 22f)
            {
                var bump = Prim(PrimitiveType.Cylinder, "Bump", root, new Vector3(-60f, -0.12f, z), new Vector3(0.5f, 7.5f, 0.5f), m.concrete, true);
                bump.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // Spawn, camera, controller.
            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(root, false);
            spawn.localPosition = new Vector3(0f, 0f, -312f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 3f, -322f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.farClipPlane = 1500f;
            cam.cullingMask = ~((1 << StudioLayer) | (1 << MinimapLayer));
            camGo.AddComponent<AudioListener>();
            SetUpLooks(cam);

            var controllerGo = new GameObject("TestTrackController");
            var controller = controllerGo.AddComponent<TestTrackController>();
            Set(controller, "database", db);
            Set(controller, "spawnPoint", spawn);
            if (a != null)
            {
                Set(controller, "moveAction", a.move);
                Set(controller, "brakeAction", a.jump);
                Set(controller, "cameraAction", a.cameraView);
                Set(controller, "gearUpAction", a.gearUp);
                Set(controller, "gearDownAction", a.gearDown);
            }

            EditorSceneManager.SaveScene(scene, TestTrackScenePath);
            VerifySavedLinks(TestTrackScenePath, db, a);
            var list = EditorBuildSettings.scenes.Where(s => s.path != TestTrackScenePath).ToList();
            list.Add(new EditorBuildSettingsScene(TestTrackScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>
        /// Re-opens the saved scene and checks the controller's references survived saving; repairs them if not.
        /// (A user reported them arriving empty; the runtime also falls back to Resources and default keys.)
        /// </summary>
        static void VerifySavedLinks(string path, CarDatabase db, ActionRefs a)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<TestTrackController>();
            if (!controller) { Debug.LogError("[CarSales] Test track: controller missing after saving the scene."); return; }
            var so = new SerializedObject(controller);
            var missing = new System.Collections.Generic.List<string>();
            void Check(string field, Object value)
            {
                var p = so.FindProperty(field);
                if (p == null || p.objectReferenceValue != null || value == null) return;
                missing.Add(field);
                p.objectReferenceValue = value;
            }
            Check("database", db);
            if (a != null)
            {
                Check("moveAction", a.move); Check("brakeAction", a.jump); Check("cameraAction", a.cameraView);
                Check("gearUpAction", a.gearUp); Check("gearDownAction", a.gearDown);
            }
            if (missing.Count == 0) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.LogWarning("[CarSales] Test track: these links were lost when saving and have been re-applied: " + string.Join(", ", missing));
        }

        static void Board(Transform parent, Mats m, Vector3 pos, string text)
        {
            var go = new GameObject("Board_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            Prim(PrimitiveType.Cube, "Post", go.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.15f, 2.4f, 0.15f), m.metal);
            Prim(PrimitiveType.Cube, "Panel", go.transform, new Vector3(0f, 2.6f, 0.05f), new Vector3(3.6f, 1.1f, 0.08f), m.signBoard);
            var t = Text3D("Text", go.transform, new Vector3(0f, 2.6f, -0.02f), text, 7f, Color.white);
            t.rectTransform.sizeDelta = new Vector2(3.4f, 1f);
        }

        static void Cone(Transform parent, Mats m, Vector3 pos)
        {
            var cone = Prim(PrimitiveType.Cylinder, "Cone", parent, pos + new Vector3(0f, 0.3f, 0f), new Vector3(0.32f, 0.3f, 0.32f), m.cone, true);
            var rb = cone.AddComponent<Rigidbody>();
            rb.mass = 3f;
            cone.layer = 0;
        }
    }
}
