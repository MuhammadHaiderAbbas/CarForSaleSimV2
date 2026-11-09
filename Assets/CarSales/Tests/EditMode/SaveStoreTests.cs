using System;
using System.IO;
using CarSales.Foundation;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class SaveStoreTests
    {
        // A real v4 save (shortened) as written by v4.0's SaveManager with JsonUtility.
        const string V4Save = "{\n    \"version\": 4,\n    \"money\": 61250,\n    \"reputation\": 57.5,\n    \"day\": 6,\n" +
                              "    \"ownedCars\": [ { \"instanceId\": \"c1\", \"carId\": \"hatch_velo\", \"fuel\": 40.0 } ],\n" +
                              "    \"marketListings\": [],\n    \"hour\": 21.5,\n    \"activeHome\": \"home_trailer\"\n}";

        string root;
        SaveStore store;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "carsales_tests_" + Guid.NewGuid().ToString("N"));
            store = new SaveStore(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static SaveEnvelope Make(int day, int money)
        {
            var e = new SaveEnvelope { Day = day, Money = money, Label = $"Day {day}", GameVersion = "test" };
            e.Set("game", 5, "{\"day\": " + day + ", \"money\": " + money + ", \"note\": \"quote \\\" inside\"}");
            e.Set("ledger", 1, "{\"entries\": []}");
            return e;
        }

        [Test]
        public void WriteThenLoad_RoundTripsEverything()
        {
            store.Write(1, Make(3, 1234));
            var r = store.Load(1);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.IsTrue(r.ChecksumOk);
            Assert.IsFalse(r.FromBackup);
            Assert.AreEqual(3, r.Envelope.Day);
            Assert.AreEqual(1234, r.Envelope.Money);
            Assert.AreEqual(2, r.Envelope.Modules.Count);
            Assert.AreEqual(Make(3, 1234).Get("game").Data, r.Envelope.Get("game").Data);
            Assert.IsFalse(File.Exists(store.MainFile(1) + ".tmp"));
        }

        [Test]
        public void Write_KeepsThreeRotatingBackups()
        {
            for (int day = 1; day <= 5; day++) store.Write(2, Make(day, day * 100));
            Assert.AreEqual(5, SaveEnvelope.Parse(File.ReadAllText(store.MainFile(2)), out _).Day);
            Assert.AreEqual(4, SaveEnvelope.Parse(File.ReadAllText(store.BackupFile(2, 1)), out _).Day);
            Assert.AreEqual(3, SaveEnvelope.Parse(File.ReadAllText(store.BackupFile(2, 2)), out _).Day);
            Assert.AreEqual(2, SaveEnvelope.Parse(File.ReadAllText(store.BackupFile(2, 3)), out _).Day);
            Assert.IsFalse(File.Exists(store.MainFile(2) + ".bak4"));
        }

        [Test]
        public void CorruptMainFile_LoadsNewestBackup()
        {
            store.Write(1, Make(1, 100));
            store.Write(1, Make(2, 200));
            File.WriteAllText(store.MainFile(1), "{ \"format\": \"CARSALES-SAVE\", \"modu");   // crash mid-write
            var r = store.Load(1);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(1, r.FileIndex);
            Assert.AreEqual(1, r.Envelope.Day);
            StringAssert.Contains("backup", r.Message);
        }

        [Test]
        public void EditedFile_FailsChecksum_ButBackupIsPreferred()
        {
            store.Write(1, Make(1, 100));
            store.Write(1, Make(2, 200));
            string main = store.MainFile(1);
            File.WriteAllText(main, File.ReadAllText(main).Replace("\"money\": 200", "\"money\": 999999"));
            var r = store.Load(1);
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.ChecksumOk);
            Assert.AreEqual(1, r.FileIndex, "a valid backup beats a tampered main file");
        }

        [Test]
        public void EditedFile_WithNoValidBackup_StillLoadsWithWarning()
        {
            store.Write(3, Make(7, 700));
            string main = store.MainFile(3);
            File.WriteAllText(main, File.ReadAllText(main).Replace("\"money\": 700,", "\"money\": 800,"));
            var r = store.Load(3);
            Assert.IsTrue(r.Ok);
            Assert.IsFalse(r.ChecksumOk);
            Assert.AreEqual(0, r.FileIndex);
            Assert.IsNotEmpty(r.Message);
        }

        [Test]
        public void EmptyOrInvalidSlot_FailsCleanly()
        {
            Assert.IsFalse(store.Load(1).Ok);
            Assert.IsFalse(store.Load(0).Ok);
            Assert.IsFalse(store.Load(99).Ok);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Write(4, Make(1, 1)));
        }

        [Test]
        public void Peek_And_MostRecentSlot()
        {
            var a = Make(2, 10); a.SavedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
            var b = Make(9, 90); b.SavedAtUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
            store.Write(1, a);
            store.Write(3, b);
            Assert.AreEqual(3, store.MostRecentSlot());
            var info = store.Peek(3);
            Assert.IsTrue(info.Readable);
            Assert.AreEqual(9, info.Day);
            Assert.AreEqual(90, info.Money);
            Assert.AreEqual(2026, info.SavedAtUtc.Year);
            Assert.AreEqual(2, info.SavedAtUtc.Month);
            Assert.IsFalse(store.Peek(2).Exists);
            Assert.AreEqual(2, store.FirstEmptySlot());
        }

        [Test]
        public void Delete_RemovesSlotAndBackups()
        {
            store.Write(1, Make(1, 1));
            store.Write(1, Make(2, 2));
            store.Delete(1);
            Assert.IsFalse(store.Exists(1));
            Assert.AreEqual(-1, store.MostRecentSlot());
        }

        [Test]
        public void LegacyV4File_IsWrappedAsGameModule()
        {
            var env = SaveEnvelope.Parse(V4Save, out bool ok);
            Assert.IsTrue(ok);
            Assert.IsTrue(env.IsLegacy);
            Assert.AreEqual(6, env.Day);
            Assert.AreEqual(61250, env.Money);
            var game = env.Get("game");
            Assert.AreEqual(4, game.Version);
            Assert.AreEqual(V4Save, game.Data, "v4 data must pass through untouched for JsonUtility");
            Assert.IsNull(env.Get("ledger"));
        }

        [Test]
        public void ImportLegacy_MovesOldFileIntoFirstEmptySlot()
        {
            Directory.CreateDirectory(root);
            string legacy = Path.Combine(root, "carsales_save.json");
            File.WriteAllText(legacy, V4Save);
            store.Write(1, Make(1, 1));

            int slot = store.ImportLegacy(legacy);
            Assert.AreEqual(2, slot);
            Assert.IsFalse(File.Exists(legacy));
            Assert.IsTrue(File.Exists(legacy + ".v4-imported"));
            var r = store.Load(2);
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.ChecksumOk);
            Assert.AreEqual(V4Save, r.Envelope.Get("game").Data);
            Assert.AreEqual(-1, store.ImportLegacy(legacy), "nothing left to import");
        }

        [Test]
        public void NotASaveFile_IsRejected()
        {
            Assert.Throws<FormatException>(() => SaveEnvelope.Parse("{\"hello\": 1}", out _));
            Assert.Throws<FormatException>(() => SaveEnvelope.Parse("[1,2]", out _));
        }

        [Test]
        public void ModuleWithoutData_IsRejectedOnSet()
        {
            var e = new SaveEnvelope();
            Assert.Throws<ArgumentException>(() => e.Set("game", 1, ""));
            Assert.Throws<ArgumentException>(() => e.Set("", 1, "{}"));
        }
    
        [Test]
        public void Thumbnail_LivesInTheSlotFolder_AndIsDeletedWithIt()
        {
            store.Write(2, Make(3, 100));
            string thumb = store.ThumbnailFile(2);
            Assert.AreEqual(store.SlotDir(2), Path.GetDirectoryName(thumb));
            File.WriteAllBytes(thumb, new byte[] { 1, 2, 3 });
            Assert.IsTrue(store.Peek(2).Readable, "a thumbnail must not break reading the slot");
            store.Delete(2);
            Assert.IsFalse(File.Exists(thumb));
        }
    }
}
