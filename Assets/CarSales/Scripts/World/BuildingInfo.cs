using UnityEngine;

namespace CarSales
{
    public enum BuildingFunction { Decorative, Dealership, Workshop, CarWash, FuelStation, AuctionHall, Home, Office, Shop, Warehouse }

    /// <summary>
    /// Added to buildings imported with the Asset Studio: what the building is for and where its entrance is.
    /// The World Builder (spec Phase 8) turns these into working sites; until then they are scenery with colliders.
    /// </summary>
    public class BuildingInfo : MonoBehaviour
    {
        public BuildingFunction function = BuildingFunction.Decorative;
        [Tooltip("Where people walk in (front of the building by default). Move it to the real door.")]
        public Transform entrance;
        public Vector3 size;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * size.y * 0.5f, size);
            if (!entrance) return;
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(entrance.position + Vector3.up * 0.2f, 0.4f);
            Gizmos.DrawRay(entrance.position + Vector3.up * 0.2f, entrance.forward * 2f);
        }
    }
}
