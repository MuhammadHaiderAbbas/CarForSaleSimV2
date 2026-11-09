using UnityEngine;

namespace CarSales
{
    /// <summary>Registers the car database. Lives on the CarSalesSystems prefab.</summary>
    public class CarSalesBootstrap : MonoBehaviour
    {
        [SerializeField] CarDatabase database;

        void Awake()
        {
            if (!database) database = CarDatabase.LoadDefault();
            CarSalesServices.Database = database;
            if (!database) Debug.LogError("[CarSales] No CarDatabase assigned on CarSalesBootstrap.");
            // Phase 5 service: added here so older generated scenes work without re-running Build Everything.
            if (!GetComponent<WorkshopManager>()) gameObject.AddComponent<WorkshopManager>();
            if (!GetComponent<MarketManager>()) gameObject.AddComponent<MarketManager>();
            if (!GetComponent<StaffManager>()) gameObject.AddComponent<StaffManager>();
            if (!GetComponent<CrmManager>()) gameObject.AddComponent<CrmManager>();
            if (!GetComponent<SourcingManager>()) gameObject.AddComponent<SourcingManager>();
            if (!GetComponent<WorldManager>()) gameObject.AddComponent<WorldManager>();
            if (!GetComponent<BusinessManager>()) gameObject.AddComponent<BusinessManager>();
            if (!GetComponent<AmbienceManager>()) gameObject.AddComponent<AmbienceManager>();
            if (!FindFirstObjectByType<TrafficSystem>()) new GameObject("Traffic").AddComponent<TrafficSystem>();
            if (!FindFirstObjectByType<PedestrianSystem>()) new GameObject("Pedestrians").AddComponent<PedestrianSystem>();
        }
    }
}
