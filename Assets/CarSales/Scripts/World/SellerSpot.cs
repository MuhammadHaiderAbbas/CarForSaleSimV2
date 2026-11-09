using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    /// <summary>A driveway where a private seller shows their car (built by the wizard on Maple Close).</summary>
    public class SellerSpot : MonoBehaviour
    {
        public static readonly List<SellerSpot> All = new List<SellerSpot>();
        public string address = "Maple Close";
        [Tooltip("Where the seller stands (empty = beside the car).")]
        public Transform sellerPoint;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
    }
}
