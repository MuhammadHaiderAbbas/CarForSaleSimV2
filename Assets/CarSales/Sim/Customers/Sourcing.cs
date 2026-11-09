using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    // ------------------------------------------------------------------ PRIVATE SELLERS (§6.8)
    /// <summary>A classified ad: a private seller and their car (kept by the runtime by car id).</summary>
    [Serializable]
    public class PrivateAd
    {
        public string id;
        public string carId;            // CarInstance.instanceId
        public string seller;
        public float honesty;           // 0 = lies about everything, 1 = straight
        public int asking;
        public int minPrice;            // hidden
        public string claim = "";       // what the ad says
        public int postedDay, expiresDay;
        public int appointmentDay = -1; // booked viewing
        public float appointmentHour;
        public int spot = -1;           // SellerSpot index
        public bool sold;
        public int patience = 3;
    }

    public enum SellerQuestion { AnyProblems = 0, ServiceHistory = 1, WhySelling = 2, Mileage = 3, Finance = 4 }

    public static class PrivateSellers
    {
        static readonly string[] Names = { "Maria", "Tom", "Priya", "Ken", "Lucia", "Omar", "Grace", "Ivan", "Sofia", "Ben", "Aisha", "Pete" };

        public static string Name(Random rng) => Names[rng.Next(Names.Length)];

        /// <summary>Private sellers ask 10-25% under market; their real floor is 8-15% under the asking price.</summary>
        public static int Asking(int marketValue, Random rng) => (int)(marketValue * (0.75f + rng.NextDouble() * 0.15f) / 50f) * 50;
        public static int MinPrice(int asking, Random rng) => (int)(asking * (0.85f + rng.NextDouble() * 0.07f) / 50f) * 50;

        /// <summary>
        /// The seller's answer. Honest sellers tell the truth; dishonest ones play down faults and history.
        /// <paramref name="truthBad"/>: the true answer is bad news (faults, missing history, clocked, finance).
        /// Returns (answer text, isLie).
        /// </summary>
        public static (string text, bool lie) Answer(SellerQuestion q, bool truthBad, string truthDetail, float honesty, Random rng)
        {
            bool lie = truthBad && rng.NextDouble() > honesty;
            switch (q)
            {
                case SellerQuestion.AnyProblems:
                    return truthBad && !lie ? ($"To be honest: {truthDetail}.", false) : ("No, it's been perfect. Never let me down.", lie);
                case SellerQuestion.ServiceHistory:
                    return truthBad && !lie ? ("There's not much history, I'm afraid.", false) : ("Serviced every year, all stamped.", lie);
                case SellerQuestion.Mileage:
                    return truthBad && !lie ? ("I'm not 100% sure the mileage is right - I bought it like this.", false) : ("Genuine miles, I'm the only driver.", lie);
                case SellerQuestion.Finance:
                    return truthBad && !lie ? ("There's a bit of finance left, I'll pay it off from the sale.", false) : ("No finance, it's all mine.", lie);
                default:
                    return (rng.NextDouble() < 0.5 ? "Getting a bigger car for the family." : "Moving abroad, need it gone this week.", false);
            }
        }

        /// <summary>Private haggling: they come down part of the way each round, never under their floor.</summary>
        public static (bool accepted, int counter) Haggle(int offer, int current, int minPrice, Random rng)
        {
            if (offer >= current) return (true, offer);
            if (offer >= minPrice && rng.NextDouble() < 0.5 + (offer - minPrice) / (double)Math.Max(1, current - minPrice) * 0.5) return (true, offer);
            int counter = Math.Max(minPrice, (int)((current + Math.Max(offer, minPrice)) / 2f / 50f) * 50);
            return (false, counter);
        }

        /// <summary>A quick test drive around the block with the seller: like a road test, a bit less thorough.</summary>
        public const float AroundTheBlockSkill = 0.6f;
    }

    // ------------------------------------------------------------------ LISTINGS & LEADS (§6.10)
    public enum Highlight { FullServiceHistory = 0, NewMot = 1, LowMileage = 2, OneOwner = 3, Valeted = 4, Warranty = 5, FaultsListed = 6 }

    [Serializable]
    public class ListingInfo
    {
        public bool online;
        public int photos;                         // 0-6 angles taken
        public List<int> highlights = new List<int>();
        public string headline = "";
        public int postedDay;
        public int leads;
    }

    public static class Listings
    {
        public const int MaxPhotos = 6;

        /// <summary>Listing quality 0-1: photos, honest highlights, a fair price, good reviews.</summary>
        public static float Quality(int photos, int highlights, bool faultsListed, float priceVsMarket, float avgStars)
        {
            float q = Math.Min(photos, MaxPhotos) / (float)MaxPhotos * 0.35f;
            q += Math.Min(highlights, 4) / 4f * 0.2f;
            q += faultsListed ? 0.05f : 0f;
            q += Math.Max(0f, Math.Min(1f, 1.3f - priceVsMarket)) * 0.3f;   // full marks at 30% under, none at 30% over
            q += avgStars > 0f ? (avgStars - 1f) / 4f * 0.1f : 0.05f;
            return Math.Max(0f, Math.Min(1f, q));
        }

        /// <summary>Expected enquiries per day for an online listing (receptionist answers more of them).</summary>
        public static float LeadsPerDay(float quality, bool receptionist) => (0.15f + quality * 1.1f) * (receptionist ? 1.3f : 1f);

        /// <summary>A lead who booked a viewing comes with a head start: they already like this car.</summary>
        public const float LeadInterestBonus = 0.25f;

        public static string Headline(string carName, int year, int mileage, IList<int> highlights)
        {
            var parts = new List<string> { $"{year} {carName}", $"{mileage / 1000}k km" };
            foreach (var h in highlights)
                switch ((Highlight)h)
                {
                    case Highlight.FullServiceHistory: parts.Add("FSH"); break;
                    case Highlight.NewMot: parts.Add("long MOT"); break;
                    case Highlight.LowMileage: parts.Add("low miles"); break;
                    case Highlight.OneOwner: parts.Add("1 owner"); break;
                    case Highlight.Valeted: parts.Add("fully valeted"); break;
                    case Highlight.Warranty: parts.Add("3-month warranty"); break;
                }
            return string.Join(" - ", parts);
        }

        public static string HighlightName(Highlight h) =>
            h == Highlight.FullServiceHistory ? "Full service history" : h == Highlight.NewMot ? "Long MOT" : h == Highlight.LowMileage ? "Low mileage" :
            h == Highlight.OneOwner ? "One owner" : h == Highlight.Valeted ? "Fully valeted" : h == Highlight.Warranty ? "Warranty available" : "Known faults listed";
    }

    // ------------------------------------------------------------------ CUSTOMER Q&A
    public enum BuyerQuestion { AnyProblems = 0, ServiceHistory = 1, Mot = 2, Mileage = 3, Accidents = 4 }

    public static class BuyerQuestions
    {
        public static string Text(BuyerQuestion q) =>
            q == BuyerQuestion.AnyProblems ? "Are there any problems with it I should know about?" :
            q == BuyerQuestion.ServiceHistory ? "Has it got a service history?" :
            q == BuyerQuestion.Mot ? "How long is left on the MOT?" :
            q == BuyerQuestion.Mileage ? "Is the mileage genuine?" : "Has it been in an accident?";

        /// <summary>
        /// Effect of your answer on the customer's limit (share of price) and patience. Honest bad news costs a little;
        /// a lie (when you know the truth is bad) costs nothing now but counts as a hidden fault for the review.
        /// </summary>
        public static (float priceFactor, int patience, bool lie) Effect(bool honest, bool truthBad)
        {
            if (!truthBad) return honest ? (1.01f, 1, false) : (0.99f, -1, false);   // dodging a harmless question looks shifty
            return honest ? (0.97f, 1, false) : (1f, 0, true);
        }
    }

    // ------------------------------------------------------------------ WARRANTY & PAPERWORK
    public static class AfterSale
    {
        public const int WarrantyDays = 90;

        /// <summary>3-month warranty price: 4% of the sale price, $150-$900.</summary>
        public static int WarrantyPrice(int salePrice) => Math.Max(150, Math.Min(900, (int)Math.Round(salePrice * 0.04 / 10.0) * 10));

        /// <summary>Chance per day that a warranty claim comes in, from the faults the car really has.</summary>
        public static float ClaimChancePerDay(IList<float> faultSeverities)
        {
            float s = 0f;
            foreach (var v in faultSeverities) s += v;
            return Math.Min(0.25f, s * 0.02f);
        }

        /// <summary>Paperwork score 0-1 (logbook transferred, service records, keys, invoice) → review bonus up to +0.5 stars.</summary>
        public static float Paperwork(bool v5c, bool serviceRecords, int keys, bool invoice) =>
            (v5c ? 0.4f : 0f) + (serviceRecords ? 0.2f : 0f) + (keys >= 2 ? 0.2f : keys == 1 ? 0.1f : 0f) + (invoice ? 0.2f : 0f);

        public static float ReviewBonus(float paperwork) => (paperwork - 0.6f) * 1.25f;
    }
}
