using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// People walking along the pavements (spec §6.13). Busier by day, fewer in the rain. Walking past an open
    /// dealership, some of them go in and become customers.
    /// </summary>
    public class PedestrianSystem : MonoBehaviour
    {
        class Walker { public Transform t; public float z, x, dir, speed; public bool turnedIn; }
        static readonly float[] Pavements = { -33.2f, 101.2f };
        readonly List<Walker> walkers = new List<Walker>();
        float spawnTimer, walkInTimer;
        public int Count => walkers.Count;
        Material mat;

        void Start()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "Pedestrian" };
        }

        void OnDestroy() { if (mat) Destroy(mat); }

        void Update()
        {
            var cam = Camera.main;
            if (!cam) return;
            float hour = CarSalesServices.DayCycle ? CarSalesServices.DayCycle.HourOfDay : 12f;
            float weather = CarSalesServices.World ? WorldSim.Footfall(CarSalesServices.World.Weather) : 1f;
            int wanted = Mathf.RoundToInt(TrafficRules.MaxPedestrians * TrafficRules.Density(hour) * weather * GameSettings.TrafficMultiplier * PerformanceGovernor.Scale);

            if (walkers.Count > wanted + 2 && walkers.Count > 0) { var last = walkers[walkers.Count - 1]; if (last.t) Destroy(last.t.gameObject); walkers.RemoveAt(walkers.Count - 1); }
            for (int i = walkers.Count - 1; i >= 0; i--)
            {
                var w = walkers[i];
                if (!w.t) { walkers.RemoveAt(i); continue; }
                w.x += w.dir * w.speed * Time.deltaTime;
                w.t.position = new Vector3(w.x, 0f, w.z);
                w.t.rotation = Quaternion.LookRotation(new Vector3(w.dir, 0f, 0f));
                if (Mathf.Abs(w.x) > 245f || Vector3.Distance(w.t.position, cam.transform.position) > 200f) { Destroy(w.t.gameObject); walkers.RemoveAt(i); }
            }

            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f && walkers.Count < wanted)
            {
                spawnTimer = 1.2f;
                float z = Pavements[Random.Range(0, Pavements.Length)] + Random.Range(-0.6f, 0.6f);
                float x = cam.transform.position.x + Random.Range(-120f, 120f);
                if (Mathf.Abs(x) < 240f && Mathf.Abs(z - cam.transform.position.z) < 160f) Spawn(x, z);
            }

            // Walk-ins: someone passing an open dealership goes in.
            walkInTimer -= Time.deltaTime;
            if (walkInTimer > 0f) return;
            walkInTimer = 4f;
            foreach (var w in walkers.Where(x => !x.turnedIn))
                foreach (var s in CustomerSpawner.All)
                {
                    if (!s || !s.Dealer.IsOpenNow) continue;
                    var p = s.transform.position;
                    if (Mathf.Abs(w.x - p.x) < 12f && Mathf.Abs(w.z - p.z) < 45f && Random.value < 0.15f)
                    {
                        w.turnedIn = true;
                        s.SpawnWalkIn();
                        Destroy(w.t.gameObject);
                        return;
                    }
                }
        }

        void Spawn(float x, float z)
        {
            var root = new GameObject("Pedestrian").transform;
            var look = AssetRegistry.RandomCustomerModel();
            if (look)
            {
                var v = Instantiate(look, root);
                foreach (var c in v.GetComponentsInChildren<Collider>()) Destroy(c);
                var anim = v.GetComponentInChildren<Animator>();
                if (anim) { anim.applyRootMotion = false; anim.SetFloat("Speed", 1.3f); }
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(root, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                var r = body.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                var mpb = new MaterialPropertyBlock();
                var col = Random.ColorHSV(0f, 1f, 0.3f, 0.7f, 0.4f, 0.9f);
                mpb.SetColor("_BaseColor", col); mpb.SetColor("_Color", col);
                r.SetPropertyBlock(mpb);
            }
            walkers.Add(new Walker { t = root, x = x, z = z, dir = Random.value < 0.5f ? -1f : 1f, speed = Random.Range(1.1f, 1.6f) });
        }
    }
}
