namespace CarSales.Foundation
{
    public enum DealRating { GreatDeal, GoodPrice, Fair, Overpriced, Unknown }

    /// <summary>How an asking price compares with the estimated market value (marketplace badges, vehicle file).</summary>
    public static class PriceRating
    {
        public static DealRating Rate(int price, int estimate)
        {
            if (price <= 0 || estimate <= 0) return DealRating.Unknown;
            float r = (float)price / estimate;
            if (r < 0.85f) return DealRating.GreatDeal;
            if (r < 0.97f) return DealRating.GoodPrice;
            if (r <= 1.06f) return DealRating.Fair;
            return DealRating.Overpriced;
        }

        public static string Label(DealRating r)
        {
            switch (r)
            {
                case DealRating.GreatDeal: return "Great deal";
                case DealRating.GoodPrice: return "Good price";
                case DealRating.Fair: return "Fair price";
                case DealRating.Overpriced: return "Overpriced";
                default: return "No estimate";
            }
        }

        /// <summary>Selling price at which a car returns what was spent on it (purchase + repairs + other costs).</summary>
        public static int BreakEven(int purchase, int repairs, int otherCosts) => purchase + repairs + otherCosts;
    }
}
