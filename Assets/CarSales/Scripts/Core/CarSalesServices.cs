using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Simple service locator. Managers register themselves in Awake().
    /// Everything else should only USE services from Start() onward.
    /// Unity's null check works here: after a scene change, destroyed managers compare equal to null.
    /// </summary>
    public static class CarSalesServices
    {
        public static CarDatabase Database;
        public static EconomyManager Economy;
        public static InventoryManager Inventory;
        public static MarketplaceManager Marketplace;
        public static DayCycleManager DayCycle;
        public static SaveManager Save;
        public static UIManager UI;
        public static DealershipManager Dealership;
        public static CustomerSpawner Spawner;
        public static UpgradeManager Upgrades;
        public static BankManager Bank;
        public static ProgressManager Progress;
        public static AudioManager Audio;
        public static TestDriveManager TestDrive;
        public static MessageManager Messages;
        public static VehicleRegistry Vehicles;
        public static PropertyManager Properties;
        public static TowService Tow;
        public static AuctionManager Auction;
        public static WorkshopManager Workshop;
        public static MarketManager Market;
        public static StaffManager Staff;
        public static CrmManager Crm;
        public static SourcingManager Sourcing;
        public static WorldManager World;
        public static BusinessManager Business;

        /// <summary>Plays a sound if an AudioManager exists in the scene.</summary>
        public static void PlaySfx(Sfx sfx, float volume = 1f)
        {
            if (Audio) Audio.Play(sfx, volume);
        }

        // Clears statics when entering Play Mode with "Domain Reload" disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Database = null; Economy = null; Inventory = null; Marketplace = null;
            DayCycle = null; Save = null; UI = null; Dealership = null; Spawner = null;
            Upgrades = null; Bank = null; Progress = null; Audio = null; TestDrive = null; Messages = null; Vehicles = null; Properties = null; Tow = null; Auction = null;
        }
    }
}
