using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Phase 7 selling maths for one customer and one car (used by the negotiation screen and by salespeople):
    /// what the car is worth to this kind of buyer, their opening offer and their limit, and finishing the sale
    /// (trade-in, finance commission, review, market demand).
    /// </summary>
    public static class CustomerDeal
    {
        public struct DealQuote { public int opening, max, marketValue; public float score; }

        public static BuyerPersona Persona(CustomerProfile p) => Buyers.Get((PersonaId)Mathf.Clamp(p.persona, 0, Buyers.All.Length - 1));

        public static CarFacts Facts(CarDisplay d, bool countKnownFaults = true)
        {
            var c = d.Data;
            var def = d.Definition;
            string fuel = def ? (def.fuelType ?? "").ToLowerInvariant() : "";
            return new CarFacts
            {
                price = c.listedPrice, marketValue = CarValueCalculator.MarketValue(c), condition = c.condition, cleanliness = c.cleanliness,
                mileage = c.mileage, ageYears = VehicleRecords.AgeYears(def ? def.year : 2015, CarRecords.Today),
                category = def ? (MarketCategory)(int)def.category : MarketCategory.Sedan,
                economical = fuel.Contains("electric") || fuel.Contains("hybrid") || (def && def.category == CarCategory.Hatchback),
                fullHistory = c.documents != null && c.documents.service == ServiceHistory.Full,
                motValid = CarRecords.MotValid(c), v5cPresent = c.documents == null || c.documents.v5cPresent,
                knownFaults = countKnownFaults ? InspectionEngine.KnownCount(c.faults) : 0,
            };
        }

        public static float Score(CustomerProfile p, CarDisplay d) => Buyers.Score(Persona(p), Facts(d), p.budget);

        /// <summary>
        /// disclose = you told them about the faults you know: they price those in (but trust you). Not disclosed, they
        /// only price in what they notice themselves (the session handles that).
        /// </summary>
        public static DealQuote Quote(CustomerProfile p, CarDisplay d, bool disclose)
        {
            var persona = Persona(p);
            var c = d.Data;
            int value = CarValueCalculator.MarketValue(c);
            float score = Buyers.Score(persona, Facts(d, countKnownFaults: disclose), p.budget);
            int max = Buyers.MaxPrice(persona, value, score, p.budget, p.wantsFinance);
            if (disclose)
                max -= Mathf.RoundToInt(CarRecords.KnownFaults(c).Sum(f => CarRecords.RepairEstimate(c, f)) * 0.6f);
            if (CarSalesServices.Crm && CarSalesServices.Crm.AverageStars >= 4.2f) max = Mathf.RoundToInt(max * 1.03f);   // good reviews
            max = Mathf.Max(100, max);
            return new DealQuote { opening = Mathf.Min(max, Buyers.OpeningOffer(persona, max)), max = max, marketValue = value, score = score };
        }

        /// <summary>Finishes a sale: money in, the trade-in onto your lot, finance commission, review, market demand.</summary>
        public static void CompleteSale(CustomerAI customer, int price, int marketValue, int disclosed, int hiddenKnown, int rounds, string soldBy,
                                        CarInstance tradeIn, int tradeInCredit, bool finance, float reviewBonus = 0f)
        {
            var display = customer.TargetCar;
            var car = display.Data;
            var def = display.Definition;
            if (CarSalesServices.Business) CarSalesServices.Business.SetSaleContext(customer.Profile.persona, customer.Profile.targetCarId == car.instanceId, soldBy);
            CarSalesServices.Vehicles.CompleteSale(display, price);
            if (tradeIn != null && tradeInCredit > 0)
            {
                CarSalesServices.Economy.ForceSpend(tradeInCredit, TxCategory.VehiclePurchase, $"Trade-in from {customer.Profile.customerName} - {tradeIn.Name}", tradeIn.instanceId);
                tradeIn.purchasePrice = tradeInCredit;
                tradeIn.inspected = true;
                tradeIn.state = CarValueCalculator.DefaultState(tradeIn);
                CarSalesServices.Vehicles.AddOwnedCar(tradeIn);
                CarSalesServices.UI.Notify($"Trade-in: the {tradeIn.Name} is on your lot (credited {UIFactory.Money(tradeInCredit)}).");
            }
            if (finance)
            {
                int commission = Buyers.FinanceCommission(price - (tradeIn != null ? tradeInCredit : 0));
                CarSalesServices.Economy.AddMoney(commission, TxCategory.FinanceCommission, $"Finance commission - {car.Name}", car.instanceId);
                CarSalesServices.UI.Notify($"Finance arranged: the lender paid in full, you earned {UIFactory.Money(commission)} commission.");
            }
            if (CarSalesServices.Market) CarSalesServices.Market.RegisterSale(def);
            int stars = CarSalesServices.Crm ? CarSalesServices.Crm.RecordSale(customer.Profile, car, price, marketValue, disclosed, hiddenKnown, rounds, soldBy, reviewBonus) : 3;
            CarSalesServices.UI.Toast(ToastType.Success, soldBy != null ? $"{soldBy} sold a car" : "Review",
                $"{customer.Profile.customerName} bought the {car.Name} for {UIFactory.Money(price)} and left a {stars}-star review.");
            customer.EndNegotiation(true);
        }

        /// <summary>A car the customer brings to trade in (a cheaper model than they're buying).</summary>
        public static CarInstance MakeTradeIn(int budget)
        {
            var db = CarSalesServices.Database;
            if (!db || db.cars.Count == 0) return null;
            var options = db.cars.Where(c => c && c.basePrice < budget * 0.9f).ToList();
            var def = options.Count > 0 ? options[Random.Range(0, options.Count)] : db.GetRandom();
            var car = CarValueCalculator.GenerateRandom(def);
            car.mileage = Random.Range(60000, 220000);
            CarParts.Randomize(car, Random.Range(30f, 75f));
            car.reportedCondition = car.condition;
            return car;
        }
    }
}
