using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// An editable physics profile. Assign it to a CarDefinition's "Physics Override" to replace the class preset
    /// (Tools > Car Sales > Vehicle Studio creates one pre-filled from the preset).
    /// </summary>
    [CreateAssetMenu(menuName = "CarSales/Vehicle Physics Profile", fileName = "Physics_New")]
    public class VehiclePhysicsProfileAsset : ScriptableObject
    {
        public VehicleProfile profile = VehicleProfiles.For(VehicleClass.Sedan, PowertrainType.Petrol);

        void OnValidate() => profile?.engine?.Invalidate();
    }

    /// <summary>Chooses the physics profile for a car model.</summary>
    public static class VehicleProfileResolver
    {
        public static VehicleClass ClassOf(CarDefinition def)
        {
            if (!def) return VehicleClass.Sedan;
            var pt = PowertrainTypes.Parse(def.fuelType);
            switch (def.category)
            {
                case CarCategory.Hatchback:
                    if (pt == PowertrainType.Electric) return VehicleClass.ElectricHatch;
                    return def.basePrice < 14000 ? VehicleClass.CityHatch : VehicleClass.FamilyHatch;
                case CarCategory.Sedan: return def.basePrice >= 24000 ? VehicleClass.SportsSedan : VehicleClass.Sedan;
                case CarCategory.SUV: return VehicleClass.SUV;
                case CarCategory.Sports: return VehicleClass.Sports;
                case CarCategory.Pickup: return VehicleClass.Pickup;
                case CarCategory.Luxury: return VehicleClass.Luxury;
                default: return VehicleClass.Sedan;
            }
        }

        /// <summary>A private copy of the car's profile (its override asset, or the preset for its class and fuel type).</summary>
        public static VehicleProfile For(CarDefinition def)
        {
            if (def && def.physicsOverride && def.physicsOverride.profile != null) return def.physicsOverride.profile.Copy();
            return VehicleProfiles.For(ClassOf(def), PowertrainTypes.Parse(def ? def.fuelType : null));
        }
    }
}
