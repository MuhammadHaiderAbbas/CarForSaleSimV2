using System.Collections.Generic;
using UnityEngine;

namespace CarSales
{
    [CreateAssetMenu(menuName = "CarSales/Car Database", fileName = "CarDatabase")]
    public class CarDatabase : ScriptableObject
    {
        public List<CarDefinition> cars = new List<CarDefinition>();

        public CarDefinition Get(string id) => cars.Find(c => c && c.carId == id);
        public CarDefinition GetRandom() => cars[Random.Range(0, cars.Count)];

        /// <summary>Resources name of the database Build Everything creates (Generated/Resources/CarSalesDatabase.asset).</summary>
        public const string ResourceName = "CarSalesDatabase";

        /// <summary>Fallback when a scene's reference to the database is missing.</summary>
        public static CarDatabase LoadDefault() => Resources.Load<CarDatabase>(ResourceName);
    }
}
