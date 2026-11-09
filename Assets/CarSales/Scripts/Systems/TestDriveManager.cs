using System;
using CarSales.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Driving any car you own. F = get in / get out (the car stays where you leave it). C = camera view.
    /// Uses realistic wheel physics (WheelVehicleController) or, if chosen in Settings or if the model can't be
    /// set up, the simple ArcadeCarController.
    /// While driving, E does the context action: park in a free dealership spot, refuel at a fuel station,
    /// wash at the car wash, repair at the workshop. Driving uses fuel and adds mileage.
    /// (Named TestDriveManager for compatibility with older versions.)
    /// </summary>
    public class TestDriveManager : MonoBehaviour
    {
        [SerializeField] InputActionReference moveAction, brakeAction, exitAction, vehicleAction, cameraAction, gearUpAction, gearDownAction;
        [Tooltip("Arcade mode only (realistic cars use their physics profile).")]
        [SerializeField] float fuelPercentPerKm = 4f;      // 100% tank = 25 km

        public bool IsDriving { get; private set; }
        public CarDisplay CurrentCar => car;
        public float SpeedKmh => car && driver != null ? Mathf.Abs(driver.Speed) * 3.6f : 0f;
        public string Gear => car && driver != null ? driver.GearLabel : "";
        public float Rpm => car && driver != null ? driver.Rpm : 0f;
        /// <summary>The realistic controller of the current drive (null in arcade mode).</summary>
        public WheelVehicleController PhysicsController => driver as WheelVehicleController;
        public float Fuel => car ? car.Data.fuel : 0f;
        public string ContextPrompt { get; private set; } = "";
        public bool BlocksInteraction => IsDriving || Time.frameCount == endFrame;

        enum Context { None, Park, Refuel, Wash, Repair }
        Context context;
        DealershipManager contextDealer;
        ParkingSpot contextSpot;

        CarDisplay car;
        GameObject vehicle;
        IVehicleDriver driver;
        VehicleProfile profile;
        Bounds carBounds;
        GameObject vehicleModel;
        Transform cam, camParent;
        VehicleCamera vehicleCamera;
        Vector3 camLocalPos, lastPos;
        Quaternion camLocalRot;
        MenuInputBlocker blocker;
        InputAction move, brake, contextKey, vehicleKey, cameraKey, gearUpKey, gearDownKey;
        int endFrame = -1;
        float distanceAccum, wearAccum, contextTimer;
        bool warnedEmpty;

        void Awake() => CarSalesServices.TestDrive = this;

        void OnEnable()
        {
            move = CarSalesInput.Resolve(moveAction, "Move");
            brake = CarSalesInput.Resolve(brakeAction, "Jump");
            contextKey = CarSalesInput.Resolve(exitAction, "Interact");
            vehicleKey = CarSalesInput.Resolve(vehicleAction, "Vehicle");
            cameraKey = CarSalesInput.Resolve(cameraAction, "CameraView");
            gearUpKey = CarSalesInput.Resolve(gearUpAction, "GearUp");
            gearDownKey = CarSalesInput.Resolve(gearDownAction, "GearDown");
            if (contextKey != null) contextKey.performed += OnContextKey;
            if (vehicleKey != null) vehicleKey.performed += OnVehicleKey;
        }

        void OnDisable()
        {
            if (contextKey != null) contextKey.performed -= OnContextKey;
            if (vehicleKey != null) vehicleKey.performed -= OnVehicleKey;
        }

        void Start() { if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged += HandleMenu; }
        void OnDestroy() { if (CarSalesServices.UI) CarSalesServices.UI.OnMenuStateChanged -= HandleMenu; }
        void HandleMenu(bool open) { if (driver != null && vehicle) driver.enabled = !open; }

        // ------------------------------------------------------------------ INPUT
        void OnVehicleKey(InputAction.CallbackContext _)
        {
            if (CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen) return;
            if (IsDriving) { EndDrive(); return; }
            var interactor = FindFirstObjectByType<PlayerInteractor>();
            var target = interactor ? interactor.CurrentTarget as CarDisplay : null;
            if (target && !target.IsForSaleListing) StartDrive(target);
        }

        void OnContextKey(InputAction.CallbackContext _)
        {
            if (!IsDriving || (CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen)) return;
            var c = car;
            switch (context)
            {
                case Context.Park:
                    var dealer = contextDealer; var spot = contextSpot;
                    EndDrive();
                    if (c && dealer && spot != null && (spot.IsFree || spot.Occupant == c))
                    {
                        CarSalesServices.Vehicles.PlaceAtSpot(c, dealer, spot);
                        CarSalesServices.UI.Success("Parked", $"{c.Definition.displayName} is parked at {dealer.displayName}, spot #{spot.index + 1}.");
                    }
                    break;
                case Context.Refuel: CarSalesServices.Vehicles.Refuel(c); warnedEmpty = false; break;
                case Context.Wash: EndDrive(); CarSalesServices.UI.carWash.Show(c); break;
                case Context.Repair: EndDrive(); CarSalesServices.UI.garage.Show(c); break;
            }
        }

        // ------------------------------------------------------------------ DRIVING
        public bool CanStart(CarDisplay target, out string reason)
        {
            reason = "";
            if (IsDriving) { reason = "You're already driving."; return false; }
            if (target == null || target.Definition == null || !target.Definition.displayPrefab) { reason = "This car can't be driven."; return false; }
            if (target.IsForSaleListing) { reason = "Buy the car first."; return false; }
            if (!Camera.main) { reason = "No main camera found."; return false; }
            if (target.Data.fuel <= 0.5f) { reason = "The tank is empty! Tow it to a fuel station (phone, P)."; return false; }
            if (target.Data.documents != null && target.Data.documents.generated && target.Data.documents.keys <= 0)
            { reason = "There's no key for this car. Get one cut by the locksmith (computer: Workshop > Paperwork)."; return false; }
            if (!CarParts.Effects(target.Data).CanStart)
            { reason = "It won't start - the 12V battery or starter motor is dead. Repair the electronics (tow it to the workshop)."; return false; }
            return true;
        }

        public void StartDrive(CarDisplay target)
        {
            if (!CanStart(target, out string reason)) { CarSalesServices.UI.Error(reason); return; }
            CarSalesServices.UI.Close();
            car = target;
            warnedEmpty = false;

            profile = VehicleProfileResolver.For(car.Definition);
            driver = null;
            if (GameSettings.Driving == DrivingModel.Realistic)
            {
                try
                {
                    var model = CreateVehicleShell();
                    var physics = WheelVehicleController.Setup(vehicle, model, profile, CarParts.Effects(car.Data));
                    carBounds = physics.BodyBounds;
                    driver = physics;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CarSales] Realistic driving unavailable for {car.Definition.displayName} ({e.Message}). Using arcade handling.");
                    if (vehicle) DestroyImmediate(vehicle);
                    driver = null;
                }
            }
            if (driver == null)
            {
                CreateVehicleShell();
                var cc = vehicle.AddComponent<CharacterController>();
                cc.radius = 1.0f;
                cc.height = 2.0f;
                cc.center = new Vector3(0f, 1.0f, 0f);
                cc.stepOffset = 0.35f;
                cc.slopeLimit = 35f;
                driver = vehicle.AddComponent<ArcadeCarController>();
                carBounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(1.9f, 1.6f, 4.6f));
            }
            driver.MoveAction = move;
            driver.BrakeAction = brake;
            driver.GearUpAction = gearUpKey;
            driver.GearDownAction = gearDownKey;
            driver.OnImpact += HandleImpact;
            vehicle.AddComponent<EngineAudio>().Init(driver, driver is WheelVehicleController ? profile : null);
            vehicle.AddComponent<VehicleTelemetry>().Init(driver, car);
            car.gameObject.SetActive(false);

            cam = Camera.main.transform;
            camParent = cam.parent;
            camLocalPos = cam.localPosition;
            camLocalRot = cam.localRotation;
            cam.SetParent(null, true);
            vehicleCamera = cam.gameObject.AddComponent<VehicleCamera>();
            vehicleCamera.Init(vehicle.transform, cameraKey, carBounds);

            blocker = FindFirstObjectByType<MenuInputBlocker>();
            if (blocker) blocker.SetBlocked(true);
            var player = FindFirstObjectByType<PlayerInteractor>();
            if (player) foreach (var r in player.GetComponentsInChildren<Renderer>()) r.enabled = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            IsDriving = true;
            lastPos = vehicle.transform.position;
            distanceAccum = wearAccum = 0f;
            if (CarSalesServices.Progress) CarSalesServices.Progress.Stats.testDrives++;
            CarSalesServices.UI.Notify(driver is WheelVehicleController
                ? (GameSettings.ManualGearbox
                    ? "MANUAL: Left Shift / Left Ctrl shift up / down (Ctrl in 1st when stopped = R). W gas, S brake, Space handbrake, C camera, F get out."
                    : "W accelerate, S brake (reverses when stopped), A/D steer, Space handbrake, C camera, F get out. E = park / refuel / wash / repair.")
                : "W/S drive, A/D steer, Space brake, C camera. F = get out. E = park / refuel / wash / repair when prompted.");
        }

        /// <summary>Creates the driving object with the car's model, minimap marker and headlights. Returns the model.</summary>
        Transform CreateVehicleShell()
        {
            vehicle = new GameObject("Driving_" + car.Definition.displayName) { layer = 2 };   // Ignore Raycast
            vehicle.transform.SetPositionAndRotation(car.transform.position + Vector3.up * 0.2f, car.transform.rotation);
            var model = Instantiate(car.Definition.displayPrefab, vehicle.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var col in model.GetComponentsInChildren<Collider>()) DestroyImmediate(col);   // the vehicle makes its own colliders
            UIFactory.SetLayer(model, 2);
            var visual = model.GetComponent<CarVisual>();
            if (visual) visual.Apply(car.Data.paintColor, 1f - car.Data.cleanliness / 100f);
            CarDamageVisual.Apply(model, car.Data);
            vehicleModel = model;
            MinimapMarker.Attach(vehicle, new Color(0.2f, 0.95f, 0.95f), 3.5f);
            var lightGo = new GameObject("Headlights");
            lightGo.transform.SetParent(vehicle.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.8f, 2.4f);
            lightGo.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            var headlights = lightGo.AddComponent<Light>();
            headlights.type = LightType.Spot;
            headlights.range = 35f;
            headlights.spotAngle = 70f;
            headlights.intensity = 4f;
            headlights.color = new Color(1f, 0.95f, 0.85f);
            return model.transform;
        }

        void Update()
        {
            if (!IsDriving || !vehicle) return;

            // Fuel & mileage
            Vector3 p = vehicle.transform.position;
            float meters = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(lastPos.x, lastPos.z));
            lastPos = p;
            if (meters > 0f && meters < 20f)
            {
                float rate = driver is WheelVehicleController ? profile.energyPercentPerKm : fuelPercentPerKm;
                car.Data.fuel = Mathf.Max(0f, car.Data.fuel - FuelModel.PercentUsed(meters, driver.Load, rate));
                distanceAccum += meters;
                wearAccum += meters;
                if (distanceAccum >= 1000f) { car.Data.mileage += 1; distanceAccum -= 1000f; }
                if (wearAccum >= 200f)
                {
                    CarParts.Wear(car.Data, wearAccum / 1000f, 1f + driver.Load * 0.6f);
                    wearAccum = 0f;
                }
            }
            driver.OutOfFuel = car.Data.fuel <= 0f;
            if (driver.OutOfFuel && !warnedEmpty)
            {
                warnedEmpty = true;
                CarSalesServices.UI.Error("Out of fuel! Get out (F) and call a tow truck from your phone (P).");
            }

            contextTimer -= Time.deltaTime;
            if (contextTimer <= 0f) { contextTimer = 0.2f; UpdateContext(p); }
        }

        void UpdateContext(Vector3 pos)
        {
            context = Context.None;
            contextDealer = null;
            contextSpot = null;
            var zone = ServiceZone.At(pos);
            if (zone)
            {
                switch (zone.type)
                {
                    case ServiceType.FuelStation:
                        context = Context.Refuel;
                        ContextPrompt = car.Data.fuel >= 99.5f ? "Tank is full" : $"[E] Refuel ({UIFactory.Money(CarSalesServices.Vehicles.RefuelCost(car.Data))})";
                        if (car.Data.fuel >= 99.5f) context = Context.None;
                        return;
                    case ServiceType.CarWash:
                        context = Context.Wash;
                        ContextPrompt = $"[E] Car wash ({UIFactory.Money(CarValueCalculator.CleanCost())})";
                        return;
                    case ServiceType.Workshop:
                        context = Context.Repair;
                        ContextPrompt = "[E] Leave the car with the mechanic (repairs)";
                        return;
                }
            }
            foreach (var d in DealershipManager.Owned)
            {
                var spot = d.NearestFreeSpot(pos, 4.5f);
                if (spot == null) continue;
                context = Context.Park;
                contextDealer = d;
                contextSpot = spot;
                ContextPrompt = $"[E] Park in {d.displayName} spot #{spot.index + 1}";
                return;
            }
            ContextPrompt = "";
        }

        void HandleImpact(VehicleImpact impact)
        {
            if (!car) return;
            var p = impact.LocalPoint;
            var zone = ComponentCatalog.ZoneFromLocalPoint(p.x, p.y, p.z, carBounds.extents.x, carBounds.extents.z, carBounds.max.y);
            float damage = Mathf.Round(Mathf.Clamp((impact.Speed - 2f) * 2.2f, 1f, 30f));
            float before = car.Data.condition;
            CarParts.Damage(car.Data, zone, damage);
            if (car.Data.state == CarState.Ready && car.Data.condition < CarValueCalculator.RepairThreshold)
                car.Data.state = CarState.NeedsRepair;
            if (driver is WheelVehicleController physics) physics.SetEffects(CarParts.Effects(car.Data));
            CarDamageVisual.Apply(vehicleModel, car.Data);
            CarSalesServices.PlaySfx(Sfx.Crash);
            CarSalesServices.UI.Error($"CRASH! {char.ToUpper(ComponentCatalog.ZoneName(zone)[0])}{ComponentCatalog.ZoneName(zone).Substring(1)} damaged. " +
                                      $"Condition {before:0}% -> {car.Data.condition:0}%");
        }

        /// <summary>Gets out: the car is parked exactly where it is, the player stands next to it.</summary>
        public void EndDrive()
        {
            if (!IsDriving) return;
            IsDriving = false;
            endFrame = Time.frameCount;
            ContextPrompt = "";

            Vector3 parkPos = vehicle ? vehicle.transform.position : car.transform.position;
            float yaw = vehicle ? vehicle.transform.eulerAngles.y : car.transform.eulerAngles.y;
            int groundMask = Physics.DefaultRaycastLayers & ~(1 << 20) & ~(1 << 2);
            if (Physics.Raycast(parkPos + Vector3.up * 2f, Vector3.down, out var hit, 10f, groundMask, QueryTriggerInteraction.Ignore))
                parkPos.y = hit.point.y;
            Vector3 exitPos = parkPos - Quaternion.Euler(0f, yaw, 0f) * Vector3.right * 2.3f;

            if (vehicleCamera) Destroy(vehicleCamera);
            vehicleCamera = null;
            if (cam)
            {
                cam.SetParent(camParent, false);
                cam.localPosition = camLocalPos;
                cam.localRotation = camLocalRot;
            }
            if (driver != null) driver.OnImpact -= HandleImpact;
            driver = null;
            if (vehicle) Destroy(vehicle);

            if (car)
            {
                car.gameObject.SetActive(true);
                CarSalesServices.Vehicles.PlaceInWorld(car, parkPos, yaw);
                CarSalesServices.Inventory.NotifyChanged();
            }
            var player = FindFirstObjectByType<PlayerInteractor>();
            if (player) foreach (var r in player.GetComponentsInChildren<Renderer>()) r.enabled = true;
            TowService.TeleportPlayer(exitPos, yaw);
            if (blocker) blocker.SetBlocked(false);
            car = null;
        }
    }
}
