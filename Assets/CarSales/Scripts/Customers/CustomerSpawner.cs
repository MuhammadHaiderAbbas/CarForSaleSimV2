using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace CarSales
{
    /// <summary>Spawns customers at its dealership automatically while that dealership is open.</summary>
    [RequireComponent(typeof(DealershipManager))]
    public class CustomerSpawner : MonoBehaviour
    {
        public static readonly List<CustomerSpawner> All = new List<CustomerSpawner>();

        [SerializeField] CustomerAI customerPrefab;
        [SerializeField] Vector2 spawnInterval = new Vector2(15f, 30f);
        [SerializeField] int maxCustomers = 4;
        [SerializeField] string[] customerNames =
            { "Alex", "Sam", "Jordan", "Taylor", "Chris", "Morgan", "Riley", "Jamie", "Casey", "Avery", "Drew", "Robin" };

        public DealershipManager Dealer { get; private set; }
        readonly List<CustomerAI> active = new List<CustomerAI>();
        Coroutine loop;
        bool warnedNoCars;

        void Awake()
        {
            Dealer = GetComponent<DealershipManager>();
            if (!All.Contains(this)) All.Add(this);
            if (Dealer.isMain || !CarSalesServices.Spawner) CarSalesServices.Spawner = this;
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            bool open = Dealer.IsOpenNow;
            if (open && loop == null) loop = StartCoroutine(SpawnLoop());
            else if (!open && loop != null) StopSpawning(true);
        }

        public void StartSpawning() { if (loop == null) loop = StartCoroutine(SpawnLoop()); }

        public void StopSpawning(bool sendEveryoneHome)
        {
            if (loop != null) { StopCoroutine(loop); loop = null; }
            if (sendEveryoneHome) foreach (var c in active.ToArray()) if (c) c.Leave();
        }

        public static void StopAll(bool sendHome) { foreach (var s in All) s.StopSpawning(sendHome); }

        public void Unregister(CustomerAI c) => active.Remove(c);

        /// <summary>Phase 8: a passer-by walks in (pedestrians who turn into customers).</summary>
        public void SpawnWalkIn() { if (Dealer.IsOpenNow) TrySpawn(); }

        IEnumerator SpawnLoop()
        {
            yield return new WaitForSeconds(2f);
            while (true)
            {
                TrySpawn();
                float mult = CarSalesServices.Upgrades ? CarSalesServices.Upgrades.SpawnIntervalMultiplier : 1f;
                if (CarSalesServices.Staff && CarSalesServices.Staff.Working(CarSales.Sim.StaffRole.Receptionist).Any()) mult *= 0.8f;
                if (CarSalesServices.World) mult /= Mathf.Max(0.2f, CarSalesServices.World.Footfall);   // phase 8: weather, weekends, holidays, events
                if (CarSalesServices.Business) mult /= Mathf.Max(0.2f, CarSalesServices.Business.FootfallFactor);   // phase 9: marketing, reviews
                if (CarSalesServices.Business) mult /= Mathf.Max(0.2f, CarSalesServices.Business.Balance.footfallMultiplier);   // phase 10: difficulty
                yield return new WaitForSeconds(Random.Range(spawnInterval.x, spawnInterval.y) * mult);
            }
        }

        void TrySpawn()
        {
            active.RemoveAll(c => c == null);
            int max = maxCustomers + (CarSalesServices.Upgrades ? CarSalesServices.Upgrades.ExtraCustomers : 0);
            if (active.Count >= max) return;
            if (Dealer.GetListedCars().Count == 0)
            {
                if (!warnedNoCars && Dealer.isMain)
                {
                    warnedNoCars = true;
                    CarSalesServices.UI.Notify($"No cars listed at {Dealer.displayName} - customers won't come!");
                }
                return;
            }
            warnedNoCars = false;
            var points = Dealer.customerSpawnPoints;
            if (points == null || points.Length == 0) return;

            var point = points[Random.Range(0, points.Length)];
            if (!NavMesh.SamplePosition(point.position, out var hit, 3f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[CarSales] Customer spawn point at {Dealer.displayName} is not on the NavMesh.");
                return;
            }
            var customer = Instantiate(customerPrefab, hit.position, point.rotation);
            var look = AssetRegistry.RandomCustomerModel();
            if (look) customer.UseAppearance(look);
            var profile = GenerateProfile();
            // Phase 7b: someone who booked a viewing from your online advert.
            var lead = CarSalesServices.Sourcing && CarSalesServices.DayCycle ? CarSalesServices.Sourcing.TakeLead(Dealer, CarSalesServices.DayCycle.HourOfDay) : null;
            if (lead != null)
            {
                profile.customerName = lead.name;
                profile.persona = lead.persona;
                profile.targetCarId = lead.carId;
                var car = CarSalesServices.Inventory.OwnedCars.Find(c => c.instanceId == lead.carId);
                if (car != null) profile.budget = Mathf.Max(profile.budget, Mathf.RoundToInt(car.listedPrice * 1.05f));
            }
            customer.Init(profile, this);
            active.Add(customer);
            CarSalesServices.PlaySfx(Sfx.DoorChime, 0.5f);
        }

        static readonly System.Random personaRng = new System.Random();

        /// <summary>Phase 7: a customer is one of nine buyer types (budget, tastes, haggling, patience, finance, trade-in).</summary>
        CustomerProfile GenerateProfile()
        {
            float rep = CarSalesServices.Economy.Reputation / 100f;
            float budgetMult = (CarSalesServices.Upgrades ? CarSalesServices.Upgrades.BudgetMultiplier : 1f) * Dealer.budgetMultiplier;
            var persona = CarSales.Sim.Buyers.Pick(personaRng, Dealer.budgetMultiplier);
            var biz = CarSalesServices.Business;
            if (biz)
            {
                var pick = biz.CampaignPersona(personaRng);   // phase 9: campaigns bring their kind of buyer
                if (pick.HasValue) persona = CarSales.Sim.Buyers.Get(pick.Value);
                // Luxury buyers only visit premium dealers (level 5+) or a premium site.
                for (int tries = 0; tries < 5 && persona.Id == CarSales.Sim.PersonaId.LuxuryBuyer && !CarSales.Sim.BusinessRules.LuxuryBuyers(biz.Level, Dealer.budgetMultiplier > 1.2f); tries++)
                    persona = CarSales.Sim.Buyers.Pick(personaRng, Dealer.budgetMultiplier);
            }
            int best = 0;
            for (int i = 1; i < persona.CategoryLike.Length; i++) if (persona.CategoryLike[i] > persona.CategoryLike[best]) best = i;
            var p = new CustomerProfile
            {
                customerName = customerNames[Random.Range(0, customerNames.Length)],
                budget = Mathf.RoundToInt(Random.Range(persona.BudgetMin, persona.BudgetMax) * (0.8f + rep * 0.4f) * budgetMult * (CarSalesServices.Business ? CarSalesServices.Business.Balance.budgetMultiplier : 1f)),
                preferredCategory = (CarCategory)best,
                patience = persona.Patience,
                greed = persona.Haggling,
                minCondition = 30f + persona.WCondition * 120f,
                persona = (int)persona.Id,
                wantsFinance = Random.value < persona.FinanceChance,
                creditScore = Random.Range(420, 840),
            };
            // A happy customer from the records comes back now and then.
            var back = CarSalesServices.Crm ? CarSalesServices.Crm.PickReturning(personaRng) : null;
            if (back != null) { p.customerName = back.name; p.recordId = back.id; p.persona = back.persona; p.budget = Mathf.RoundToInt(p.budget * 1.2f); }
            if (Random.value < persona.TradeInChance)
            {
                p.tradeIn = CustomerDeal.MakeTradeIn(p.budget);
                if (p.tradeIn != null)
                    p.tradeInWants = CarSales.Sim.Buyers.TradeInExpectation(Mathf.RoundToInt(CarValueCalculator.MarketValue(p.tradeIn) * 0.75f), personaRng);
            }
            if (CarSalesServices.Crm) CarSalesServices.Crm.RecordVisit(p);
            return p;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
    }
}
