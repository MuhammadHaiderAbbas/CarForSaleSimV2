using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Haggling (Negotiation 2.0): the buyer type sets the opening offer and the hidden limit; customers may notice
    /// faults you didn't mention; you can disclose what you know (they price it in, but review you better); trade-in
    /// and finance change the deal.
    /// </summary>
    public class NegotiationSession
    {
        public CustomerAI Customer { get; }
        public CarDisplay Car { get; }
        public int MarketValue { get; private set; }
        public int CustomerOffer { get; private set; }
        public int FinalPrice { get; private set; }
        public bool Disclosed { get; private set; }
        public int Rounds { get; private set; }
        public readonly List<string> Spotted = new List<string>();
        public bool IncludeTradeIn { get; private set; }
        public bool UseFinance { get; private set; }
        public bool FinanceApproved { get; private set; }
        public CarInstance TradeIn => Customer.Profile.tradeIn;
        public int TradeInCredit => Customer.Profile.tradeInWants;
        public BuyerPersona Persona { get; }

        int maxWillingToPay;
        readonly int startPatience;
        int patience;
        readonly HashSet<FaultInstance> spottedFaults = new HashSet<FaultInstance>();

        /// <summary>1 = calm, 0 = about to walk away.</summary>
        public float PatienceRatio => startPatience > 0 ? Mathf.Clamp01(patience / (float)startPatience) : 0f;
        /// <summary>Known faults you didn't mention and they didn't notice.</summary>
        public int HiddenKnownFaults => (Disclosed ? 0 : CarRecords.KnownFaults(Car.Data).Count(f => !spottedFaults.Contains(f))) + lies;
        public int DisclosedFaults => Disclosed ? CarRecords.KnownFaults(Car.Data).Count() : 0;

        public NegotiationSession(CustomerAI customer, CarDisplay car)
        {
            Customer = customer;
            Car = car;
            Persona = CustomerDeal.Persona(customer.Profile);
            patience = Mathf.Max(1, customer.Profile.patience);
            startPatience = patience;
            UseFinance = customer.Profile.wantsFinance;
            Recalculate(firstTime: true);

            // They look the car over: a knowledgeable buyer notices faults you didn't mention.
            foreach (var f in car.Data.faults.Where(x => x.Def != null && x.severity > 0.25f))
            {
                if (Random.value >= Buyers.SpotChance(Persona, f.severity)) continue;
                spottedFaults.Add(f);
                f.known = true;
                Spotted.Add(f.Def.Name);
                maxWillingToPay -= CarRecords.RepairEstimate(car.Data, f);
            }
            if (Spotted.Count > 0) patience = Mathf.Max(1, patience - 1);
            maxWillingToPay = Mathf.Max(100, maxWillingToPay);
            CustomerOffer = Mathf.Min(CustomerOffer, maxWillingToPay);
        }

        void Recalculate(bool firstTime)
        {
            var q = CustomerDeal.Quote(Customer.Profile, Car, Disclosed);
            MarketValue = q.marketValue;
            int max = q.max;
            if (IncludeTradeIn) max = Mathf.RoundToInt(max * 1.03f);            // motivated: their old car is sorted
            if (!UseFinance || (FinanceApproved == false && financeChecked)) max = Mathf.Min(max, Mathf.RoundToInt(Customer.Profile.budget * 1.05f));
            foreach (var f in spottedFaults) if (!Disclosed) max -= CarRecords.RepairEstimate(Car.Data, f);
            maxWillingToPay = Mathf.Max(100, max);
            if (firstTime) CustomerOffer = Mathf.Min(maxWillingToPay, q.opening);
            else CustomerOffer = Mathf.Min(CustomerOffer, maxWillingToPay);
        }

        bool financeChecked;

        /// <summary>Tell them about every fault you know. Their limit drops by the repair cost, but they trust you.</summary>
        public void Disclose()
        {
            if (Disclosed) return;
            Disclosed = true;
            patience += 1;
            Recalculate(false);
        }

        public void SetTradeIn(bool on) { if (TradeIn == null) return; IncludeTradeIn = on; Recalculate(false); }
        public void SetFinance(bool on) { UseFinance = on; Recalculate(false); }

        /// <summary>Asks the lender (once). Declined: the customer can only pay from their own budget.</summary>
        public bool CheckFinance(int price, out string message)
        {
            if (!UseFinance) { message = ""; return true; }
            if (!financeChecked)
            {
                financeChecked = true;
                float deposit = Mathf.Clamp01(Customer.Profile.budget * 0.15f / Mathf.Max(1f, price));
                FinanceApproved = Random.value < Buyers.FinanceApproval(Customer.Profile.creditScore, deposit);
                if (!FinanceApproved) Recalculate(false);
            }
            message = FinanceApproved ? "The finance is approved." : "The lender turned their finance down.";
            return FinanceApproved || price <= maxWillingToPay;
        }

        public void AcceptCustomerOffer() => FinalPrice = CustomerOffer;

        // ------------------------------------------------------------------ Q&A (phase 7b)
        int lies;
        float trustFactor = 1f;
        public bool TestDriven { get; private set; }
        public int Lies => lies;

        /// <summary>True when the honest answer to this question is bad news for the sale.</summary>
        public bool TruthBad(BuyerQuestion q)
        {
            var c = Car.Data;
            switch (q)
            {
                case BuyerQuestion.AnyProblems: return CarRecords.KnownFaults(c).Any() && !Disclosed;
                case BuyerQuestion.ServiceHistory: return c.documents.service != ServiceHistory.Full;
                case BuyerQuestion.Mot: return !CarRecords.MotValid(c);
                case BuyerQuestion.Mileage: return c.history.clockingConfirmed || c.history.clockingSuspected;
                default: return c.history.checkedHistory && c.history.writeOff != WriteOff.None;
            }
        }

        /// <summary>You answer a customer's question honestly or dodge it.</summary>
        public void Answer(BuyerQuestion q, bool honest)
        {
            var (factor, pat, lie) = BuyerQuestions.Effect(honest, TruthBad(q));
            if (honest && q == BuyerQuestion.AnyProblems && TruthBad(q)) { Disclose(); return; }
            trustFactor *= factor;
            patience += pat;
            if (lie) lies++;
            maxWillingToPay = Mathf.Max(100, Mathf.RoundToInt(maxWillingToPay * factor));
            CustomerOffer = Mathf.Min(CustomerOffer, maxWillingToPay);
        }

        /// <summary>
        /// Customer test drive (alone or with you riding along): they may notice faults that show on the road; if not,
        /// they like the car a bit more. Riding along, you notice what they notice.
        /// </summary>
        public string TestDrive(bool rideAlong)
        {
            if (TestDriven) return "They've already driven it.";
            TestDriven = true;
            var c = Car.Data;
            c.mileage += 4;
            c.fuel = Mathf.Max(0f, c.fuel - 2f);
            var noticed = new List<string>();
            foreach (var f in c.faults.Where(x => x.Def != null && (x.Def.FoundBy & InspectMethod.RoadTest) != 0 && !spottedFaults.Contains(x)))
            {
                if (Random.value >= Buyers.SpotChance(Persona, f.severity) * 1.5f) continue;
                spottedFaults.Add(f);
                noticed.Add(f.Def.Symptom.ToLowerInvariant());
                if (rideAlong) { f.known = true; f.foundBy = (int)InspectMethod.RoadTest; }
                if (!Disclosed) maxWillingToPay -= CarRecords.RepairEstimate(c, f);
            }
            if (noticed.Count == 0) { maxWillingToPay = Mathf.RoundToInt(maxWillingToPay * 1.03f); patience += 1; }
            if (rideAlong) patience += 1;
            maxWillingToPay = Mathf.Max(100, maxWillingToPay);
            CustomerOffer = Mathf.Min(CustomerOffer, maxWillingToPay);
            return noticed.Count == 0 ? "Lovely drive. I can see myself in this." : $"Hmm. I noticed {string.Join(" and ", noticed)}.";
        }

        public NegotiationResult Propose(int playerOffer, out string reply)
        {
            Rounds++;
            if (playerOffer <= CustomerOffer)
            {
                FinalPrice = playerOffer;
                reply = "Deal! Pleasure doing business.";
                return NegotiationResult.Accepted;
            }
            float greed = Persona.Haggling;
            if (playerOffer <= maxWillingToPay)
            {
                float gap = maxWillingToPay - CustomerOffer + 1f;
                float acceptChance = 1f - (playerOffer - CustomerOffer) / gap;
                if (Random.value < acceptChance * (1f - greed * 0.3f))
                {
                    FinalPrice = playerOffer;
                    reply = "Alright... you've got a deal.";
                    return NegotiationResult.Accepted;
                }
                patience -= 1;
                CustomerOffer += Mathf.RoundToInt((playerOffer - CustomerOffer) * Mathf.Lerp(0.6f, 0.25f, greed));
                CustomerOffer = Mathf.Min(CustomerOffer, maxWillingToPay);
                reply = $"Hmm, how about ${CustomerOffer:N0}?";
            }
            else
            {
                patience -= 2;
                CustomerOffer += Mathf.RoundToInt((maxWillingToPay - CustomerOffer) * 0.15f);
                reply = Spotted.Count > 0 && !Disclosed ? $"Not with that {Spotted[0].ToLowerInvariant()}. I could do ${CustomerOffer:N0}." : $"That's way too much! I could do ${CustomerOffer:N0}.";
            }

            if (patience <= 0)
            {
                reply = "Forget it, I'm going elsewhere.";
                return NegotiationResult.WalkedAway;
            }
            return NegotiationResult.Countered;
        }
    }
}
