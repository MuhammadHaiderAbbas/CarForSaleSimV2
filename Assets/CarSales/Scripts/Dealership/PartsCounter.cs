using UnityEngine;

namespace CarSales
{
    /// <summary>The counter at Parts Direct in town: buying in person is 10% cheaper and you take the parts with you.</summary>
    public class PartsCounter : MonoBehaviour, IInteractable
    {
        void Start() => MinimapMarker.Attach(gameObject, UIFactory.Warn, 3.5f);
        public bool CanInteract() => CarSalesServices.Workshop && CarSalesServices.UI;
        public string GetInteractionText() => "[E] Parts counter (10% off, take them today)";
        public void Interact(GameObject interactor)
        {
            CarSalesServices.Workshop.AtCounter = true;
            CarSalesServices.UI.computerHub.Workshop.ShowCounter();
        }
    }
}
