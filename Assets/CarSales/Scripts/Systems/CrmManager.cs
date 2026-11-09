using System;
using System.Collections.Generic;
using System.Linq;
using CarSales.Foundation;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    [Serializable]
    public class CustomerRecord
    {
        public string id, name;
        public int persona;
        public int visits, purchases, spent, lastDay;
        public float satisfaction = -1f;   // average stars, -1 = none yet
        public string lastCar = "";
    }

    [Serializable]
    public class CustomerReview
    {
        public int day, stars;
        public string name, car, text;
    }

    [Serializable]
    public class PendingComplaint
    {
        public int day;
        public string customer, car;
        public int faults;
    }

    [Serializable]
    public class CrmState
    {
        public int version = 1;
        public List<CustomerRecord> customers = new List<CustomerRecord>();
        public List<CustomerReview> reviews = new List<CustomerReview>();
        public List<PendingComplaint> complaints = new List<PendingComplaint>();
    }

    /// <summary>
    /// Customer records, reviews and complaints (spec §6.10 CRM / after-sale). Every visit is recorded; every sale gets
    /// a review that moves your reputation; selling a car with faults you knew about and didn't mention brings a
    /// complaint a few days later. Saved as module "crm".
    /// </summary>
    public class CrmManager : MonoBehaviour
    {
        CrmState state = new CrmState();
        public CrmState State => state;
        public event Action OnChanged;

        void Awake() => CarSalesServices.Crm = this;
        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        public void SetState(CrmState s) { state = s ?? new CrmState(); OnChanged?.Invoke(); }
        public void ResetState() => SetState(new CrmState());

        public float AverageStars => state.reviews.Count == 0 ? 0f : (float)state.reviews.Average(r => r.stars);

        CustomerRecord Record(CustomerProfile p)
        {
            if (string.IsNullOrEmpty(p.recordId)) p.recordId = Guid.NewGuid().ToString("N").Substring(0, 10);
            var r = state.customers.Find(c => c.id == p.recordId);
            if (r == null)
            {
                r = new CustomerRecord { id = p.recordId, name = p.customerName, persona = p.persona };
                state.customers.Insert(0, r);
                while (state.customers.Count > 200) state.customers.RemoveAt(state.customers.Count - 1);
            }
            return r;
        }

        /// <summary>A returning customer from the records (repeat business), or null.</summary>
        public CustomerRecord PickReturning(System.Random rng)
        {
            var happy = state.customers.Where(c => c.purchases > 0 && c.satisfaction >= 4f && CarRecords.Today - c.lastDay >= 5).ToList();
            return happy.Count > 0 && rng.NextDouble() < 0.25 ? happy[rng.Next(happy.Count)] : null;
        }

        public void RecordVisit(CustomerProfile p)
        {
            var r = Record(p);
            r.visits++;
            r.lastDay = CarRecords.Today;
            OnChanged?.Invoke();
        }

        /// <summary>After a sale: the customer's review (stars → reputation) and, if you hid known faults, a complaint later.</summary>
        public int RecordSale(CustomerProfile p, CarInstance car, int price, int marketValue, int disclosed, int hiddenKnown, int rounds, string soldBy, float bonus = 0f)
        {
            var r = Record(p);
            var persona = Buyers.Get((PersonaId)p.persona);
            int stars = Mathf.Clamp(Mathf.RoundToInt(Buyers.ReviewStars(persona, car.condition, price / (float)Mathf.Max(1, marketValue), disclosed, hiddenKnown, rounds) + bonus), 1, 5);
            r.purchases++;
            r.spent += price;
            r.lastCar = car.Name;
            r.satisfaction = r.satisfaction < 0f ? stars : (r.satisfaction * (r.purchases - 1) + stars) / r.purchases;
            string text = stars >= 5 ? "Fantastic service, honest and fair. Highly recommended!" :
                          stars == 4 ? "Good car, good price. Would buy here again." :
                          stars == 3 ? "It was OK. A bit of haggling but fine." :
                          stars == 2 ? "Not great. I expected more for the money." : "Avoid. They weren't honest with me.";
            if (!string.IsNullOrEmpty(soldBy)) text += $" ({soldBy} looked after me.)";
            state.reviews.Insert(0, new CustomerReview { day = CarRecords.Today, stars = stars, name = p.customerName, car = car.Name, text = text });
            while (state.reviews.Count > 100) state.reviews.RemoveAt(state.reviews.Count - 1);
            CarSalesServices.Economy.ChangeReputation(Buyers.ReputationFromStars(stars));
            if (hiddenKnown > 0)
                state.complaints.Add(new PendingComplaint { day = CarRecords.Today + UnityEngine.Random.Range(2, 7), customer = p.customerName, car = car.Name, faults = hiddenKnown });
            OnChanged?.Invoke();
            return stars;
        }

        void OnDayStarted(DayStarted e)
        {
            foreach (var c in state.complaints.Where(x => x.day <= e.Day).ToList())
            {
                state.complaints.Remove(c);
                float hit = 2f * c.faults;
                CarSalesServices.Economy.ChangeReputation(-hit);
                string msg = $"{c.customer} says the {c.car} has problems you knew about and didn't mention. They're telling everyone (-{hit:0} reputation).";
                if (CarSalesServices.Messages) CarSalesServices.Messages.Add(MessageType.Info, "Customer complaint", msg);
                CarSalesServices.UI?.Toast(ToastType.Warning, "Complaint", msg);
            }
            OnChanged?.Invoke();
        }
    }
}
