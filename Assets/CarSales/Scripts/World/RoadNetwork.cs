using System.Collections.Generic;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>The town's road segments for the traffic lane graph (set by the wizard / World Builder). Without one, the default town is used.</summary>
    public class RoadNetwork : MonoBehaviour
    {
        public List<RoadSegment> segments = new List<RoadSegment>();
        public float roadWidth = 9f;
        public static RoadNetwork Instance { get; private set; }
        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }
    }
}
