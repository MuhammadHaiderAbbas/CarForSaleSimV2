using UnityEngine;

namespace CarSales
{
    /// <summary>A screen that handles Back (Esc / gamepad B) itself, e.g. the phone returning to its home screen.</summary>
    public interface IBackHandler { bool HandleBack(); }

    /// <summary>A full-screen panel created and owned by UIManager. Build() creates its UI from code.</summary>
    public abstract class UIPanel : MonoBehaviour
    {
        protected RectTransform Root => (RectTransform)transform;
        public abstract void Build();
        protected void OpenSelf() => CarSalesServices.UI.Open(gameObject);
        protected static void CloseAll() => CarSalesServices.UI.Close();
        protected static void GoBack() => CarSalesServices.UI.Back();
    }

    /// <summary>A page inside the dealer computer window.</summary>
    public abstract class ComputerPage : MonoBehaviour
    {
        public string pageTitle;
        protected RectTransform Area => (RectTransform)transform;
        public abstract void Build();
        public abstract void Refresh();
        public void Show() => CarSalesServices.UI.computerHub.ShowPage(this);
    }
}
