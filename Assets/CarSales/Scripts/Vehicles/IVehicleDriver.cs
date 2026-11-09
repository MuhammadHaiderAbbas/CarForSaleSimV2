using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    public struct VehicleImpact
    {
        public float Speed;          // change of speed in the hit, m/s
        public Vector3 LocalPoint;   // where it was hit, in the car's local space
    }

    /// <summary>
    /// What TestDriveManager needs from a driving model. Implemented by the realistic
    /// <see cref="WheelVehicleController"/> and the simple <see cref="ArcadeCarController"/> fallback.
    /// </summary>
    public interface IVehicleDriver
    {
        InputAction MoveAction { set; }
        /// <summary>Space: handbrake (realistic) / brake (arcade).</summary>
        InputAction BrakeAction { set; }
        /// <summary>Manual gearbox shift buttons (ignored by automatic and arcade cars).</summary>
        InputAction GearUpAction { set; }
        InputAction GearDownAction { set; }
        /// <summary>Signed forward speed in m/s (negative while reversing).</summary>
        float Speed { get; }
        float Rpm { get; }
        string GearLabel { get; }
        /// <summary>0..1 engine effort (fuel use, engine sound).</summary>
        float Load { get; }
        bool OutOfFuel { get; set; }
        bool enabled { get; set; }
        event Action<VehicleImpact> OnImpact;
    }
}
