using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Renders "photos" of cars for the UI. A small hidden photo studio (far below the map, on its own layer)
    /// renders each car once into a RenderTexture; the result is cached and re-rendered when paint/dirt changes.
    /// </summary>
    public class ThumbnailService : MonoBehaviour
    {
        public const int StudioLayer = 21;
        public static ThumbnailService Instance { get; private set; }

        [SerializeField] Vector2Int size = new Vector2Int(480, 300);
        [SerializeField] int maxCached = 48;
        [SerializeField] Color background = new Color(0.1f, 0.15f, 0.27f);
        [SerializeField] Vector3 studioPosition = new Vector3(0f, -500f, 0f);

        class Entry { public RenderTexture rt; public string key; public int lastUse; }
        class Job { public CarInstance car; public Entry entry; public float yaw; }

        readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        readonly Queue<Job> queue = new Queue<Job>();
        Camera cam;
        Transform anchor;
        int useCounter;

        void Awake()
        {
            Instance = this;
            BuildStudio();
            StartCoroutine(Process());
        }

        void Start()
        {
            // Make sure normal cameras never see the studio.
            foreach (var c in Camera.allCameras) if (c != cam) c.cullingMask &= ~(1 << StudioLayer);
        }

        void BuildStudio()
        {
            var root = new GameObject("ThumbnailStudio").transform;
            root.SetParent(transform, false);
            root.position = studioPosition;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(10f, 0.02f, 10f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            var floorColor = new Color(0.14f, 0.18f, 0.27f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", floorColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", floorColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.8f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            floor.layer = StudioLayer;

            anchor = new GameObject("CarAnchor").transform;
            anchor.SetParent(root, false);

            var camGo = new GameObject("ThumbnailCamera");
            camGo.transform.SetParent(root, false);
            camGo.transform.localPosition = new Vector3(5.6f, 2.1f, 6.9f);
            camGo.transform.LookAt(root.position + new Vector3(0f, 0.55f, 0f));
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << StudioLayer;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;

            AddLight(root, new Vector3(4f, 5f, 5f), 3.5f, Color.white);
            AddLight(root, new Vector3(-5f, 3f, 2f), 2f, new Color(0.7f, 0.8f, 1f));
            AddLight(root, new Vector3(0f, 4f, -5f), 2f, new Color(1f, 0.9f, 0.8f));
        }

        static void AddLight(Transform root, Vector3 local, float intensity, Color color)
        {
            var go = new GameObject("StudioLight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 18f;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << StudioLayer;
        }

        /// <summary>Returns a texture for the car (it fills in a frame later if it isn't rendered yet).</summary>
        public Texture Get(CarInstance car) => Get(car, 0);

        /// <summary>Listing photo from another angle (0 = front three-quarter, 1 = side, 2 = rear, 3 = other side...).</summary>
        public Texture Get(CarInstance car, int angle)
        {
            if (car == null || string.IsNullOrEmpty(car.instanceId)) return null;
            string key = ColorUtility.ToHtmlStringRGB(car.paintColor) + "|" + Mathf.RoundToInt(car.cleanliness / 10f);
            string id = angle == 0 ? car.instanceId : car.instanceId + "#" + angle;
            if (!cache.TryGetValue(id, out var e))
            {
                e = new Entry { rt = new RenderTexture(size.x, size.y, 16) { name = "CarPhoto_" + car.carId } };
                e.rt.Create();
                cache[id] = e;
                Trim();
            }
            e.lastUse = ++useCounter;
            if (e.key != key)
            {
                e.key = key;
                queue.Enqueue(new Job { car = car, entry = e, yaw = AngleYaw(angle) });
            }
            return e.rt;
        }

        public static float AngleYaw(int angle) { float[] yaws = { 0f, 55f, 160f, -60f, 110f, -140f }; return yaws[Mathf.Abs(angle) % yaws.Length]; }

        void Trim()
        {
            while (cache.Count > maxCached)
            {
                string oldest = null; int min = int.MaxValue;
                foreach (var pair in cache) if (pair.Value.lastUse < min) { min = pair.Value.lastUse; oldest = pair.Key; }
                if (oldest == null) return;
                var e = cache[oldest];
                cache.Remove(oldest);
                if (e.rt) { e.rt.Release(); Destroy(e.rt); }
            }
        }

        IEnumerator Process()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                if (queue.Count == 0) { yield return null; continue; }
                var job = queue.Dequeue();
                var db = CarSalesServices.Database;
                var def = db ? db.Get(job.car.carId) : null;
                if (job.entry.rt == null || def == null || !def.displayPrefab) continue;

                var model = Instantiate(def.displayPrefab, anchor);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, job.yaw, 0f);
                UIFactory.SetLayer(model, StudioLayer);
                var visual = model.GetComponent<CarVisual>();
                if (visual) visual.Apply(job.car.paintColor, 1f - job.car.cleanliness / 100f);

                cam.targetTexture = job.entry.rt;
                cam.enabled = true;
                yield return endOfFrame;          // rendered this frame
                cam.enabled = false;
                cam.targetTexture = null;
                Destroy(model);
            }
        }

        void OnDestroy()
        {
            foreach (var e in cache.Values) if (e.rt) { e.rt.Release(); Destroy(e.rt); }
            cache.Clear();
            if (Instance == this) Instance = null;
        }
    }
}
