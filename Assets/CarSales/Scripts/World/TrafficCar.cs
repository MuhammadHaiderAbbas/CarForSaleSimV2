using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>One traffic car: moves along its lane, keeps a gap, obeys the lights, stops for whatever is in front (you!).</summary>
    public class TrafficCar : MonoBehaviour
    {
        TrafficSystem system;
        Lane lane;
        public int LaneId => lane != null ? lane.id : -1;
        public float Distance { get; private set; }
        public float Speed { get; private set; }
        float hazardTimer;
        BoxCollider box;

        public void Init(TrafficSystem sys, Lane l, float distance)
        {
            system = sys;
            lane = l;
            Distance = distance;
            Speed = l.speedLimit * 0.6f;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 1.4f, 4.3f);
            box.center = new Vector3(0f, 0.7f, 0f);
            Place();
        }

        void Place()
        {
            var p = lane.PointAt(Distance);
            transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.LookRotation(new Vector3(lane.Dir.x, 0f, lane.Dir.z)));
        }

        void OnCollisionEnter(Collision c)
        {
            if (!c.collider.GetComponentInParent<TrafficCar>()) hazardTimer = 6f;   // bumped: stop with the hazards on
        }

        float farDt;

        void Update()
        {
            if (lane == null) return;
            float dt = Time.deltaTime;
            // Spec §11: far cars tick at about 4 Hz.
            var cam = Camera.main;
            if (cam && (transform.position - cam.transform.position).sqrMagnitude > 120f * 120f)
            {
                farDt += dt;
                if (farDt < 0.25f) return;
                dt = farDt;
            }
            farDt = 0f;
            if (hazardTimer > 0f) { hazardTimer -= dt; Speed = 0f; return; }

            float gap = system.GapAhead(this, out float speedAhead);
            // Something else in front (the player, a parked car, a pedestrian): treat it as a stopped car.
            if (Physics.Raycast(transform.position + Vector3.up * 0.7f + transform.forward * 2.3f, transform.forward, out var hit, 14f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.GetComponentInParent<TrafficCar>() && hit.collider.attachedRigidbody != null)
            {
                if (gap < 0f || hit.distance < gap) { gap = hit.distance; speedAhead = 0f; }
            }
            float toEnd = lane.Length - Distance;
            bool stop = TrafficSystem.MustStop(lane, Time.time);
            float target = TrafficRules.TargetSpeed(lane.speedLimit, gap, speedAhead, stop ? toEnd : -1f, stop);
            Speed = TrafficRules.Approach(Speed, target, dt);
            Distance += Speed * dt;
            if (Distance >= lane.Length)
            {
                if (lane.next.Count == 0) { Destroy(gameObject); return; }   // end of the map
                float over = Distance - lane.Length;
                lane = system.Lane(lane.next[Random.Range(0, lane.next.Count)]);
                Distance = over;
            }
            var before = transform.rotation;
            Place();
            transform.rotation = Quaternion.Slerp(before, transform.rotation, Mathf.Clamp01(dt * 6f));
        }
    }
}
