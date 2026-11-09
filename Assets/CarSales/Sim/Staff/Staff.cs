using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    public enum StaffRole { Salesperson = 0, Mechanic = 1, Detailer = 2, Driver = 3, Receptionist = 4, Manager = 5 }

    [Serializable]
    public class Employee
    {
        public string id, name;
        public int role;
        public int skill;          // 0-100
        public int wage;           // per week
        public float morale = 0.75f;
        public float experience;   // grows with work, raises skill
        public int hiredDay;
        public int sickDay = -1;   // off sick on this day
        public int jobsDone;
        public StaffRole Role => (StaffRole)role;
        public float Skill01 => skill / 100f;
    }

    /// <summary>Hiring, wages, sick days and how staff do their jobs (spec §6.11). Pure; tests in StaffTests.</summary>
    public static class StaffRules
    {
        static readonly string[] Names = { "Sam", "Alex", "Jordan", "Priya", "Marco", "Chloe", "Dev", "Hannah", "Kofi", "Lena", "Owen", "Mei", "Tariq", "Zoe", "Ravi", "Ellie" };
        static readonly string[] Surnames = { "Patel", "Smith", "Okafor", "Novak", "Jones", "Khan", "Silva", "Brown", "Wright", "Chen", "Evans", "Murphy" };

        public static string RoleName(StaffRole r) => r == StaffRole.Salesperson ? "Salesperson" : r == StaffRole.Mechanic ? "Mechanic" : r == StaffRole.Detailer ? "Detailer" :
                                                      r == StaffRole.Driver ? "Driver" : r == StaffRole.Receptionist ? "Receptionist" : "Manager";

        public static string RoleDuty(StaffRole r) =>
            r == StaffRole.Salesperson ? "Talks to customers you don't reach and closes deals above your minimum margin." :
            r == StaffRole.Mechanic ? "Works through the Ready column of the workshop board." :
            r == StaffRole.Detailer ? "Washes and valets the cars on your lots." :
            r == StaffRole.Driver ? "Brings paid auction cars from the Auction Center to your dealership." :
            r == StaffRole.Receptionist ? "Answers calls: 30% more online leads, customers arrive 20% more often." :
            "Keeps everyone happy and busy: +10% morale and +10% work speed for all staff.";

        /// <summary>Weekly wage: a base per role plus skill.</summary>
        public static int Wage(StaffRole r, int skill)
        {
            int baseWage = r == StaffRole.Mechanic ? 520 : r == StaffRole.Salesperson ? 480 : r == StaffRole.Driver ? 380 :
                           r == StaffRole.Manager ? 700 : r == StaffRole.Receptionist ? 330 : 340;
            return (int)Math.Round((baseWage + skill * 6) / 10.0) * 10;
        }

        public static List<Employee> Candidates(int week, Random rng)
        {
            var list = new List<Employee>();
            int n = 3 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var role = (StaffRole)rng.Next(6);
                int skill = 25 + rng.Next(66);
                list.Add(new Employee
                {
                    id = $"w{week}c{i}", role = (int)role, skill = skill, wage = Wage(role, skill),
                    name = $"{Names[rng.Next(Names.Length)]} {Surnames[rng.Next(Surnames.Length)]}",
                    morale = 0.6f + (float)rng.NextDouble() * 0.3f,
                });
            }
            return list;
        }

        /// <summary>Chance to call in sick on a given day (low morale = more sick days).</summary>
        public static float SickChance(Employee e) => 0.01f + (1f - e.morale) * 0.05f;

        /// <summary>Experience from finished work slowly raises skill (max 100).</summary>
        public static void GainExperience(Employee e, float amount)
        {
            e.experience += amount;
            while (e.experience >= 1f && e.skill < 100) { e.experience -= 1f; e.skill++; }
            e.jobsDone++;
        }

        /// <summary>
        /// A salesperson's deal: they get a share of the way from the customer's opening offer to the customer's maximum
        /// (more with skill), and never go below your floor. Returns the price, or 0 if no deal is possible.
        /// </summary>
        public static int SalespersonDeal(int customerOpening, int customerMax, int floorPrice, int skill)
        {
            if (customerMax < floorPrice) return 0;
            float share = 0.35f + skill / 100f * 0.55f;
            int price = (int)(customerOpening + (customerMax - customerOpening) * share);
            price = Math.Max(price, floorPrice);
            return Math.Min(price, customerMax) / 50 * 50;
        }

        /// <summary>Hours of workshop work a mechanic gets through per clock hour.</summary>
        public static float MechanicSpeed(int skill) => 0.6f + skill / 100f * 0.6f;

        /// <summary>Cleanliness points a detailer adds per working hour to the car they're on.</summary>
        public static float DetailPerHour(int skill) => 30f + skill * 0.4f;

        /// <summary>Morale drifts towards a level set by pay vs. the market rate for their skill.</summary>
        public static void UpdateMorale(Employee e, bool manager = false)
        {
            float fair = Wage(e.Role, e.skill);
            float target = Math.Max(0.2f, Math.Min(1f, 0.7f + (e.wage - fair) / fair + (manager ? 0.1f : 0f)));
            e.morale += (target - e.morale) * 0.3f;
        }
    }
}
