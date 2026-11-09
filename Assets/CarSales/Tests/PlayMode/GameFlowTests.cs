using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CarSales.Tests.PlayMode
{
    /// <summary>
    /// Phase 10 PlayMode suite: loads the real game scene (made by Tools > Car Sales > 2. Build Everything) and plays
    /// through the core loop. Saves go to a temporary folder, never your own save games. Every test is skipped
    /// (not failed) if the scene isn't in Build Settings yet. Run: Window > General > Test Runner > PlayMode.
    /// </summary>
    public class GameFlowTests
    {
        const string GameScene = "DealershipTest";
        string saveRoot;
        readonly List<string> errors = new List<string>();

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Application.CanStreamedLevelBeLoaded(GameScene))
                Assert.Ignore($"Scene '{GameScene}' is not in Build Settings. Run Tools > Car Sales > 2. Build Everything first.");
            saveRoot = Path.Combine(Path.GetTempPath(), "CarSalesPlayModeTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            SaveManager.SavesRootOverride = saveRoot;
            PlayerPrefs.SetInt(GameSettings.NewGameFlagKey, 1);
            errors.Clear();
            Application.logMessageReceived += OnLog;
            SceneManager.LoadScene(GameScene);
            float until = Time.realtimeSinceStartup + 20f;
            while ((!CarSalesServices.Economy || !CarSalesServices.DayCycle || !CarSalesServices.Save || CarSalesServices.Save.Store == null) &&
                   Time.realtimeSinceStartup < until)
                yield return null;
            for (int i = 0; i < 10; i++) yield return null;   // let Start() run everywhere
            Assert.IsTrue(CarSalesServices.Economy, "the game scene didn't start (no EconomyManager)");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
            var empty = SceneManager.CreateScene("Empty" + Random.Range(0, 100000));
            yield return null;
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            SaveManager.SavesRootOverride = null;
            try { if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true); } catch { }
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void AssertNoErrors(string when) =>
            Assert.IsEmpty(errors, $"errors logged {when}:\n" + string.Join("\n", errors.Take(20)));

        [UnityTest]
        public IEnumerator Scene_Loads_WithEveryService()
        {
            yield return Frames(5);
            Assert.IsTrue(CarSalesServices.Inventory, "Inventory");
            Assert.IsTrue(CarSalesServices.Marketplace, "Marketplace");
            Assert.IsTrue(CarSalesServices.UI, "UI");
            Assert.IsTrue(CarSalesServices.Vehicles, "Vehicles");
            Assert.IsTrue(CarSalesServices.Workshop, "Workshop");
            Assert.IsTrue(CarSalesServices.Business, "Business");
            Assert.IsTrue(CarSalesServices.World, "World");
            Assert.IsTrue(AmbienceManager.Instance, "Ambience");
            Assert.AreEqual(1, CarSalesServices.DayCycle.CurrentDay, "a new game starts on day 1");
            Assert.AreEqual(CarSalesServices.Economy.Money, CarSalesServices.Economy.Ledger.Balance, "ledger matches cash");
            AssertNoErrors("while loading");
        }

        [UnityTest]
        public IEnumerator BuyCar_Online_PaysAndDelivers()
        {
            var market = CarSalesServices.Marketplace;
            var eco = CarSalesServices.Economy;
            if (market.Listings.Count == 0) market.RefreshListings();
            var listing = market.Listings.OrderBy(l => l.purchasePrice).FirstOrDefault();
            Assert.IsNotNull(listing, "the marketplace has cars for sale");
            if (!eco.CanAfford(listing.purchasePrice + 1000)) eco.AddMoney(listing.purchasePrice + 5000, CarSales.Foundation.TxCategory.Adjustment, "Test money");
            int before = eco.Money, owned = CarSalesServices.Inventory.OwnedCars.Count;
            bool ok = market.TryBuy(listing, out string message);
            yield return Frames(3);
            Assert.IsTrue(ok, message);
            Assert.AreEqual(owned + 1, CarSalesServices.Inventory.OwnedCars.Count);
            Assert.Less(eco.Money, before - listing.purchasePrice + 1, "paid the price (and delivery)");
            Assert.IsTrue(CarSalesServices.Vehicles.GetDisplay(listing.instanceId), "the car appears on the lot");
            Assert.AreEqual(eco.Money, eco.Ledger.Balance);
            AssertNoErrors("buying a car");
        }

        [UnityTest]
        public IEnumerator Sleep_AdvancesTheDay()
        {
            int day = CarSalesServices.DayCycle.CurrentDay;
            CarSalesServices.DayCycle.Sleep(false);
            yield return Frames(10);
            Assert.AreEqual(day + 1, CarSalesServices.DayCycle.CurrentDay);
            Assert.AreEqual(CarSalesServices.Economy.Money, CarSalesServices.Economy.Ledger.Balance, "rent and bills went through the ledger");
            AssertNoErrors("sleeping");
        }

        [UnityTest]
        public IEnumerator SaveAndLoad_RoundTrip()
        {
            CarSalesServices.DayCycle.Sleep(false);
            yield return Frames(10);
            var eco = CarSalesServices.Economy;
            int money = eco.Money, day = CarSalesServices.DayCycle.CurrentDay, cars = CarSalesServices.Inventory.OwnedCars.Count;
            var ids = CarSalesServices.Inventory.OwnedCars.Select(c => c.instanceId).OrderBy(x => x).ToList();
            CarSalesServices.Save.SaveGame();
            yield return Frames(2);
            Assert.IsTrue(File.Exists(CarSalesServices.Save.FilePath), "save file written to the test folder");
            StringAssert.StartsWith(saveRoot, CarSalesServices.Save.FilePath);

            eco.ForceSpend(1234, CarSales.Foundation.TxCategory.Adjustment, "Test spend");
            Assert.AreNotEqual(money, eco.Money);
            Assert.IsTrue(CarSalesServices.Save.LoadGame(), "load");
            yield return Frames(10);
            Assert.AreEqual(money, eco.Money);
            Assert.AreEqual(day, CarSalesServices.DayCycle.CurrentDay);
            Assert.AreEqual(cars, CarSalesServices.Inventory.OwnedCars.Count);
            CollectionAssert.AreEqual(ids, CarSalesServices.Inventory.OwnedCars.Select(c => c.instanceId).OrderBy(x => x).ToList());
            Assert.AreEqual(eco.Money, eco.Ledger.Balance);
            AssertNoErrors("saving and loading");
        }

        [UnityTest]
        public IEnumerator TestDrive_StartsAndEnds()
        {
            var drive = CarSalesServices.TestDrive;
            Assert.IsTrue(drive, "TestDriveManager");
            var display = CarSalesServices.Vehicles.All.FirstOrDefault(d => d.Data != null && CarRecords.IsOwned(d.Data) && drive.CanStart(d, out _));
            if (!display) Assert.Ignore("No owned car that can be driven in this scene.");
            drive.StartDrive(display);
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(drive.IsDriving, "driving");
            drive.EndDrive();
            yield return Frames(5);
            Assert.IsFalse(drive.IsDriving);
            AssertNoErrors("test driving");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Soak_FourteenDays_NoErrors_LedgerReconciles()
        {
            var eco = CarSalesServices.Economy;
            for (int d = 0; d < 14; d++)
            {
                CarSalesServices.DayCycle.AdvanceTime(4f);
                yield return Frames(5);
                CarSalesServices.DayCycle.Sleep(false);
                yield return Frames(10);
                Assert.AreEqual(eco.Money, eco.Ledger.Balance, $"ledger matches cash on day {CarSalesServices.DayCycle.CurrentDay}");
                if (CarSalesServices.Progress && CarSalesServices.Progress.IsGameOver) break;
            }
            Assert.GreaterOrEqual(CarSalesServices.DayCycle.CurrentDay, 8, "time moved on");
            AssertNoErrors("during 14 days");
        }
    }
}
