using System;
using System.Collections.Generic;
using System.Text;

namespace CarSales.Sim
{
    public enum ServiceHistory { None = 0, Partial = 1, Full = 2 }
    /// <summary>Insurance write-off categories (UK). N = non-structural, S = structural damage, repaired.</summary>
    public enum WriteOff { None = 0, CatN = 1, CatS = 2 }

    /// <summary>VIN, registration plate and first-registration year.</summary>
    [Serializable]
    public class VehicleIdentity
    {
        public string vin = "";
        public string plate = "";
        public int firstRegYear;
        public bool IsSet => !string.IsNullOrEmpty(vin);
    }

    [Serializable]
    public class MotRecord
    {
        public int year;          // calendar year of the test
        public int day;           // game day (0 = before the game started)
        public int mileage;       // odometer reading at the test
        public bool passed;
        public List<string> fails = new List<string>();
        public List<string> advisories = new List<string>();
    }

    /// <summary>The paperwork that comes with the car. Visible to anyone looking at the car (spec §6.7).</summary>
    [Serializable]
    public class DocumentSet
    {
        public bool generated;
        public bool v5cPresent = true;
        public int v5cArrivesDay;      // > 0: replacement logbook ordered, arrives that day
        public int keepers = 1;
        public int keys = 2;
        public int motExpiryDay = -1;  // game day the MOT runs out; -1 = car too young to need one
        public List<MotRecord> motHistory = new List<MotRecord>();
        public ServiceHistory service = ServiceHistory.Partial;
        public int lastServiceKm;
        public int serviceStamps;
    }

    /// <summary>The car's past. Hidden until a history check (or clues found while inspecting).</summary>
    [Serializable]
    public class HistoryRecord
    {
        public bool generated;
        public WriteOff writeOff;
        public bool outstandingFinance;
        public float financeShare;     // outstanding finance as a share of the car's value
        public bool clocked;           // odometer wound back
        public int trueMileage;
        public int plateChanges;
        // What the player knows
        public bool checkedHistory;    // a history check was run
        public int checkedDay;
        public bool clockingSuspected; // the interior wear didn't match the odometer
        public bool clockingConfirmed; // the history check found an earlier, higher reading
        public bool financeSettled;
    }

    /// <summary>What the player has done to learn about the car (spec §6.5: the UI shows knowledge, not truth).</summary>
    [Serializable]
    public class VehicleKnowledge
    {
        public int methodsDone;        // InspectMethod flags
        public float roadTestKm;
        public bool Done(InspectMethod m) => (methodsDone & (int)m) != 0;
        public void Mark(InspectMethod m) => methodsDone |= (int)m;
    }

    // ------------------------------------------------------------------ VIN
    /// <summary>17-character VINs (no I, O, Q) with the North-American check digit in position 9.</summary>
    public static class Vin
    {
        const string Chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        static readonly int[] Weights = { 8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2 };

        static int Value(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            const string map = "ABCDEFGHJKLMNPRSTUVWXYZ";
            int[] vals = { 1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 7, 9, 2, 3, 4, 5, 6, 7, 8, 9 };
            int i = map.IndexOf(c);
            return i < 0 ? -1 : vals[i];
        }

        public static char CheckDigit(string vin)
        {
            int sum = 0;
            for (int i = 0; i < 17; i++) if (i != 8) sum += Value(vin[i]) * Weights[i];
            int r = sum % 11;
            return r == 10 ? 'X' : (char)('0' + r);
        }

        public static string Generate(Random rng, string worldMaker = "SAJ")
        {
            var sb = new StringBuilder(worldMaker.Length >= 3 ? worldMaker.Substring(0, 3) : "SAJ");
            while (sb.Length < 17) sb.Append(Chars[rng.Next(Chars.Length)]);
            sb[8] = '0';
            sb[8] = CheckDigit(sb.ToString());
            return sb.ToString();
        }

        public static bool IsValid(string vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length != 17) return false;
            foreach (char c in vin) if (Value(c) < 0) return false;
            return CheckDigit(vin) == vin[8];
        }
    }

    // ------------------------------------------------------------------ UK PLATE
    /// <summary>Current UK format "AB12 CDE": 2 area letters, age identifier, 3 random letters.</summary>
    public static class UkPlate
    {
        const string Letters = "ABCDEFGHJKLMNOPRSTUVWXY";   // no I, Q, Z

        /// <summary>Age identifier: March registrations use the year (25), September ones year + 50 (75).</summary>
        public static int AgeIdentifier(int year, bool september) => (year % 100) + (september ? 50 : 0);

        public static string Generate(int year, Random rng)
        {
            int age = AgeIdentifier(Math.Max(2001, year), rng.NextDouble() < 0.5);
            char L() => Letters[rng.Next(Letters.Length)];
            return $"{L()}{L()}{age:00} {L()}{L()}{L()}";
        }

        /// <summary>Year a plate's age identifier stands for (2001-2049), or -1.</summary>
        public static int YearOf(string plate)
        {
            if (string.IsNullOrEmpty(plate) || plate.Length < 4 || !char.IsDigit(plate[2]) || !char.IsDigit(plate[3])) return -1;
            int n = (plate[2] - '0') * 10 + (plate[3] - '0');
            if (n >= 51) n -= 50;
            return n >= 1 && n <= 49 ? 2000 + n : -1;
        }
    }

    // ------------------------------------------------------------------ GENERATION & VALUE
    public static class VehicleRecords
    {
        public const int GameStartYear = 2025;
        public const int MotFreeYears = 3;
        public const int MotValidDays = 365;
        public const int V5cReplacementDays = 5;

        public static int CurrentYear(int day) => GameStartYear + Foundation.GameCalendar.YearOf(day) - 1;
        public static int AgeYears(int modelYear, int day) => Math.Max(0, CurrentYear(day) - modelYear);
        public static bool NeedsMot(int modelYear, int day) => AgeYears(modelYear, day) >= MotFreeYears;
        public static bool MotValid(DocumentSet d, int modelYear, int day) => !NeedsMot(modelYear, day) || d.motExpiryDay >= day;

        /// <summary>Stable seed from a car's id (string.GetHashCode differs between runs).</summary>
        public static int Seed(string id)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id ?? "") { h ^= c; h *= 16777619; }
                return (int)(h & 0x7fffffff);
            }
        }

        public static VehicleIdentity NewIdentity(int modelYear, Random rng) =>
            new VehicleIdentity { vin = Vin.Generate(rng), plate = UkPlate.Generate(modelYear, rng), firstRegYear = modelYear };

        /// <summary>
        /// Paperwork and past for a used car. <paramref name="displayedMileage"/> is what the odometer shows.
        /// Probabilities: missing logbook 8%, 1 key 27%, no keys 3%, Cat N 7%, Cat S 3%, finance 5%, clocked 6%.
        /// </summary>
        public static void Generate(DocumentSet docs, HistoryRecord history, int modelYear, int displayedMileage, int day, Random rng)
        {
            int age = AgeYears(modelYear, day);
            double r() => rng.NextDouble();

            docs.generated = true;
            docs.v5cPresent = r() >= 0.08;
            docs.keepers = 1 + age / 3 + rng.Next(0, 2 + age / 4);
            double k = r();
            docs.keys = k < 0.03 ? 0 : k < 0.30 ? 1 : 2;
            double s = r();
            docs.service = s < 0.45 ? ServiceHistory.Full : s < 0.8 ? ServiceHistory.Partial : ServiceHistory.None;
            docs.serviceStamps = docs.service == ServiceHistory.Full ? Math.Max(1, age) : docs.service == ServiceHistory.Partial ? Math.Max(1, age / 2) : 0;
            docs.lastServiceKm = docs.service == ServiceHistory.None ? 0 : Math.Max(0, displayedMileage - rng.Next(1000, docs.service == ServiceHistory.Full ? 15000 : 40000));

            history.generated = true;
            double w = r();
            history.writeOff = w < 0.03 ? WriteOff.CatS : w < 0.10 ? WriteOff.CatN : WriteOff.None;
            history.outstandingFinance = r() < 0.05;
            history.financeShare = history.outstandingFinance ? 0.2f + (float)r() * 0.4f : 0f;
            history.clocked = age >= 2 && r() < 0.06;
            history.trueMileage = history.clocked ? (int)(displayedMileage * (1.3 + r() * 0.5)) : displayedMileage;
            history.plateChanges = r() < 0.1 ? 1 : 0;

            // MOT: valid until somewhere in the next year, or already expired (about 15%).
            docs.motHistory.Clear();
            if (NeedsMot(modelYear, day))
            {
                docs.motExpiryDay = r() < 0.15 ? day - rng.Next(1, 90) : day + rng.Next(20, 340);
                int now = CurrentYear(day);
                int tests = age - MotFreeYears + 1;
                for (int i = 0; i < tests; i++)
                {
                    int year = now - tests + i;
                    float share = (year - modelYear) / (float)Math.Max(1, age);
                    // A clocked car's history shows the TRUE readings, so a later advert reading is lower than a past test.
                    int reading = (int)(history.trueMileage * Math.Min(1f, share));
                    var rec = new MotRecord { year = year, day = 0, mileage = reading, passed = r() > 0.2 };
                    if (!rec.passed) rec.fails.Add(r() < 0.5 ? "Tyre tread below 1.6 mm" : "Brake pads worn");
                    if (r() < 0.5) rec.advisories.Add(r() < 0.5 ? "Slight corrosion on brake pipes" : "Tyres close to the legal limit");
                    docs.motHistory.Add(rec);
                    if (!rec.passed) docs.motHistory.Add(new MotRecord { year = year, mileage = reading + 30, passed = true });
                }
            }
            else docs.motExpiryDay = -1;
        }

        /// <summary>Highest mileage ever recorded at an MOT.</summary>
        public static int HighestRecordedMileage(DocumentSet docs)
        {
            int max = 0;
            foreach (var m in docs.motHistory) if (m.mileage > max) max = m.mileage;
            return max;
        }

        /// <summary>True when the odometer shows less than an earlier MOT recorded (what a history check spots).</summary>
        public static bool MileageAnomaly(DocumentSet docs, int displayedMileage) => HighestRecordedMileage(docs) > displayedMileage + 500;

        /// <summary>
        /// Value multiplier from paperwork and history (spec §6.7). <paramref name="knownOnly"/>: hidden history flags only
        /// count once the player has run a history check (what a buyer who hasn't checked would pay).
        /// </summary>
        public static float ValueMultiplier(DocumentSet d, HistoryRecord h, int modelYear, int day, bool knownOnly)
        {
            float m = 1f;
            if (d != null && d.generated)
            {
                if (!d.v5cPresent) m *= 0.75f;
                if (!MotValid(d, modelYear, day)) m *= 0.9f;
                m *= d.service == ServiceHistory.Full ? 1.06f : d.service == ServiceHistory.None ? 0.95f : 1f;
                m *= d.keys >= 2 ? 1f : d.keys == 1 ? 0.98f : 0.9f;
            }
            if (h != null && h.generated && (!knownOnly || h.checkedHistory))
            {
                if (h.writeOff == WriteOff.CatN) m *= 0.8f;
                else if (h.writeOff == WriteOff.CatS) m *= 0.65f;
                if (h.clocked && (!knownOnly || h.clockingConfirmed)) m *= 0.85f;
                else if (knownOnly && h.clockingSuspected) m *= 0.92f;
            }
            else if (h != null && knownOnly && h.clockingSuspected) m *= 0.92f;
            return m;
        }
    }
}
