using CarSales.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarSales
{
    /// <summary>Homes, extra dealerships and income properties you can buy. Edit the list in the Inspector.</summary>
    public class PropertyManager : MonoBehaviour
    {
        public const string StarterHome = "home_trailer";

        [SerializeField] List<PropertyInfo> properties = new List<PropertyInfo>
        {
            new PropertyInfo { id = StarterHome, displayName = "Starter Trailer", type = PropertyType.Home, price = 0, wakeHour = 8f, ownedAtStart = true,
                description = "A tiny trailer. It has a bed. That's about it. Wake up at 08:00." },
            new PropertyInfo { id = "home_house", displayName = "Suburban House", type = PropertyType.Home, price = 85000, wakeHour = 7f, dailyReputation = 0.5f,
                description = "Comfortable family house. Wake up at 07:00 well rested (+0.5 reputation per day)." },
            new PropertyInfo { id = "home_villa", displayName = "Hillside Villa", type = PropertyType.Home, price = 350000, wakeHour = 6f, dailyReputation = 1f,
                description = "Luxury villa with a pool. Wake up at 06:00 (+1 reputation per day). Customers will hear about it." },

            new PropertyInfo { id = "autoworld", displayName = "AUTO WORLD", type = PropertyType.Dealership, ownedAtStart = true,
                description = "Your first dealership: showroom, office and lot." },
            new PropertyInfo { id = "dealer_downtown", displayName = "Downtown Motors", type = PropertyType.Dealership, price = 150000, dailyCost = 700,
                description = "Second dealership with 10 more parking spots and its own customers. Rent $700/day." },
            new PropertyInfo { id = "dealer_luxury", displayName = "Luxury Lane", type = PropertyType.Dealership, price = 450000, dailyCost = 1800,
                description = "Premium dealership. Customers have 60% bigger budgets. Rent $1,800/day." },

            new PropertyInfo { id = "prop_apartments", displayName = "Rental Apartments", type = PropertyType.Investment, price = 60000, dailyIncome = 450,
                description = "Small apartment block. Tenants pay $450 per day." },
            new PropertyInfo { id = "prop_parking", displayName = "Parking Garage", type = PropertyType.Investment, price = 140000, dailyIncome = 1100,
                description = "Multi-storey car park. Earns $1,100 per day." },
            new PropertyInfo { id = "prop_office", displayName = "Office Tower", type = PropertyType.Investment, price = 500000, dailyIncome = 4200,
                description = "Downtown office tower. Earns $4,200 per day." },
        };

        readonly HashSet<string> owned = new HashSet<string>();
        public string ActiveHome { get; private set; } = StarterHome;
        public event Action OnPropertiesChanged;

        public IReadOnlyList<PropertyInfo> Properties => properties;

        void Awake()
        {
            CarSalesServices.Properties = this;
            ResetToStart();
        }

        public PropertyInfo Get(string id) => properties.Find(p => p.id == id);

        /// <summary>Phase 8: properties added by World Builder sites (once per id).</summary>
        public void RegisterProperty(PropertyInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.id) || properties.Exists(p => p.id == info.id)) return;
            properties.Add(info);
            OnPropertiesChanged?.Invoke();
        }
        public bool Owns(string id) => !string.IsNullOrEmpty(id) && owned.Contains(id);
        public PropertyInfo ActiveHomeInfo => Get(ActiveHome) ?? Get(StarterHome);
        public float WakeHour => ActiveHomeInfo != null ? ActiveHomeInfo.wakeHour : 8f;

        /// <summary>Phase 9: developed properties earn more (BusinessManager.IncomeFactor).</summary>
        public int DailyIncome => properties.Where(p => Owns(p.id)).Sum(p => IncomeOf(p));
        public int IncomeOf(PropertyInfo p) => Mathf.RoundToInt(p.dailyIncome * (CarSalesServices.Business ? CarSalesServices.Business.IncomeFactor(p.id) : 1f));
        public int DailyCosts => properties.Where(p => Owns(p.id)).Sum(p => p.dailyCost);
        /// <summary>Counts toward net worth (80% of purchase price).</summary>
        public int PortfolioValue => Mathf.RoundToInt(properties.Where(p => Owns(p.id)).Sum(p => p.price) * 0.8f);

        public bool Buy(string id, out string message)
        {
            var p = Get(id);
            if (p == null) { message = "Unknown property."; return false; }
            if (Owns(id)) { message = "You already own this."; return false; }
            if (!CarSalesServices.Economy.TrySpend(p.price, TxCategory.PropertyPurchase, $"Bought {p.displayName}", p.id)) { message = $"You need {UIFactory.Money(p.price)} to buy {p.displayName}."; return false; }
            owned.Add(id);
            CarSalesServices.DayCycle.RegisterExpense(p.price);
            CarSalesServices.PlaySfx(Sfx.Success);
            if (p.type == PropertyType.Home && p.wakeHour <= WakeHour) ActiveHome = id;   // move into the better home
            OnPropertiesChanged?.Invoke();
            message = p.type == PropertyType.Home ? $"You bought {p.displayName}! It's now your home - sleep there tonight."
                    : p.type == PropertyType.Dealership ? $"You bought {p.displayName}! Its parking spots are yours. Customers come during opening hours."
                    : $"You bought {p.displayName}! It earns {UIFactory.Money(p.dailyIncome)} every night.";
            return true;
        }

        public void MoveInto(string id)
        {
            var p = Get(id);
            if (p == null || p.type != PropertyType.Home || !Owns(id)) return;
            ActiveHome = id;
            OnPropertiesChanged?.Invoke();
        }

        public void ResetToStart()
        {
            owned.Clear();
            foreach (var p in properties) if (p.ownedAtStart) owned.Add(p.id);
            ActiveHome = StarterHome;
            OnPropertiesChanged?.Invoke();
        }

        public List<string> GetOwned() => owned.ToList();

        public void SetState(List<string> ownedIds, string activeHome)
        {
            owned.Clear();
            foreach (var p in properties) if (p.ownedAtStart) owned.Add(p.id);
            if (ownedIds != null) foreach (var id in ownedIds) owned.Add(id);
            ActiveHome = !string.IsNullOrEmpty(activeHome) && Owns(activeHome) ? activeHome : StarterHome;
            OnPropertiesChanged?.Invoke();
        }
    }
}
