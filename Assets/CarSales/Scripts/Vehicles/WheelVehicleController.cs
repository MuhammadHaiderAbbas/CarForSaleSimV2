using System;
using System.Collections.Generic;
using CarSales.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Realistic driving: Rigidbody + 4 WheelColliders, an engine and automatic gearbox (CarSales.Sim.Powertrain),
    /// ABS, traction control, stability control, anti-roll bars and speed-sensitive steering.
    /// The car's component condition changes power, brakes, grip and damping (CarSales.Sim.DrivingEffects).
    /// Built at runtime around any car model by <see cref="Setup"/>; wheels are found by name (wheel/tyre/tire)
    /// or placed from the model's size when the model has none.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WheelVehicleController : MonoBehaviour, IVehicleDriver
    {
        public class Wheel
        {
            public WheelCollider collider;
            public Transform pivot;          // visual (may be null)
            public bool front, left, driven;
            public float forwardSlip, sidewaysSlip;
            public bool grounded;
        }

        const float SteerSpeed = 140f;        // degrees per second
        const float HoldBrakeTorque = 600f;   // keeps a stopped car from creeping

        public InputAction MoveAction { get; set; }
        public InputAction BrakeAction { get; set; }
        public InputAction GearUpAction { get; set; }
        public InputAction GearDownAction { get; set; }
        public bool OutOfFuel { get; set; }
        public event Action<VehicleImpact> OnImpact;

        public VehicleProfile Profile { get; private set; }
        public Powertrain Powertrain { get; private set; }
        public DrivingEffects Effects { get; private set; } = DrivingEffects.Perfect;
        public IReadOnlyList<Wheel> Wheels => wheels;
        public Bounds BodyBounds { get; private set; }
        public float Wheelbase { get; private set; }

        public float Speed { get; private set; }
        public float Rpm => Powertrain != null ? Powertrain.Rpm : 0f;
        public string GearLabel => Powertrain != null ? Powertrain.GearLabel : "N";
        public float Load => Powertrain != null ? Powertrain.Load : 0f;

        // Telemetry
        public float ThrottleInput { get; private set; }
        public float BrakeInput { get; private set; }
        public float SteerAngle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool AbsActive { get; private set; }
        public bool TcsActive { get; private set; }
        public bool EscActive { get; private set; }
        public bool Limited { get; private set; }

        readonly List<Wheel> wheels = new List<Wheel>();
        Rigidbody rb;
        float impactCooldown, wheelRadius;
        bool handbrakeFriction;

        // ------------------------------------------------------------------ SETUP
        /// <summary>
        /// Turns <paramref name="root"/> (which contains the car <paramref name="model"/>) into a physics car.
        /// Throws if the model can't be used; the caller then falls back to arcade driving.
        /// </summary>
        public static WheelVehicleController Setup(GameObject root, Transform model, VehicleProfile profile, DrivingEffects effects)
        {
            if (!root || !model || profile == null) throw new ArgumentException("Vehicle setup needs a root, a model and a profile.");
            var c = root.AddComponent<WheelVehicleController>();
            c.Profile = profile;
            c.Effects = effects;
            c.Powertrain = new Powertrain(profile) { PowerFactor = effects.Power, Manual = GameSettings.ManualGearbox };
            c.Build(model);
            return c;
        }

        void Build(Transform model)
        {
            var ch = Profile.chassis;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("The car model has no renderers.");

            // 1. Find the wheels: from the Asset Studio's VehicleRig if there is one, else by mesh name and position.
            var corners = new List<(Vector3 center, float radius, List<Transform> parts)>();
            var rig = model.GetComponentInChildren<VehicleRig>();
            if (rig && rig.HasAllWheels)
            {
                foreach (var w in rig.Wheels)
                {
                    var rs = w.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { corners.Clear(); break; }
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    corners.Add((transform.InverseTransformPoint(b.center), b.extents.y, new List<Transform> { w }));
                }
            }
            if (corners.Count != 4)
            {
                corners.Clear();
                var found = new List<(Vector3 center, float radius, List<Transform> parts)>();
                var candidates = new List<WheelCandidate>();
                foreach (var r in renderers)
                {
                    if (!WheelDetection.LooksLikeWheel(r.name)) continue;
                    Vector3 c = transform.InverseTransformPoint(r.bounds.center);
                    float radius = r.bounds.extents.y;
                    if (radius < 0.15f || radius > 0.8f) continue;
                    candidates.Add(new WheelCandidate { Id = found.Count, Name = r.name, X = c.x, Y = c.y, Z = c.z, Radius = radius });
                    found.Add((c, radius, new List<Transform> { r.transform }));
                }
                var picked = WheelDetection.AssignCorners(candidates);
                if (picked != null) foreach (int i in picked) corners.Add(found[i]);
            }

            // 2. Body bounds from everything that isn't a wheel.
            bool any = false;
            var body = new Bounds();
            foreach (var r in renderers)
            {
                bool isWheelPart = false;
                Vector3 c = transform.InverseTransformPoint(r.bounds.center);
                foreach (var w in corners)
                {
                    if (w.parts.Exists(p => r.transform == p || r.transform.IsChildOf(p))) { isWheelPart = true; break; }
                    if (Vector3.Distance(c, w.center) < w.radius * 1.05f && r.bounds.extents.y <= w.radius * 1.1f)
                    {
                        w.parts.Add(r.transform);   // hub, rim, brake disc...
                        isWheelPart = true;
                        break;
                    }
                }
                if (isWheelPart) continue;
                var lb = LocalBounds(r);
                if (!any) { body = lb; any = true; } else body.Encapsulate(lb);
            }
            if (!any) throw new InvalidOperationException("The car model has no body.");

            wheelRadius = corners.Count == 4 ? Average(corners, w => w.radius) : Profile.WheelRadiusOr(0.33f);
            wheelRadius = Profile.WheelRadiusOr(wheelRadius);
            if (corners.Count != 4)
            {
                // No usable wheel meshes: place invisible wheels at the corners of the body.
                corners.Clear();
                float x = body.extents.x - 0.2f, zf = body.max.z - Mathf.Min(0.9f, body.size.z * 0.2f), zr = body.min.z + Mathf.Min(0.9f, body.size.z * 0.2f);
                foreach (var p in new[] { new Vector3(-x, wheelRadius, zf), new Vector3(x, wheelRadius, zf), new Vector3(-x, wheelRadius, zr), new Vector3(x, wheelRadius, zr) })
                    corners.Add((p, wheelRadius, new List<Transform>()));
            }

            // 3. Collider for the body (kept above the wheels' ground contact so it never scrapes).
            float minY = Mathf.Max(body.min.y, wheelRadius * 0.55f);
            float maxY = Mathf.Max(body.max.y, minY + 0.4f);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(body.center.x, (minY + maxY) * 0.5f, body.center.z);
            box.size = new Vector3(body.size.x, maxY - minY, body.size.z);
            BodyBounds = new Bounds(box.center, box.size);

            // 4. Rigidbody.
            rb = GetComponent<Rigidbody>();
            rb.mass = Mathf.Max(300f, ch.massKg);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            SetDamping(0.02f, 0.6f);
            rb.centerOfMass = new Vector3(body.center.x, ch.centerOfMassHeight, body.center.z);

            // 5. Wheels.
            float cornerMass = rb.mass * 0.25f;
            float d = Mathf.Max(0.05f, ch.suspensionDistance);
            float spring = cornerMass * 9.81f / (0.3f * d) * Mathf.Max(0.2f, ch.springScale);
            foreach (var w in corners)
            {
                bool front = w.center.z > body.center.z, left = w.center.x < body.center.x;
                var pivot = new GameObject("WheelPivot_" + (front ? "F" : "R") + (left ? "L" : "R")).transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = w.center;
                foreach (var part in w.parts) part.SetParent(pivot, true);

                var colGo = new GameObject("WheelCollider_" + (front ? "F" : "R") + (left ? "L" : "R")) { layer = gameObject.layer };
                colGo.transform.SetParent(transform, false);
                colGo.transform.localPosition = w.center + Vector3.up * d * 0.2f;
                var wc = colGo.AddComponent<WheelCollider>();
                wc.radius = wheelRadius;
                wc.mass = 20f;
                wc.wheelDampingRate = 0.25f;
                wc.suspensionDistance = d;
                wc.forceAppPointDistance = 0.15f;
                wheels.Add(new Wheel
                {
                    collider = wc,
                    pivot = w.parts.Count > 0 ? pivot : null,
                    front = front,
                    left = left,
                    driven = Profile.drivetrain == Drivetrain.AWD || (Profile.drivetrain == Drivetrain.FWD) == front
                });
                if (w.parts.Count == 0) Destroy(pivot.gameObject);
            }
            ApplySuspension(spring);
            ApplyFriction(false);
            wheels[0].collider.ConfigureVehicleSubsteps(5f, 12, 15);

            float zFront = 0f, zRear = 0f;
            foreach (var w in wheels) if (w.front) zFront += w.collider.transform.localPosition.z * 0.5f; else zRear += w.collider.transform.localPosition.z * 0.5f;
            Wheelbase = Mathf.Max(1f, zFront - zRear);
        }

        static float Average(List<(Vector3 center, float radius, List<Transform> parts)> list, Func<(Vector3 center, float radius, List<Transform> parts), float> f)
        {
            float s = 0f;
            foreach (var x in list) s += f(x);
            return list.Count > 0 ? s / list.Count : 0f;
        }

        Bounds LocalBounds(Renderer r)
        {
            var b = r.bounds;
            var lb = new Bounds(transform.InverseTransformPoint(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                lb.Encapsulate(transform.InverseTransformPoint(corner));
            }
            return lb;
        }

        void ApplySuspension(float spring)
        {
            float cornerMass = rb.mass * 0.25f;
            float damper = 2f * 0.4f * Mathf.Sqrt(spring * cornerMass) * Mathf.Max(0.1f, Profile.chassis.damperScale) * Effects.Damping;
            foreach (var w in wheels)
            {
                var s = w.collider.suspensionSpring;
                s.spring = spring;
                s.damper = damper;
                s.targetPosition = 0.5f;
                w.collider.suspensionSpring = s;
            }
        }

        void ApplyFriction(bool handbrake)
        {
            handbrakeFriction = handbrake;
            var ch = Profile.chassis;
            foreach (var w in wheels)
            {
                float grip = (w.front ? Effects.FrontGrip : Effects.RearGrip) * (CarSalesServices.World ? CarSalesServices.World.Grip : 1f);   // phase 8: wet roads
                w.collider.forwardFriction = new WheelFrictionCurve
                {
                    extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.5f,
                    stiffness = ch.forwardGrip * grip
                };
                float side = ch.sidewaysGrip * grip * (handbrake && !w.front ? 0.55f : 1f);
                w.collider.sidewaysFriction = new WheelFrictionCurve
                {
                    extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f,
                    stiffness = side
                };
            }
        }

        /// <summary>Call after the car's condition changed (e.g. a crash) to update power, brakes, grip and damping.</summary>
        public void SetEffects(DrivingEffects effects)
        {
            Effects = effects;
            if (Powertrain != null) Powertrain.PowerFactor = effects.Power;
            if (wheels.Count == 0) return;
            ApplySuspension(wheels[0].collider.suspensionSpring.spring);
            ApplyFriction(handbrakeFriction);
        }

        // ------------------------------------------------------------------ PHYSICS
        void FixedUpdate()
        {
            if (!rb || Powertrain == null || wheels.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            var ch = Profile.chassis;
            Vector3 velocity = Velocity;
            Speed = Vector3.Dot(velocity, transform.forward);
            impactCooldown -= dt;

            Vector2 input = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;
            Handbrake = BrakeAction != null && BrakeAction.IsPressed();
            var cmd = Powertrain.Manual ? DriveInput.MapManual(input.y, Powertrain.Gear < 0)
                                        : DriveInput.Map(input.y, Speed, Powertrain.Gear < 0);
            Limited = ch.topSpeedLimitKmh > 0f && Mathf.Abs(Speed) * 3.6f >= ch.topSpeedLimitKmh;
            ThrottleInput = Limited ? 0f : cmd.Throttle;
            BrakeInput = cmd.Brake;

            // Sense the wheels.
            float drivenRpm = 0f;
            int drivenCount = 0;
            foreach (var w in wheels)
            {
                w.grounded = w.collider.GetGroundHit(out var hit);
                w.forwardSlip = w.grounded ? hit.forwardSlip : 0f;
                w.sidewaysSlip = w.grounded ? hit.sidewaysSlip : 0f;
                if (w.driven) { drivenRpm += w.collider.rpm; drivenCount++; }
            }
            drivenRpm = drivenCount > 0 ? drivenRpm / drivenCount : 0f;

            // Engine & gearbox.
            Powertrain.EngineOn = !OutOfFuel && Effects.CanStart;
            float torque = Powertrain.Step(dt, ThrottleInput, cmd.WantReverse, drivenRpm, Speed);

            // Brakes: the brakes can hold `brakeStrength` g; split front/rear by the bias.
            float brakeTotal = rb.mass * 9.81f * Mathf.Max(0.2f, ch.brakeStrength) * wheelRadius * Effects.Brakes;
            float esc = DriveAssists.EscBrake(GameSettings.StabilityControl, rb.angularVelocity.y, Mathf.Abs(Speed), SteerAngle, Wheelbase);
            bool hold = cmd.Throttle < 0.01f && cmd.Brake < 0.01f && Mathf.Abs(Speed) < 0.4f;
            AbsActive = TcsActive = false;
            EscActive = esc != 0f;
            if (Handbrake != handbrakeFriction) ApplyFriction(Handbrake);

            foreach (var w in wheels)
            {
                // Drive
                float motor = 0f;
                if (w.driven && drivenCount > 0)
                {
                    float spin = torque >= 0f ? w.forwardSlip : -w.forwardSlip;
                    float tcs = DriveAssists.TcsFactor(GameSettings.TractionControl, spin);
                    if (tcs < 1f) TcsActive = true;
                    motor = torque / drivenCount * tcs;
                }
                // Brake
                float share = w.front ? ch.frontBrakeBias * 0.5f : (1f - ch.frontBrakeBias) * 0.5f;
                float lockSlip = Speed >= 0f ? w.forwardSlip : -w.forwardSlip;
                float abs = DriveAssists.AbsFactor(GameSettings.Abs, lockSlip);
                if (abs < 1f && cmd.Brake > 0.05f) AbsActive = true;
                float brake = cmd.Brake * brakeTotal * share * abs;
                if (Handbrake && !w.front) brake += ch.handbrakeTorque;
                if (esc > 0f && w.left) brake += esc * brakeTotal * 0.12f;
                if (esc < 0f && !w.left) brake += -esc * brakeTotal * 0.12f;
                if (hold) { brake = Mathf.Max(brake, HoldBrakeTorque); motor = 0f; }
                if (Handbrake && w.driven && !w.front) motor = 0f;
                w.collider.motorTorque = motor;
                w.collider.brakeTorque = brake;
            }

            // Steering (shrinks with speed; bad alignment pulls to one side).
            float limit = DriveAssists.SteerLimit(ch.maxSteerAngle, ch.highSpeedSteerFactor, Speed);
            float pull = Mathf.Abs(Speed) > 3f ? Effects.SteeringPull : 0f;
            SteerAngle = Mathf.MoveTowards(SteerAngle, Mathf.Clamp(input.x + pull, -1f, 1f) * limit, SteerSpeed * dt);
            foreach (var w in wheels) if (w.front) w.collider.steerAngle = SteerAngle;

            ApplyAntiRoll(true);
            ApplyAntiRoll(false);
            rb.AddForce(-transform.up * ch.downforce * velocity.sqrMagnitude);
        }

        void ApplyAntiRoll(bool frontAxle)
        {
            Wheel l = null, r = null;
            foreach (var w in wheels) if (w.front == frontAxle) { if (w.left) l = w; else r = w; }
            if (l == null || r == null) return;
            float travelL = 1f, travelR = 1f;
            if (l.collider.GetGroundHit(out var hl))
                travelL = (-l.collider.transform.InverseTransformPoint(hl.point).y - l.collider.radius) / l.collider.suspensionDistance;
            if (r.collider.GetGroundHit(out var hr))
                travelR = (-r.collider.transform.InverseTransformPoint(hr.point).y - r.collider.radius) / r.collider.suspensionDistance;
            float force = (travelL - travelR) * Profile.chassis.antiRollForce;
            if (l.grounded) rb.AddForceAtPosition(l.collider.transform.up * -force, l.collider.transform.position);
            if (r.grounded) rb.AddForceAtPosition(r.collider.transform.up * force, r.collider.transform.position);
        }

        void Update()
        {
            if (Powertrain != null && Powertrain.Manual)
            {
                if (GearUpAction != null && GearUpAction.WasPressedThisFrame()) Powertrain.ShiftUp(Speed);
                if (GearDownAction != null && GearDownAction.WasPressedThisFrame()) Powertrain.ShiftDown(Speed);
            }
            foreach (var w in wheels)
            {
                if (!w.pivot) continue;
                w.collider.GetWorldPose(out var pos, out var rot);
                w.pivot.SetPositionAndRotation(pos, rot);
            }
        }

        void OnDisable()
        {
            // Menus open: park the car where it is.
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                w.collider.motorTorque = 0f;
                w.collider.brakeTorque = 5000f;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (impactCooldown > 0f || !rb || collision.contactCount == 0) return;
            var contact = collision.GetContact(0);
            if (contact.normal.y > 0.75f) return;                 // landing on the ground, not a crash
            float deltaV = collision.impulse.magnitude / rb.mass;
            if (deltaV < 2.5f) return;
            impactCooldown = 0.6f;
            OnImpact?.Invoke(new VehicleImpact { Speed = deltaV, LocalPoint = transform.InverseTransformPoint(contact.point) });
        }

        // ------------------------------------------------------------------ UNITY VERSION HELPERS
        Vector3 Velocity
        {
#if UNITY_6000_0_OR_NEWER
            get => rb.linearVelocity;
#else
            get => rb.velocity;
#endif
        }

        void SetDamping(float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear;
            rb.angularDamping = angular;
#else
            rb.drag = linear;
            rb.angularDrag = angular;
#endif
        }
    }
}
