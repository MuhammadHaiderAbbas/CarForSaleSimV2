using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Traffic (spec §6.13): cars follow the lane graph (driving on the left), keep their distance, stop for red
    /// lights and for you, take a random turn at junctions, and appear/disappear out of sight. Density follows the
    /// time of day; never more than TrafficRules.MaxTrafficCars. Visual only: they don't use the car database's
    /// physics, so they're cheap.
    /// </summary>
    public class TrafficSystem : MonoBehaviour
    {
        public const float SpawnMin = 70f, SpawnMax = 220f, DespawnDistance = 260f;
        List<Lane> lanes;
        readonly List<TrafficCar> cars = new List<TrafficCar>();
        readonly Dictionary<Vector2Int, Transform> lightPoles = new Dictionary<Vector2Int, Transform>();
        float spawnTimer;

        public IReadOnlyList<Lane> Lanes => lanes;
        public IReadOnlyList<TrafficCar> Cars => cars;

        void Start()
        {
            var net = RoadNetwork.Instance ? RoadNetwork.Instance : FindFirstObjectByType<RoadNetwork>();
            var segs = net && net.segments.Count > 0 ? net.segments : RoadGraph.DefaultTown();
            lanes = RoadGraph.Build(segs, net ? net.roadWidth : 9f);
            BuildLights();
            if (!FindFirstObjectByType<PerformanceGovernor>()) gameObject.AddComponent<PerformanceGovernor>();
        }

        /// <summary>Simple lights where lanes end at a junction (a pole with three lamps).</summary>
        void BuildLights()
        {
            foreach (var l in lanes.Where(x => x.endsAtJunction))
            {
                var p = new Vector3(l.end.x, 0f, l.end.z);
                var key = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
                if (lightPoles.ContainsKey(key)) continue;
                var dir = new Vector3(l.Dir.x, 0f, l.Dir.z);
                var left = new Vector3(-dir.z, 0f, dir.x);
                var pole = new GameObject("TrafficLight").transform;
                pole.position = p - dir * 2f + left * 3.2f;
                pole.rotation = Quaternion.LookRotation(-dir);
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(pole, false);
                post.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                post.transform.localScale = new Vector3(0.12f, 1.6f, 0.12f);
                var tl = pole.gameObject.AddComponent<TrafficLight>();
                tl.phaseA = l.alongX;
                tl.Build();
                lightPoles[key] = pole;
            }
        }

        public static bool MustStop(Lane l, float time) => l.endsAtJunction && TrafficRules.Light(time, l.alongX) != LightState.Green;

        void Update()
        {
            if (lanes == null || lanes.Count == 0) return;
            var cam = Camera.main;
            if (!cam) return;
            Vector3 me = cam.transform.position;
            for (int i = cars.Count - 1; i >= 0; i--)
                if (!cars[i] || Vector3.Distance(cars[i].transform.position, me) > DespawnDistance) { if (cars[i]) Destroy(cars[i].gameObject); cars.RemoveAt(i); }

            spawnTimer -= Time.deltaTime;
            if (spawnTimer > 0f) return;
            spawnTimer = 1.5f;
            float hour = CarSalesServices.DayCycle ? CarSalesServices.DayCycle.HourOfDay : 12f;
            int wanted = Mathf.RoundToInt(TrafficRules.MaxTrafficCars * TrafficRules.Density(hour) * GameSettings.TrafficMultiplier * PerformanceGovernor.Scale);
            if (cars.Count > wanted + 2)   // thinned out (setting / frame budget): remove the farthest
            {
                var far = cars.Where(c => c).OrderByDescending(c => Vector3.Distance(c.transform.position, me)).First();
                cars.Remove(far);
                Destroy(far.gameObject);
            }
            if (cars.Count < wanted) TrySpawn(me, cam);
        }

        void TrySpawn(Vector3 me, Camera cam)
        {
            var db = CarSalesServices.Database;
            if (!db || db.cars.Count == 0) return;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var lane = lanes[Random.Range(0, lanes.Count)];
                float d = Random.Range(0f, lane.Length);
                var p2 = lane.PointAt(d);
                var p = new Vector3(p2.x, 0f, p2.z);
                float dist = Vector3.Distance(p, me);
                if (dist < SpawnMin || dist > SpawnMax) continue;
                var vp = cam.WorldToViewportPoint(p);
                if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f && dist < 150f) continue;   // don't pop in on screen
                if (cars.Any(c => c.LaneId == lane.id && Mathf.Abs(c.Distance - d) < 15f)) continue;
                var def = db.GetRandom();
                if (!def || !def.displayPrefab) continue;
                var go = new GameObject("Traffic_" + def.displayName);
                var model = Instantiate(def.displayPrefab, go.transform);
                model.transform.localPosition = Vector3.zero;
                var visual = model.GetComponent<CarVisual>();
                if (visual) visual.Apply(Color.HSVToRGB(Random.value, Random.Range(0.1f, 0.8f), Random.Range(0.2f, 0.95f)), Random.Range(0f, 0.4f));
                foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
                var car = go.AddComponent<TrafficCar>();
                car.Init(this, lane, d);
                cars.Add(car);
                return;
            }
        }

        public Lane Lane(int id) => lanes[id];

        /// <summary>The nearest traffic car ahead on the same lane (gap in metres, -1 = none).</summary>
        public float GapAhead(TrafficCar me, out float speedAhead)
        {
            speedAhead = 0f;
            float best = -1f;
            foreach (var c in cars)
            {
                if (!c || c == me || c.LaneId != me.LaneId || c.Distance <= me.Distance) continue;
                float gap = c.Distance - me.Distance;
                if (best < 0f || gap < best) { best = gap; speedAhead = c.Speed; }
            }
            return best;
        }
    }
}
