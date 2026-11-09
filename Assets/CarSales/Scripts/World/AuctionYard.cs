using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Numbered bays outside the hall: the lots of today's and tomorrow's hall sales stand here for viewing.
    /// Walk up to one and press E to read the condition report, inspect it (no road test) or leave a bid.
    /// </summary>
    public class AuctionYard : MonoBehaviour
    {
        public Transform[] bays;
        [SerializeField] CarDisplay displayPrefab;
        readonly List<CarDisplay> shown = new List<CarDisplay>();
        string signature = "";

        void Start()
        {
            if (CarSalesServices.Auction) CarSalesServices.Auction.OnChanged += Rebuild;
            Rebuild();
        }

        void OnDestroy() { if (CarSalesServices.Auction) CarSalesServices.Auction.OnChanged -= Rebuild; }

        public void Rebuild()
        {
            var a = CarSalesServices.Auction;
            if (!a || bays == null || !displayPrefab) return;
            var lots = a.YardLots().Take(bays.Length).ToList();
            string sig = string.Join(",", lots.Select(l => l.id));
            if (sig == signature) return;
            signature = sig;
            foreach (var d in shown) if (d) Destroy(d.gameObject);
            shown.Clear();
            for (int i = 0; i < lots.Count; i++)
            {
                var car = a.CarOf(lots[i]);
                if (car == null) continue;
                var d = Instantiate(displayPrefab, bays[i].position, bays[i].rotation);
                d.InitializeForAuction(car, lots[i].id, lots[i].lotNo);
                shown.Add(d);
            }
        }
    }
}
