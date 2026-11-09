using CarSales.Foundation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>Developer shortcuts (F1 +$10k, F5 save, F9 load, F12 delete save). Remove before shipping.</summary>
    public class DebugCheats : MonoBehaviour
    {
        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.f1Key.wasPressedThisFrame)  CarSalesServices.Economy.AddMoney(10000, TxCategory.Debug, "F1 debug cheat");
            if (k.f5Key.wasPressedThisFrame)  { CarSalesServices.Save.SaveGame(); CarSalesServices.UI.Notify("Game saved."); }
            if (k.f9Key.wasPressedThisFrame)  CarSalesServices.Save.LoadGame();
            if (k.f12Key.wasPressedThisFrame) CarSalesServices.Save.DeleteSave();
        }
    }
}
