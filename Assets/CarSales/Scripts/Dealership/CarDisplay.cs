using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>A car standing on the lot. Root of CarDisplay.prefab.</summary>
    public class CarDisplay : MonoBehaviour, IInteractable
    {
        [SerializeField] Transform visualRoot;
        [SerializeField] Transform browsePoint;   // where customers stand to look at the car
        [SerializeField] TMP_Text priceTag;       // world-space TextMeshPro above the roof

        public CarInstance Data { get; private set; }
        /// <summary>True for cars standing at the Used Car Market that you don't own yet.</summary>
        public bool IsForSaleListing { get; private set; }
        public CarDefinition Definition { get; private set; }
        public Transform BrowsePoint => browsePoint ? browsePoint : transform;
        /// <summary>Set for cars standing in the auction yard / on the stage (phase 6).</summary>
        public string AuctionLotId { get; private set; }
        /// <summary>Set for a private seller's car at a viewing (phase 7b).</summary>
        public string PrivateAdId { get; private set; }

        public void MarkPrivateAd(string adId, int asking)
        {
            PrivateAdId = adId;
            if (priceTag) priceTag.text = $"${asking:N0} ono";
            var rb = GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
        }
        CarVisual visual;

        public void InitializeForSale(CarInstance listing)
        {
            IsForSaleListing = true;
            var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (obstacle) obstacle.enabled = false;
            Initialize(listing);
            if (priceTag)
            {
                priceTag.gameObject.SetActive(true);
                priceTag.text = $"${listing.purchasePrice:N0}";
            }
        }

        /// <summary>A lot in the auction yard: shows its lot number; E opens the lot (report, inspection, bids).</summary>
        public void InitializeForAuction(CarInstance car, string lotId, int lotNo)
        {
            InitializeForSale(car);
            AuctionLotId = lotId;
            if (priceTag) priceTag.text = $"LOT {lotNo}";
            var rb = GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
        }

        public void Initialize(CarInstance data)
        {
            Data = data;
            Definition = CarSalesServices.Database.Get(data.carId);
            if (visual) Destroy(visual.gameObject);
            if (Definition && Definition.displayPrefab)
            {
                var go = Instantiate(Definition.displayPrefab, visualRoot ? visualRoot : transform);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                visual = go.GetComponent<CarVisual>();
            }
            name = Definition ? $"Car_{Definition.displayName}_{data.instanceId.Substring(0, 6)}" : "Car_Unknown";
            Refresh();
        }

        public void Refresh()
        {
            MinimapMarker.Attach(gameObject, MarkerColor(), 3.2f);
            if (IsForSaleListing)
            {
                if (visual) { visual.Apply(Data.paintColor, 1f - Data.cleanliness / 100f); CarDamageVisual.Apply(visual.gameObject, Data); }
                return;
            }
            if (visual) { visual.Apply(Data.paintColor, 1f - Data.cleanliness / 100f); CarDamageVisual.Apply(visual.gameObject, Data); }
            bool listed = Data.state == CarState.Listed;
            if (priceTag)
            {
                priceTag.gameObject.SetActive(listed);
                if (listed) priceTag.text = $"${Data.listedPrice:N0}";
            }
        }

        Color MarkerColor()
        {
            if (IsForSaleListing) return new Color(0.75f, 0.5f, 1f);
            if (Data.personal) return new Color(0.2f, 0.95f, 0.95f);
            switch (Data.state)
            {
                case CarState.Listed: return UIFactory.Good;
                case CarState.NeedsRepair: return UIFactory.Warn;
                default: return new Color(0.55f, 0.65f, 0.8f);
            }
        }

        public bool CanInteract() => Data != null && Definition != null;

        public string GetInteractionText()
        {
            if (!string.IsNullOrEmpty(AuctionLotId))
            {
                var lot = CarSalesServices.Auction ? CarSalesServices.Auction.Lot(AuctionLotId) : null;
                return lot == null ? $"{Definition.displayName}" :
                    $"[E] Lot {lot.lotNo}: {Definition.year} {Definition.brand} {Definition.displayName}  |  grade {lot.report.grade}  |  guide ${lot.estimate:N0}";
            }
            if (!string.IsNullOrEmpty(PrivateAdId))
                return $"[E] Private sale: {Definition.year} {Definition.brand} {Definition.displayName}  |  ${Data.purchasePrice:N0} ono";
            if (IsForSaleListing)
                return $"[E] For sale: {Definition.year} {Definition.brand} {Definition.displayName}  |  ${Data.purchasePrice:N0}  |  free inspection on site";
            string name = $"{Definition.year} {Definition.brand} {Definition.displayName}";
            if (Data.personal) return $"[E] Your car: {name}  |  Fuel {Data.fuel:0}%   [F] Drive";
            return $"[E] {name}  |  Condition {Data.condition:0}%  |  Fuel {Data.fuel:0}%  |  {Data.state}   [F] Drive";
        }

        public void Interact(GameObject interactor)
        {
            if (!string.IsNullOrEmpty(AuctionLotId)) { CarSalesServices.UI.auction.ShowLot(AuctionLotId, this); return; }
            if (!string.IsNullOrEmpty(PrivateAdId)) { CarSalesServices.UI.privateSeller.Show(PrivateAdId); return; }
            if (IsForSaleListing) CarSalesServices.UI.vehicleDetails.ShowPhysical(this);
            else CarSalesServices.UI.carMenu.Show(this);
        }
    }
}
