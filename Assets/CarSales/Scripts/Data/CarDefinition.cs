using UnityEngine;

namespace CarSales
{
    /// <summary>Static data for one car model (the "Data Table row").</summary>
    [CreateAssetMenu(menuName = "CarSales/Car Definition", fileName = "Car_New")]
    public class CarDefinition : ScriptableObject
    {
        [Tooltip("Unique id. Never change it after players have save files.")]
        public string carId = "sedan_01";
        public string displayName = "Sedan";
        public string brand = "Generic";
        public CarCategory category = CarCategory.Sedan;
        public int year = 2015;
        public int basePrice = 20000;
        [Range(0.5f, 1.5f)] public float popularity = 1f;
        [Tooltip("Visual model prefab. Must have a CarVisual component on its root.")]
        public GameObject displayPrefab;
        [Tooltip("Optional. If empty, the UI renders a live photo of the model instead.")]
        public Sprite thumbnail;
        public string fuelType = "Petrol";
        public string transmission = "Automatic";
        [TextArea] public string description;
        [Tooltip("Optional. Replaces the handling preset for this car's class (create one in Tools > Car Sales > Vehicle Studio).")]
        public VehiclePhysicsProfileAsset physicsOverride;
    }
}
