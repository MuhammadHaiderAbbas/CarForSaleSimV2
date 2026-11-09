using UnityEngine;

namespace CarSales
{
    /// <summary>A glowing marker on a car during a hands-on inspection or repair. Look at it and hold E.</summary>
    public class HandsOnSocket : MonoBehaviour, IInteractable
    {
        public HandsOnSession session;
        public int index;
        public string label;
        public float seconds = 1f;
        public bool done;

        public bool CanInteract() => session && session.IsActive(this);
        public string GetInteractionText() => session ? session.PromptFor(this) : "";
        public void Interact(GameObject interactor) { if (session) session.BeginHold(this); }
    }
}
