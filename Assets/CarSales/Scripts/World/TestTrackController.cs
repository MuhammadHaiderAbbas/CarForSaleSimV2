using System;
using System.Collections.Generic;
using System.Text;
using CarSales.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// The test track scene (Tools > Car Sales > 5. Open Test Track, or "Drive on test track" in Vehicle Studio).
    /// Works on its own - no economy, saves or UI needed. Drive any car model with any condition and measure it:
    /// 0-100 km/h, braking distance from 100 km/h, top speed and cornering g.
    /// Keys: N / B next / previous car, 1-4 condition 100/75/50/25%, G realistic/arcade, M manual/automatic,
    /// R reset to the start, F3 telemetry, C camera.
    /// </summary>
    public class TestTrackController : MonoBehaviour
    {
        /// <summary>PlayerPrefs key Vehicle Studio uses to choose the car to drive.</summary>
        public const string CarPrefKey = "CarSales.TestTrackCar";

        [SerializeField] CarDatabase database;
        [SerializeField] Transform spawnPoint;
        [SerializeField] InputActionReference moveAction, brakeAction, cameraAction, gearUpAction, gearDownAction;

        readonly List<CarDefinition> cars = new List<CarDefinition>();
        int carIndex;
        float condition = 100f;
        CarInstance data;
        GameObject vehicle;
        IVehicleDriver driver;
        Rigidbody body;
        VehicleCamera vehicleCamera;
        InputAction move, brake, cameraKey, gearUp, gearDown;
        readonly StringBuilder sb = new StringBuilder(1024);
        GUIStyle style;

        // Measurements
        float accelStart = -1f, lastZeroTo100 = -1f, bestZeroTo100 = -1f;
        bool brakingRun;
        Vector3 brakeStartPos;
        float brakeStartSpeed, lastBrakeDistance = -1f, lastBrakeFrom;
        float topSpeed, lateralG, peakLateralG;
        Vector3 lastVelocity;
        PerformanceResult estimate;
        VehicleProfile shownProfile;

        void Awake()
        {
            if (!database) database = CarDatabase.LoadDefault();
            CarSalesServices.Database = database;
            if (database) cars.AddRange(database.cars.FindAll(c => c && c.displayPrefab));
            string wanted = PlayerPrefs.GetString(CarPrefKey, "");
            carIndex = Mathf.Max(0, cars.FindIndex(c => c.carId == wanted));
            VehicleTelemetry.Visible = true;
        }

        void OnEnable()
        {
            move = CarSalesInput.Resolve(moveAction, "Move");
            brake = CarSalesInput.Resolve(brakeAction, "Jump");
            cameraKey = CarSalesInput.Resolve(cameraAction, "CameraView");
            gearUp = CarSalesInput.Resolve(gearUpAction, "GearUp");
            gearDown = CarSalesInput.Resolve(gearDownAction, "GearDown");
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (cars.Count == 0) { Debug.LogError("[CarSales] Test track: no cars in the database. Run Build Everything."); return; }
            Spawn();
        }

        void Spawn()
        {
            if (vehicle) Destroy(vehicle);
            var def = cars[carIndex];
            data = new CarInstance { instanceId = Guid.NewGuid().ToString(), carId = def.carId, condition = condition, cleanliness = 100f, fuel = 100f };
            if (condition >= 99.5f) { CarParts.Randomize(data, 100f); CarParts.RepairAll(data); }
            else CarParts.Randomize(data, condition);

            Vector3 pos = spawnPoint ? spawnPoint.position : Vector3.zero;
            Quaternion rot = spawnPoint ? spawnPoint.rotation : Quaternion.identity;
            vehicle = new GameObject("TestCar_" + def.displayName) { layer = 2 };
            vehicle.transform.SetPositionAndRotation(pos + Vector3.up * 0.2f, rot);
            var model = Instantiate(def.displayPrefab, vehicle.transform);
            foreach (var col in model.GetComponentsInChildren<Collider>()) DestroyImmediate(col);
            UIFactory.SetLayer(model, 2);
            var visual = model.GetComponent<CarVisual>();
            if (visual) visual.Apply(new Color(0.85f, 0.15f, 0.12f), 0f);
            CarDamageVisual.Apply(model, data);

            var profile = VehicleProfileResolver.For(def);
            shownProfile = profile;
            estimate = PerformanceEstimator.Run(profile);
            driver = null;
            body = null;
            Bounds bounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(1.9f, 1.6f, 4.6f));
            if (GameSettings.Driving == DrivingModel.Realistic)
            {
                try
                {
                    var physics = WheelVehicleController.Setup(vehicle, model.transform, profile, CarParts.Effects(data));
                    driver = physics;
                    body = vehicle.GetComponent<Rigidbody>();
                    bounds = physics.BodyBounds;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CarSales] Test track: realistic setup failed for {def.displayName}: {e.Message}");
                    DestroyImmediate(vehicle);
                    GameSettings.Driving = DrivingModel.Arcade;
                    Spawn();
                    GameSettings.Driving = DrivingModel.Realistic;
                    return;
                }
            }
            if (driver == null)
            {
                var cc = vehicle.AddComponent<CharacterController>();
                cc.radius = 1f; cc.height = 2f; cc.center = new Vector3(0f, 1f, 0f); cc.stepOffset = 0.35f;
                driver = vehicle.AddComponent<ArcadeCarController>();
            }
            driver.MoveAction = move;
            driver.BrakeAction = brake;
            driver.GearUpAction = gearUp;
            driver.GearDownAction = gearDown;
            driver.OnImpact += i =>
            {
                var p = i.LocalPoint;
                var zone = ComponentCatalog.ZoneFromLocalPoint(p.x, p.y, p.z, bounds.extents.x, bounds.extents.z, bounds.max.y);
                CarParts.Damage(data, zone, Mathf.Clamp((i.Speed - 2f) * 2.2f, 1f, 30f));
                if (driver is WheelVehicleController w) w.SetEffects(CarParts.Effects(data));
                CarDamageVisual.Apply(model, data);
            };
            vehicle.AddComponent<EngineAudio>().Init(driver, driver is WheelVehicleController ? profile : null);
            vehicle.AddComponent<VehicleTelemetry>().Init(driver, null);

            var cam = Camera.main;
            if (cam)
            {
                if (!vehicleCamera) vehicleCamera = cam.gameObject.AddComponent<VehicleCamera>();
                vehicleCamera.Init(vehicle.transform, cameraKey, bounds);
            }
            ResetMeasurements();
        }

        void ResetMeasurements()
        {
            accelStart = -1f;
            lastZeroTo100 = -1f;
            brakingRun = false;
            lastBrakeDistance = -1f;
            topSpeed = peakLateralG = lateralG = 0f;
            lastVelocity = Vector3.zero;
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null || cars.Count == 0) return;
            if (k.nKey.wasPressedThisFrame) { carIndex = (carIndex + 1) % cars.Count; Spawn(); }
            if (k.bKey.wasPressedThisFrame) { carIndex = (carIndex + cars.Count - 1) % cars.Count; Spawn(); }
            if (k.rKey.wasPressedThisFrame) Spawn();
            if (k.gKey.wasPressedThisFrame)
            {
                GameSettings.Driving = GameSettings.Driving == DrivingModel.Realistic ? DrivingModel.Arcade : DrivingModel.Realistic;
                Spawn();
            }
            if (k.mKey.wasPressedThisFrame) { GameSettings.ManualGearbox = !GameSettings.ManualGearbox; Spawn(); }
            if (k.digit1Key.wasPressedThisFrame) { condition = 100f; Spawn(); }
            if (k.digit2Key.wasPressedThisFrame) { condition = 75f; Spawn(); }
            if (k.digit3Key.wasPressedThisFrame) { condition = 50f; Spawn(); }
            if (k.digit4Key.wasPressedThisFrame) { condition = 25f; Spawn(); }
            if (k.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (vehicle && vehicle.transform.position.y < -20f) Spawn();   // fell off the world
        }

        void FixedUpdate()
        {
            if (driver == null || !vehicle) return;
            float t = Time.time;
            float speed = Mathf.Abs(driver.Speed);
            float kmh = speed * 3.6f;
            topSpeed = Mathf.Max(topSpeed, kmh);

            // 0-100: starts when the car moves off from a standstill, ends at 100 km/h.
            if (speed < 0.3f) accelStart = 0f;
            else if (accelStart == 0f && driver.Speed > 0.3f) accelStart = t;
            if (accelStart > 0f && kmh >= 100f)
            {
                lastZeroTo100 = t - accelStart;
                if (bestZeroTo100 < 0f || lastZeroTo100 < bestZeroTo100) bestZeroTo100 = lastZeroTo100;
                accelStart = -1f;
            }

            // Braking: from the moment the brake is pressed at 95+ km/h until the car stops.
            var wheels = driver as WheelVehicleController;
            bool braking = wheels ? wheels.BrakeInput > 0.9f : move != null && move.ReadValue<Vector2>().y < -0.9f;
            if (!brakingRun && braking && kmh >= 95f)
            {
                brakingRun = true;
                brakeStartPos = vehicle.transform.position;
                brakeStartSpeed = kmh;
            }
            if (brakingRun && !braking) brakingRun = false;
            if (brakingRun && speed < 0.3f)
            {
                brakingRun = false;
                lastBrakeFrom = brakeStartSpeed;
                lastBrakeDistance = Vector3.Distance(Flat(brakeStartPos), Flat(vehicle.transform.position));
            }

            // Lateral g from the change of velocity.
            if (body)
            {
#if UNITY_6000_0_OR_NEWER
                var v = body.linearVelocity;
#else
                var v = body.velocity;
#endif
                var accel = (v - lastVelocity) / Time.fixedDeltaTime;
                lastVelocity = v;
                float lat = Mathf.Abs(Vector3.Dot(accel, vehicle.transform.right)) / 9.81f;
                lateralG = Mathf.Lerp(lateralG, lat, 0.1f);
                if (speed > 5f) peakLateralG = Mathf.Max(peakLateralG, Mathf.Min(lateralG, 2f));
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnGUI()
        {
            if (cars.Count == 0 || data == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true };
            var def = cars[carIndex];
            var profile = shownProfile;
            if (profile == null) return;
            sb.Clear();
            sb.Append("<b>TEST TRACK</b>   ").Append(def.brand).Append(' ').Append(def.displayName)
              .Append("  (").Append(carIndex + 1).Append('/').Append(cars.Count).Append(")\n");
            sb.Append(profile.vehicleClass).Append(" / ").Append(profile.powertrain).Append(" / ").Append(profile.drivetrain)
              .Append(" / ").Append(profile.chassis.massKg.ToString("0")).Append(" kg\n");
            sb.Append("Condition ").Append(data.condition.ToString("0")).Append("%   Physics ")
              .Append(driver is WheelVehicleController ? "REALISTIC" : "ARCADE").Append("   Gearbox ")
              .Append(GameSettings.ManualGearbox ? "MANUAL" : "AUTO").Append("\n\n");
            sb.Append("0-100 km/h:  ").Append(lastZeroTo100 > 0f ? lastZeroTo100.ToString("0.00") + " s" : accelStart > 0f ? "<color=yellow>timing...</color>" : "-")
              .Append("   best ").Append(bestZeroTo100 > 0f ? bestZeroTo100.ToString("0.00") + " s" : "-").Append('\n');
            sb.Append("Braking:  ").Append(lastBrakeDistance > 0f ? $"{lastBrakeDistance:0.0} m from {lastBrakeFrom:0} km/h" : brakingRun ? "<color=yellow>measuring...</color>" : "- (full brake at 95+ km/h)").Append('\n');
            sb.Append("Top speed: ").Append(topSpeed.ToString("0")).Append(" km/h    Cornering: ").Append(lateralG.ToString("0.00"))
              .Append(" g (peak ").Append(peakLateralG.ToString("0.00")).Append(" g)\n");
            var est = estimate;
            sb.Append("<color=#9fb0cc>Simulated: 0-100 ").Append(est.ZeroTo100Seconds.ToString("0.0")).Append(" s, top ")
              .Append(est.TopSpeedKmh.ToString("0")).Append(" km/h, 100-0 ").Append(est.StoppingDistance100.ToString("0")).Append(" m</color>\n\n");
            sb.Append("<size=13>N/B car   1-4 condition   G realistic/arcade   M manual/auto   R reset   C camera   F3 telemetry\n" +
                      "W gas  S brake/reverse  Space handbrake" + (GameSettings.ManualGearbox ? "  LShift/LCtrl shift" : "") + "</size>");
            GUI.Box(new Rect(Screen.width - 560f, 12f, 548f, 250f), sb.ToString(), style);
        }
    }
}
