using CarSales.Foundation;
using UnityEngine;

namespace CarSales
{
    /// <summary>Connects the engine-free Foundation layer to Unity (logging, play-mode resets).</summary>
    static class FoundationHooks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            EventBus.Clear();   // also correct when "Enter Play Mode Options" skip the domain reload
            EventBus.OnHandlerError = Debug.LogException;
        }
    }
}
