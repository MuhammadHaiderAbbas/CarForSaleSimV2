using UnityEngine;

namespace CarSales
{
    /// <summary>The private seller waiting next to their car at a viewing. Talk to them with E.</summary>
    public class SellerNpc : MonoBehaviour, IInteractable
    {
        public string adId;
        public string sellerName;
        public CarDisplay car;

        public bool CanInteract() => CarSalesServices.Sourcing && CarSalesServices.Sourcing.Ad(adId) != null;
        public string GetInteractionText() => $"[E] Talk to {sellerName} about the {(car && car.Definition ? car.Definition.displayName : "car")}";
        public void Interact(GameObject interactor) => CarSalesServices.UI.privateSeller.Show(adId);

        void Update()
        {
            var cam = Camera.main;
            if (!cam) return;
            var d = cam.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.5f && d.sqrMagnitude < 100f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * 3f);
        }
    }
}
