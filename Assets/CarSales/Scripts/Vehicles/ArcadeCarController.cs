using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Simple arcade driving on a CharacterController (no wheel physics - robust and easy to tune).
    /// W/S = throttle/reverse, A/D = steer, Brake action = handbrake.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ArcadeCarController : MonoBehaviour, IVehicleDriver
    {
        public float maxSpeed = 22f;        // m/s (~80 km/h)
        public float reverseSpeed = 6f;
        public float acceleration = 7f;
        public float brakeDeceleration = 20f;
        public float coastDeceleration = 3f;
        public float steerDegreesPerSecond = 85f;
        public float gravity = -20f;

        public InputAction MoveAction { get; set; }
        public InputAction BrakeAction { get; set; }
        public InputAction GearUpAction { get; set; }     // arcade cars have no gears
        public InputAction GearDownAction { get; set; }
        public float Speed { get; private set; }
        /// <summary>When true the engine gives no power (empty tank).</summary>
        public bool OutOfFuel { get; set; }

        /// <summary>Raised when hitting something hard. Parameter = impact speed (m/s).</summary>
        public event Action<float> OnCrash;
        public event Action<VehicleImpact> OnImpact;

        // Arcade cars have no real engine: fake values for the HUD and engine sound.
        public float Rpm => 900f + Mathf.Abs(Speed) / Mathf.Max(1f, maxSpeed) * 5000f;
        public string GearLabel => Speed < -0.3f ? "R" : "D";
        public float Load { get; private set; }

        CharacterController controller;
        float verticalVelocity, crashCooldown;

        void Awake() => controller = GetComponent<CharacterController>();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector2 input = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;
            if (OutOfFuel) input.y = 0f;
            Load = Mathf.Abs(input.y);
            bool brake = BrakeAction != null && BrakeAction.IsPressed();

            float speed = Speed;
            if (brake) speed = Mathf.MoveTowards(speed, 0f, brakeDeceleration * dt);
            else if (Mathf.Abs(input.y) > 0.05f)
            {
                bool opposing = speed * input.y < 0f;
                float target = input.y > 0f ? maxSpeed * input.y : reverseSpeed * input.y;
                speed = Mathf.MoveTowards(speed, target, (opposing ? brakeDeceleration : acceleration) * dt);
            }
            else speed = Mathf.MoveTowards(speed, 0f, coastDeceleration * dt);

            // Steering works better at low-medium speed and reverses when backing up.
            float steerFactor = Mathf.Clamp01(Mathf.Abs(speed) / 4f) * Mathf.Lerp(1f, 0.55f, Mathf.Abs(speed) / maxSpeed);
            transform.Rotate(0f, input.x * steerDegreesPerSecond * steerFactor * Mathf.Sign(speed) * dt, 0f);

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;

            var flags = controller.Move((transform.forward * speed + Vector3.up * verticalVelocity) * dt);

            crashCooldown -= dt;
            if ((flags & CollisionFlags.Sides) != 0)
            {
                if (Mathf.Abs(speed) > 7f && crashCooldown <= 0f)
                {
                    crashCooldown = 1f;
                    OnCrash?.Invoke(Mathf.Abs(speed));
                    OnImpact?.Invoke(new VehicleImpact { Speed = Mathf.Abs(speed), LocalPoint = new Vector3(0f, 0.6f, speed >= 0f ? 2.5f : -2.5f) });
                }
                speed *= 0.3f;
            }
            Speed = speed;
        }
    }
}
