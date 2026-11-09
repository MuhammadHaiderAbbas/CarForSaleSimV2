using UnityEngine;

namespace CarSales
{
    public enum PlaceholderKind { Building = 0, Tree = 1, StreetLamp = 2, Bench = 3 }

    /// <summary>
    /// Marks a group of placeholder primitives (a backdrop building, a tree, a lamp, a bench) that the
    /// Asset Studio can swap for an imported model (Tools > Car Sales > Asset Studio > Town).
    /// The replacement is a child called "Replacement"; the placeholder's renderers are switched off.
    /// </summary>
    public class PlaceholderSlot : MonoBehaviour
    {
        public PlaceholderKind kind;
        [Tooltip("Size the replacement is fitted to (metres). Buildings fit the footprint, others the height.")]
        public Vector3 footprint = Vector3.one;
        [Tooltip("Turn applied to the replacement so its front (+Z) faces the street.")]
        public float yaw;

        public const string ReplacementName = "Replacement";

        public Transform Replacement => transform.Find(ReplacementName);

        /// <summary>Stable number for picking one of several models (based on where the slot is).</summary>
        public int StableHash()
        {
            var p = transform.position;
            unchecked
            {
                int h = (int)kind * 73856093;
                h ^= Mathf.RoundToInt(p.x * 10f) * 19349663;
                h ^= Mathf.RoundToInt(p.z * 10f) * 83492791;
                return h & 0x7fffffff;
            }
        }
    }
}
