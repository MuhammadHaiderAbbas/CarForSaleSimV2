using UnityEngine;

namespace CarSales
{
    /// <summary>The Workshop Board on the service-centre wall: opens the board in the Workshop app.</summary>
    public class WorkshopBoardSign : MonoBehaviour, IInteractable
    {
        public bool CanInteract() => CarSalesServices.UI && CarSalesServices.UI.computerHub;
        public string GetInteractionText() => "[E] Workshop board (jobs, parts, mechanic)";
        public void Interact(GameObject interactor) => CarSalesServices.UI.computerHub.Workshop.ShowBoard();
    }
}
