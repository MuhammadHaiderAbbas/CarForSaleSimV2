using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// The sale room of the Auction Center: while a sale runs and you stand inside, lots come onto the stage one by
    /// one and you bid by raising your paddle (E). The big screen shows the lot and the bid.
    /// </summary>
    public class AuctionHall : MonoBehaviour
    {
        public static AuctionHall Instance { get; private set; }
        public Vector3 center = new Vector3(0f, 4f, 9f);
        public Vector3 size = new Vector3(26f, 8f, 18f);
        public Transform stage;
        public TMP_Text screen;
        [SerializeField] CarDisplay displayPrefab;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        public bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world) - center;
            return Mathf.Abs(l.x) <= size.x / 2f && Mathf.Abs(l.y) <= size.y / 2f && Mathf.Abs(l.z) <= size.z / 2f;
        }

        public CarDisplay ShowOnStage(CarInstance car, int lotNo)
        {
            if (!displayPrefab || !stage) return null;
            var d = Instantiate(displayPrefab, stage.position, stage.rotation);
            var lot = CarSalesServices.Auction ? CarSalesServices.Auction.LiveLot : null;
            d.InitializeForAuction(car, lot != null ? lot.id : "", lotNo);
            // Not interactable on stage: E is your paddle while a lot is up.
            foreach (var t in d.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer == 20) t.gameObject.layer = 0;
            return d;
        }

        float timer;

        void Update()
        {
            if (!screen) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.25f;
            var a = CarSalesServices.Auction;
            var lot = a ? a.LiveLot : null;
            if (lot != null)
            {
                var car = a.CarOf(lot);
                screen.text = $"LOT {lot.lotNo}\n{car?.Name}\n{(string.IsNullOrEmpty(lot.leader) ? $"START {UIFactory.Money(lot.startPrice)}" : UIFactory.Money(lot.currentBid))}";
                return;
            }
            var next = a ? a.NextHallSale() : null;
            screen.text = next == null ? "AUCTION CENTER" :
                next.day == CarRecords.Today ? $"TODAY {next.startHour:00}:00\n{CarSales.Sim.AuctionRules.TypeName(next.Type).ToUpper()}" :
                $"NEXT SALE\nDAY {next.day}  {next.startHour:00}:00\n{CarSales.Sim.AuctionRules.TypeName(next.Type).ToUpper()}";
        }
    }
}
