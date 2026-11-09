using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Marks the parts of a car model the game needs: the 4 wheels (and optionally doors, bonnet, boot).
    /// Added by the Asset Studio to imported cars, so wheel detection never has to guess from mesh names.
    /// Each wheel transform should contain the tyre and rim (everything that spins).
    /// </summary>
    public class VehicleRig : MonoBehaviour
    {
        public Transform wheelFrontLeft, wheelFrontRight, wheelRearLeft, wheelRearRight;
        [Header("Optional (customer inspections, later phases)")]
        public Transform doorFrontLeft, doorFrontRight, bonnet, boot, steeringWheel;

        public bool HasAllWheels => wheelFrontLeft && wheelFrontRight && wheelRearLeft && wheelRearRight;

        /// <summary>FL, FR, RL, RR (same order as CarSales.Sim.WheelCorner).</summary>
        public Transform[] Wheels => new[] { wheelFrontLeft, wheelFrontRight, wheelRearLeft, wheelRearRight };
    }
}
