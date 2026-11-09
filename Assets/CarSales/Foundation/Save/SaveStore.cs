using System;
using System.IO;
using System.Text;

namespace CarSales.Foundation
{
    /// <summary>What the load menu shows for a slot.</summary>
    public sealed class SaveSlotInfo
    {
        public int Slot;
        public bool Exists;          // any file (main or backup) is present
        public bool Readable;        // a file could be read
        public bool FromBackup;      // the main file is damaged, only a backup works
        public int Day;
        public int Money;
        public string Label = "";
        public DateTime SavedAtUtc;
    }

    public sealed class SaveLoadResult
    {
        public bool Ok;
        public SaveEnvelope Envelope;
        /// <summary>0 = main file, 1..3 = backup number.</summary>
        public int FileIndex;
        public bool ChecksumOk;
        public string Message = "";
        public bool FromBackup => FileIndex > 0;
    }

    /// <summary>
    /// Save slots on disk with atomic writes and rotating backups (spec §6.2):
    /// <c>root/slot_N/save.json</c> plus <c>save.json.bak1..3</c>.
    /// Writing never leaves a half-written save: data goes to a temp file first and replaces the old file only when complete.
    /// </summary>
    public sealed class SaveStore
    {
        public const int SlotCount = 3;
        public const int BackupCount = 3;
        public const string FileName = "save.json";

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public string Root { get; }

        public SaveStore(string root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public static bool IsValidSlot(int slot) => slot >= 1 && slot <= SlotCount;

        public string SlotDir(int slot) => Path.Combine(Root, $"slot_{slot}");
        public string MainFile(int slot) => Path.Combine(SlotDir(slot), FileName);
        public string BackupFile(int slot, int n) => MainFile(slot) + ".bak" + n;
        /// <summary>Small screenshot shown in the load menu (written by the game when it saves).</summary>
        public string ThumbnailFile(int slot) => Path.Combine(SlotDir(slot), ThumbnailName);
        public const string ThumbnailName = "thumb.png";
        string FileAt(int slot, int index) => index == 0 ? MainFile(slot) : BackupFile(slot, index);

        public bool Exists(int slot)
        {
            if (!IsValidSlot(slot)) return false;
            for (int i = 0; i <= BackupCount; i++) if (File.Exists(FileAt(slot, i))) return true;
            return false;
        }

        // ------------------------------------------------------------------ WRITE
        public void Write(int slot, SaveEnvelope envelope)
        {
            if (!IsValidSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (string.IsNullOrEmpty(envelope.SavedAtUtc)) envelope.SavedAtUtc = DateTime.UtcNow.ToString("o");

            string json = envelope.ToJson();
            SaveEnvelope.Parse(json, out bool ok);        // never write something we can't read back
            if (!ok) throw new InvalidOperationException("Save data failed its own checksum.");

            Directory.CreateDirectory(SlotDir(slot));
            string main = MainFile(slot);
            string tmp = main + ".tmp";
            byte[] bytes = Utf8.GetBytes(json);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            RotateBackups(slot);
            if (File.Exists(main))
            {
                try { File.Replace(tmp, main, null); }
                catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
                {
                    File.Copy(tmp, main, true);
                    File.Delete(tmp);
                }
            }
            else File.Move(tmp, main);
        }

        void RotateBackups(int slot)
        {
            string main = MainFile(slot);
            if (!File.Exists(main)) return;
            string oldest = BackupFile(slot, BackupCount);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int n = BackupCount - 1; n >= 1; n--)
            {
                string from = BackupFile(slot, n);
                if (File.Exists(from)) File.Move(from, BackupFile(slot, n + 1));
            }
            File.Copy(main, BackupFile(slot, 1), true);
        }

        // ------------------------------------------------------------------ READ
        /// <summary>
        /// Loads a slot: the main file first, then backups 1..3. Files with a valid checksum are preferred;
        /// if none is valid, the first readable one is used (with a warning) rather than losing the game.
        /// </summary>
        public SaveLoadResult Load(int slot)
        {
            var result = new SaveLoadResult();
            if (!IsValidSlot(slot)) { result.Message = $"Slot {slot} does not exist."; return result; }

            SaveLoadResult firstReadable = null;
            string firstError = null;
            for (int i = 0; i <= BackupCount; i++)
            {
                string path = FileAt(slot, i);
                if (!File.Exists(path)) continue;
                try
                {
                    var env = SaveEnvelope.Parse(File.ReadAllText(path, Utf8), out bool checksumOk);
                    var r = new SaveLoadResult { Ok = true, Envelope = env, FileIndex = i, ChecksumOk = checksumOk };
                    if (checksumOk)
                    {
                        if (i > 0) r.Message = $"The latest save was damaged - loaded backup {i} instead.";
                        return r;
                    }
                    if (firstReadable == null) firstReadable = r;
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
                {
                    if (firstError == null) firstError = $"{Path.GetFileName(path)}: {e.Message}";
                }
            }

            if (firstReadable != null)
            {
                firstReadable.Message = firstReadable.FileIndex == 0
                    ? "The save file was edited or partly damaged; it loaded, but check your progress."
                    : $"All recent saves were damaged - loaded backup {firstReadable.FileIndex} (checksum mismatch).";
                return firstReadable;
            }
            result.Message = firstError ?? "No save in this slot.";
            return result;
        }

        public SaveSlotInfo Peek(int slot)
        {
            var info = new SaveSlotInfo { Slot = slot, Exists = Exists(slot) };
            if (!info.Exists) return info;
            var r = Load(slot);
            if (!r.Ok) return info;
            info.Readable = true;
            info.FromBackup = r.FromBackup;
            info.Day = r.Envelope.Day;
            info.Money = r.Envelope.Money;
            info.Label = r.Envelope.Label;
            info.SavedAtUtc = r.Envelope.SavedAt;
            if (info.SavedAtUtc == DateTime.MinValue)
            {
                try { info.SavedAtUtc = File.GetLastWriteTimeUtc(FileAt(slot, r.FileIndex)); } catch (IOException) { }
            }
            return info;
        }

        /// <summary>The slot with the newest readable save, or -1.</summary>
        public int MostRecentSlot()
        {
            int best = -1;
            DateTime bestTime = DateTime.MinValue;
            for (int s = 1; s <= SlotCount; s++)
            {
                var info = Peek(s);
                if (!info.Readable) continue;
                if (best < 0 || info.SavedAtUtc > bestTime) { best = s; bestTime = info.SavedAtUtc; }
            }
            return best;
        }

        public int FirstEmptySlot()
        {
            for (int s = 1; s <= SlotCount; s++) if (!Exists(s)) return s;
            return -1;
        }

        public void Delete(int slot)
        {
            if (!IsValidSlot(slot)) return;
            string dir = SlotDir(slot);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        /// <summary>
        /// Moves a pre-v5 single save file into the first empty slot (the old file is kept, renamed to *.v4-imported).
        /// Returns the slot used, or -1 if there was nothing to import or no free slot.
        /// </summary>
        public int ImportLegacy(string legacyPath)
        {
            if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath)) return -1;
            int slot = FirstEmptySlot();
            if (slot < 0) return -1;
            SaveEnvelope env;
            try { env = SaveEnvelope.Parse(File.ReadAllText(legacyPath, Utf8), out _); }
            catch (Exception e) when (e is FormatException || e is IOException) { return -1; }
            env.SavedAtUtc = File.GetLastWriteTimeUtc(legacyPath).ToString("o");
            Write(slot, env);
            string imported = legacyPath + ".v4-imported";
            if (File.Exists(imported)) File.Delete(imported);
            File.Move(legacyPath, imported);
            return slot;
        }
    }
}
