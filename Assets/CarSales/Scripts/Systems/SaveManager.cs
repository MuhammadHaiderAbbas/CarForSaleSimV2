using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarSales.Foundation;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CarSales
{
    /// <summary>
    /// Saves/loads the game in one of <see cref="SaveStore.SlotCount"/> slots under persistentDataPath/saves
    /// (atomic writes, 3 rotating backups, checksum, automatic fallback to a backup). Sleeping autosaves.
    /// Each system is its own module in the file: "game" (the v4 data) and "ledger" so far.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public const string LegacyFileName = "carsales_save.json";
        public const string GameModule = "game", LedgerModule = "ledger", WorkshopModule = "workshop", AuctionModule = "auction", MarketModule = "market", CrmModule = "crm", StaffModule = "staff", SourcingModule = "sourcing", WorldModule = "world", BusinessModule = "business";
        public const int GameModuleVersion = 6, LedgerModuleVersion = 1, WorkshopModuleVersion = 1, AuctionModuleVersion = 1, MarketModuleVersion = 1, CrmModuleVersion = 1, StaffModuleVersion = 1, SourcingModuleVersion = 1, WorldModuleVersion = 1, BusinessModuleVersion = 1;   // game v6: per-car components
        public const string GameVersion = "5.0-rc1";

        [SerializeField] bool loadOnStart = true;
        [SerializeField] int starterCars = 2;
        [SerializeField] string personalCarId = "hatch_velo";

        static readonly SaveMigrations Migrations = new SaveMigrations();
        // Register data upgrades here when a module's format changes, e.g.
        // static SaveManager() { Migrations.Register(GameModule, 5, json => ...); }

        /// <summary>Phase 10: PlayMode tests point saves at a temporary folder so they never touch real save games.</summary>
        public static string SavesRootOverride;
        public static string SavesRoot => !string.IsNullOrEmpty(SavesRootOverride) ? SavesRootOverride : Path.Combine(Application.persistentDataPath, "saves");
        public static string LegacyFilePath => Path.Combine(!string.IsNullOrEmpty(SavesRootOverride) ? SavesRootOverride : Application.persistentDataPath, LegacyFileName);

        /// <summary>The save store, with any pre-v5 save file moved into a slot first.</summary>
        public static SaveStore OpenStore()
        {
            var store = new SaveStore(SavesRoot);
            try
            {
                int slot = store.ImportLegacy(LegacyFilePath);
                if (slot > 0) Debug.Log($"[CarSales] Imported your v4 save into slot {slot}.");
            }
            catch (Exception e) { Debug.LogWarning($"[CarSales] Could not import the old save file: {e.Message}"); }
            return store;
        }

        public SaveStore Store { get; private set; }
        public int ActiveSlot => GameSettings.ActiveSlot;
        public string FilePath => Store.MainFile(ActiveSlot);

        void Awake()
        {
            CarSalesServices.Save = this;
            Store = OpenStore();
        }

        void Start()
        {
            bool forceNew = PlayerPrefs.GetInt(GameSettings.NewGameFlagKey, 0) == 1;
            PlayerPrefs.DeleteKey(GameSettings.NewGameFlagKey);
            if (forceNew) { DeleteSaveFile(); NewGame(); }
            else if (!(loadOnStart && LoadGame())) NewGame();
        }

        void StopEverything()
        {
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving) CarSalesServices.TestDrive.EndDrive();
            CustomerSpawner.StopAll(true);
        }

        public void NewGame()
        {
            StopEverything();
            CarSalesServices.Economy.ResetToStart();
            if (CarSalesServices.Properties) CarSalesServices.Properties.ResetToStart();
            if (CarSalesServices.Upgrades) CarSalesServices.Upgrades.SetLevels(null);
            if (CarSalesServices.Bank) CarSalesServices.Bank.SetLoan(0);
            if (CarSalesServices.Progress) CarSalesServices.Progress.ResetProgress();
            if (CarSalesServices.Workshop) CarSalesServices.Workshop.ResetState();
            if (CarSalesServices.Auction) CarSalesServices.Auction.ResetState();
            if (CarSalesServices.Market) CarSalesServices.Market.ResetState();
            if (CarSalesServices.Crm) CarSalesServices.Crm.ResetState();
            if (CarSalesServices.Staff) CarSalesServices.Staff.ResetState();
            if (CarSalesServices.Sourcing) CarSalesServices.Sourcing.ResetState();
            if (CarSalesServices.World) CarSalesServices.World.ResetState();
            if (CarSalesServices.Business) CarSalesServices.Business.ResetState();
            CarSalesServices.DayCycle.StartDay(1, 100f);

            var cars = new List<CarInstance>();
            var db = CarSalesServices.Database;
            for (int i = 0; i < starterCars && db && db.cars.Count > 0; i++)
            {
                var car = CarValueCalculator.GenerateRandom(db.GetRandom());
                car.reportedCondition = car.condition;
                car.inspected = true;
                car.fuel = Random.Range(30f, 80f);
                car.purchasePrice = Mathf.RoundToInt(CarValueCalculator.MarketValue(car) * 0.8f);
                car.state = CarValueCalculator.DefaultState(car);
                car.location = CarLocation.Spot;
                cars.Add(car);
            }

            // Your own (beat-up) car, parked at home
            var personalDef = db ? (db.Get(personalCarId) ?? db.cars.FirstOrDefault()) : null;
            if (personalDef)
            {
                var mine = CarValueCalculator.GenerateRandom(personalDef);
                CarParts.Randomize(mine, 58f);
                mine.reportedCondition = mine.condition;
                mine.inspected = true;
                mine.personal = true;
                mine.fuel = 70f;
                mine.cleanliness = 45f;
                mine.paintColor = new Color(0.25f, 0.45f, 0.75f);
                mine.purchasePrice = 0;
                mine.state = CarState.Ready;
                var homeZone = ServiceZone.Find(ServiceType.Home, PropertyManager.StarterHome);
                var bay = homeZone ? homeZone.FreeBay() : null;
                mine.location = CarLocation.World;
                mine.worldPosition = bay ? bay.position : Vector3.zero;
                mine.worldRotationY = bay ? bay.eulerAngles.y : 0f;
                cars.Add(mine);
            }

            CarSalesServices.Inventory.SetCars(cars);
            CarSalesServices.Marketplace.RefreshListings();
            if (CarSalesServices.Auction) CarSalesServices.Auction.GenerateLots();
            CarSalesServices.Vehicles.RebuildAll();
            if (CarSalesServices.Messages)
            {
                CarSalesServices.Messages.SetMessages(null);
                CarSalesServices.Messages.Add(MessageType.Info, "Welcome to AUTO WORLD!",
                    "Congratulations on your new dealership!\n\nYou live in a small trailer. Your own car is parked outside - press F next to it to drive. " +
                    "Your dealership AUTO WORLD opens at 08:00; list cars there and haggle with customers.\n\n" +
                    "Buy cars online (computer), at the Used Car Market or at the Auction. Repair them at the Mechanic Workshop, " +
                    "wash them at the Car Wash, fuel up at the gas station. Press M for the map, P for your phone (tow & taxi).\n\n" +
                    "When the day is done, drive home and sleep (from 18:00). Stay up past 02:00 and you'll pass out!");
                CarSalesServices.Messages.GenerateDaily(1);
            }
            DayCycleManager.TeleportPlayerHome();
            CarSalesServices.UI.Toast(ToastType.Success, "Welcome to AUTO WORLD!", "Your car is outside your trailer. Press F next to it to drive. M = map.");
            SaveGame();   // claim the slot right away so it shows up in the load menu
        }

        public void SaveGame()
        {
            if (CarSalesServices.TestDrive && CarSalesServices.TestDrive.IsDriving)
            {
                CarSalesServices.TestDrive.EndDrive();   // the car is saved where you stopped
            }
            var progress = CarSalesServices.Progress;
            var props = CarSalesServices.Properties;
            var day = CarSalesServices.DayCycle;
            var eco = CarSalesServices.Economy;
            var data = new SaveData
            {
                version = GameModuleVersion,
                money = eco.Money,
                reputation = eco.Reputation,
                day = day.CurrentDay,
                hour = day.Hour,
                energy = day.Energy,
                shopClosedByPlayer = day.ClosedByPlayer,
                ownedCars = CarSalesServices.Inventory.OwnedCars,
                marketListings = CarSalesServices.Marketplace.Listings,
                lotListings = CarSalesServices.Marketplace.LotListings,
                upgradeLevels = CarSalesServices.Upgrades ? CarSalesServices.Upgrades.GetLevels() : new int[0],
                loan = CarSalesServices.Bank ? CarSalesServices.Bank.Loan : 0,
                stats = progress ? progress.Stats : new GameStats(),
                objectiveIndex = progress ? progress.ObjectiveIndex : 0,
                daysInDebt = progress ? progress.DaysInDebt : 0,
                hasWon = progress && progress.HasWon,
                messages = CarSalesServices.Messages ? CarSalesServices.Messages.Messages : new List<GameMessage>(),
                ownedProperties = props ? props.GetOwned() : new List<string>(),
                activeHome = props ? props.ActiveHome : PropertyManager.StarterHome
            };

            eco.Ledger.Reconcile(eco.Money, day.CurrentDay, day.HourOfDay);
            var env = new SaveEnvelope
            {
                GameVersion = GameVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                Day = data.day,
                Money = data.money,
                Label = $"Day {data.day} ({GameCalendar.Describe(data.day)})"
            };
            env.Set(GameModule, GameModuleVersion, JsonUtility.ToJson(data, true));
            env.Set(LedgerModule, LedgerModuleVersion, JsonUtility.ToJson(eco.Ledger.State));
            if (CarSalesServices.Workshop) env.Set(WorkshopModule, WorkshopModuleVersion, JsonUtility.ToJson(CarSalesServices.Workshop.GetState()));
            if (CarSalesServices.Auction) env.Set(AuctionModule, AuctionModuleVersion, JsonUtility.ToJson(CarSalesServices.Auction.GetState()));
            if (CarSalesServices.Market) env.Set(MarketModule, MarketModuleVersion, JsonUtility.ToJson(CarSalesServices.Market.State));
            if (CarSalesServices.Crm) env.Set(CrmModule, CrmModuleVersion, JsonUtility.ToJson(CarSalesServices.Crm.State));
            if (CarSalesServices.Staff) env.Set(StaffModule, StaffModuleVersion, JsonUtility.ToJson(CarSalesServices.Staff.State));
            if (CarSalesServices.Sourcing) env.Set(SourcingModule, SourcingModuleVersion, JsonUtility.ToJson(CarSalesServices.Sourcing.State));
            if (CarSalesServices.World) env.Set(WorldModule, WorldModuleVersion, JsonUtility.ToJson(CarSalesServices.World.State));
            if (CarSalesServices.Business) env.Set(BusinessModule, BusinessModuleVersion, JsonUtility.ToJson(CarSalesServices.Business.State));
            try
            {
                Store.Write(ActiveSlot, env);
                SaveThumbnail.Capture(Camera.main, Store.ThumbnailFile(ActiveSlot));
                Debug.Log($"[CarSales] Saved slot {ActiveSlot} to {FilePath}");
                EventBus.Publish(new GameSaved(ActiveSlot));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                CarSalesServices.UI.Error($"Saving failed: {e.Message}");
            }
        }

        static T Module<T>(SaveEnvelope env, string name, int version) where T : class
        {
            var m = env.Get(name);
            if (m == null) return null;
            Migrations.Migrate(m, version);
            return JsonUtility.FromJson<T>(m.Data);
        }

        public bool LoadGame()
        {
            var result = Store.Load(ActiveSlot);
            if (!result.Ok)
            {
                if (Store.Exists(ActiveSlot)) Debug.LogWarning($"[CarSales] Slot {ActiveSlot} could not be loaded: {result.Message}");
                return false;
            }

            SaveData data;
            LedgerState ledger = null;
            WorkshopState workshop = null;
            AuctionState auction = null;
            CarSales.Sim.MarketState market = null;
            CrmState crm = null;
            StaffState staff = null;
            SourcingState sourcing = null;
            CarSales.Sim.WorldState world = null;
            BusinessState business = null;
            try
            {
                var game = result.Envelope.Get(GameModule);
                if (game == null) throw new FormatException("The save has no game data.");
                Migrations.Migrate(game, GameModuleVersion);
                data = JsonUtility.FromJson<SaveData>(game.Data);
                var led = result.Envelope.Get(LedgerModule);
                if (led != null)
                {
                    Migrations.Migrate(led, LedgerModuleVersion);
                    ledger = JsonUtility.FromJson<LedgerState>(led.Data);
                }
                var ws = result.Envelope.Get(WorkshopModule);
                if (ws != null)
                {
                    Migrations.Migrate(ws, WorkshopModuleVersion);
                    workshop = JsonUtility.FromJson<WorkshopState>(ws.Data);
                }
                var au = result.Envelope.Get(AuctionModule);
                if (au != null)
                {
                    Migrations.Migrate(au, AuctionModuleVersion);
                    auction = JsonUtility.FromJson<AuctionState>(au.Data);
                }
                market = Module<CarSales.Sim.MarketState>(result.Envelope, MarketModule, MarketModuleVersion);
                crm = Module<CrmState>(result.Envelope, CrmModule, CrmModuleVersion);
                staff = Module<StaffState>(result.Envelope, StaffModule, StaffModuleVersion);
                sourcing = Module<SourcingState>(result.Envelope, SourcingModule, SourcingModuleVersion);
                world = Module<CarSales.Sim.WorldState>(result.Envelope, WorldModule, WorldModuleVersion);
                business = Module<BusinessState>(result.Envelope, BusinessModule, BusinessModuleVersion);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CarSales] Save slot {ActiveSlot} is unreadable: {e.Message}");
                CarSalesServices.UI.Error($"Save slot {ActiveSlot} could not be loaded: {e.Message}");
                return false;
            }
            if (data == null) return false;
            var fixes = SaveValidator.Validate(data);

            StopEverything();
            CarSalesServices.Economy.SetState(data.money, data.reputation);
            CarSalesServices.Economy.LoadLedger(ledger, data.day);
            if (CarSalesServices.Properties) CarSalesServices.Properties.SetState(data.ownedProperties, data.activeHome);
            CarSalesServices.DayCycle.SetState(data.day, data.hour, data.energy, data.shopClosedByPlayer);
            if (CarSalesServices.Upgrades) CarSalesServices.Upgrades.SetLevels(data.upgradeLevels);
            if (CarSalesServices.Bank) CarSalesServices.Bank.SetLoan(data.loan);
            if (CarSalesServices.Progress) CarSalesServices.Progress.SetState(data.stats, data.objectiveIndex, data.daysInDebt, data.hasWon);
            foreach (var c in data.ownedCars) c.inspected = true;
            CarSalesServices.Inventory.SetCars(data.ownedCars);
            if (CarSalesServices.Workshop) CarSalesServices.Workshop.SetState(workshop);
            if (CarSalesServices.Market) CarSalesServices.Market.SetState(market);
            if (CarSalesServices.Crm) CarSalesServices.Crm.SetState(crm);
            if (CarSalesServices.Staff) CarSalesServices.Staff.SetState(staff);   // after the workshop: takes over its mechanic
            if (CarSalesServices.Sourcing) CarSalesServices.Sourcing.SetState(sourcing);
            if (CarSalesServices.World) CarSalesServices.World.SetState(world);
            if (CarSalesServices.Business) CarSalesServices.Business.SetState(business);
            if (CarSalesServices.Messages) CarSalesServices.Messages.SetMessages(data.messages);
            CarSalesServices.Vehicles.RebuildAll();
            CarSalesServices.Marketplace.SetListings(data.marketListings, data.lotListings);
            if (CarSalesServices.Auction) CarSalesServices.Auction.SetState(auction);
            DayCycleManager.TeleportPlayerHome();

            if (!string.IsNullOrEmpty(result.Message)) CarSalesServices.UI.Toast(ToastType.Warning, "Save recovered", result.Message);
            if (fixes > 0) Debug.LogWarning($"[CarSales] Repaired {fixes} invalid value(s) in the save.");
            CarSalesServices.UI.Notify($"Slot {ActiveSlot} loaded (Day {data.day}). You're at home.");
            EventBus.Publish(new GameLoaded(ActiveSlot, result.FromBackup));
            return true;
        }

        /// <summary>Deletes the active slot (including its backups).</summary>
        public void DeleteSaveFile() => Store.Delete(ActiveSlot);

        public void DeleteSave()
        {
            DeleteSaveFile();
            CarSalesServices.UI.Notify($"Save slot {ActiveSlot} deleted. Restart Play Mode (or New Game) for a fresh start.");
        }
    }
}
