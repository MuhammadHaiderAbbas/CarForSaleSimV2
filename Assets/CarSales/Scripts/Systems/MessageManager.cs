using CarSales.Foundation;
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CarSales
{
    /// <summary>Inbox: private-seller offers (cheap, honest cars that expire), tips, bank reminders, goals.</summary>
    public class MessageManager : MonoBehaviour
    {
        [SerializeField] int maxMessages = 40;
        [SerializeField] Vector2Int offersPerDay = new Vector2Int(1, 2);
        [SerializeField] Vector2 offerPriceVsValue = new Vector2(0.6f, 0.8f);

        static readonly string[] SellerNames = { "Maria", "Tom", "Priya", "Ken", "Lucia", "Omar", "Grace", "Ivan", "Sofia", "Ben" };
        static readonly string[] Tips =
        {
            "Clean cars sell faster. Dirty cars scare customers away.",
            "Customers with a matching favourite category are much more likely to buy.",
            "Pricing at 100-115% of market value is the sweet spot for most customers.",
            "The Mechanic Workshop upgrade reveals lying sellers in the marketplace.",
            "Ignored customers hurt your reputation. Talk to anyone with a '!' above their head.",
            "Quick Sell gives instant cash (75% of value) - useful when you're short on money.",
            "Loans charge interest every night. Repay them as soon as you can.",
            "A full service is 10% cheaper than repairing every part one by one.",
        };

        public List<GameMessage> Messages { get; private set; } = new List<GameMessage>();
        public event Action OnMessagesChanged;
        public int UnreadCount => Messages.FindAll(m => !m.read).Count;

        void Awake() => CarSalesServices.Messages = this;

        public GameMessage Add(MessageType type, string title, string body)
        {
            var m = new GameMessage
            {
                id = Guid.NewGuid().ToString(),
                type = type,
                title = title,
                body = body,
                day = CarSalesServices.DayCycle ? CarSalesServices.DayCycle.CurrentDay : 1,
                offerCar = new CarInstance()
            };
            Messages.Insert(0, m);
            while (Messages.Count > maxMessages) Messages.RemoveAt(Messages.Count - 1);
            OnMessagesChanged?.Invoke();
            return m;
        }

        public void MarkAllRead()
        {
            foreach (var m in Messages) m.read = true;
            OnMessagesChanged?.Invoke();
        }

        /// <summary>Called at the start of every day (and on a new game).</summary>
        public void GenerateDaily(int day)
        {
            foreach (var m in Messages)
                if (m.type == MessageType.SellerOffer && !m.resolved && m.expiresDay < day)
                {
                    m.resolved = true;
                    m.body += "\n<i>(Offer expired)</i>";
                }

            int offers = Random.Range(offersPerDay.x, offersPerDay.y + 1);
            var db = CarSalesServices.Database;
            for (int i = 0; i < offers && db && db.cars.Count > 0; i++)
            {
                var def = db.GetRandom();
                var car = CarValueCalculator.GenerateRandom(def);
                car.reportedCondition = car.condition;   // private sellers are honest
                car.inspected = true;
                int price = Mathf.RoundToInt(CarValueCalculator.MarketValue(car) * Random.Range(offerPriceVsValue.x, offerPriceVsValue.y) / 100f) * 100;
                string seller = SellerNames[Random.Range(0, SellerNames.Length)];
                var m = Add(MessageType.SellerOffer, $"{seller} wants to sell a {def.year} {def.brand} {def.displayName}",
                    $"Hi! I'm moving abroad and need to sell my car quickly.\n" +
                    $"{car.mileage:N0} km, condition {car.condition:0}% ({CarParts.Label(car.condition)}). " +
                    $"I'd take <b>${price:N0}</b>. Offer valid today only!");
                m.offerCar = car;
                m.offerPrice = price;
                m.expiresDay = day;
            }

            if (Random.value < 0.6f)
                Add(MessageType.Info, "Dealer tip", Tips[Random.Range(0, Tips.Length)]);

            var bank = CarSalesServices.Bank;
            if (bank && bank.Loan > 0)
                Add(MessageType.Bank, "Bank reminder",
                    $"Your loan balance is ${bank.Loan:N0}. Tonight's interest will be ${bank.TomorrowInterest:N0}.");
        }

        public bool AcceptOffer(GameMessage m, out string message)
        {
            message = "";
            if (m == null || m.type != MessageType.SellerOffer || m.resolved) { message = "This offer is no longer available."; return false; }
            if (DealershipManager.FindFreeSpotAnywhere(out _) == null) { message = "No free parking spot at your dealerships! Sell a car, expand, or buy another dealership."; return false; }
            if (!CarSalesServices.Economy.TrySpend(m.offerPrice, TxCategory.VehiclePurchase, $"Private seller - {m.offerCar.Name}", m.offerCar.instanceId)) { message = "Not enough money!"; return false; }

            var car = m.offerCar;
            car.purchasePrice = m.offerPrice;
            car.state = CarValueCalculator.DefaultState(car);
            CarSalesServices.Vehicles.AddOwnedCar(car);
            CarSalesServices.DayCycle.RegisterPurchase(m.offerPrice);
            if (CarSalesServices.Progress) CarSalesServices.Progress.RegisterPurchase();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            m.resolved = true;
            m.read = true;
            m.body += "\n<color=#6f6><i>(You bought this car)</i></color>";
            OnMessagesChanged?.Invoke();
            message = "Bought from a private seller.";
            return true;
        }

        public void Decline(GameMessage m)
        {
            if (m == null || m.resolved) return;
            m.resolved = true;
            m.read = true;
            m.body += "\n<i>(Declined)</i>";
            OnMessagesChanged?.Invoke();
        }

        public void SetMessages(List<GameMessage> saved)
        {
            Messages = saved ?? new List<GameMessage>();
            foreach (var m in Messages)
                if (m.offerCar != null && !string.IsNullOrEmpty(m.offerCar.carId)) CarParts.EnsureParts(m.offerCar);
            OnMessagesChanged?.Invoke();
        }
    }
}
