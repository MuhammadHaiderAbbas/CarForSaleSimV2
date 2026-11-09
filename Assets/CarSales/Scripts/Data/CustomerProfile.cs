using System;

namespace CarSales
{
    [Serializable]
    public class CustomerProfile
    {
        public string customerName;
        public int budget;
        public CarCategory preferredCategory;
        public int patience;        // counter-offers tolerated (1-4)
        public float greed;         // 0 = easy buyer, 1 = hard haggler
        public float minCondition;  // won't want cars below this condition
        // Phase 7
        public int persona;          // CarSales.Sim.PersonaId
        public string recordId;      // CRM record
        public bool wantsFinance;
        public int creditScore = 650;
        public CarInstance tradeIn;  // null = no trade-in
        public int tradeInWants;     // what they think it's worth
        public string targetCarId;   // phase 7b: came about an online advert for this car
    }
}
