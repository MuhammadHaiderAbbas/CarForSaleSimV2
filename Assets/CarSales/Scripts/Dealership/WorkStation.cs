using System.Collections.Generic;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    public enum StationType { Mechanical = 0, Tyres = 1, MotBay = 2 }

    /// <summary>
    /// A bay in your service centre (spec §6.6 stations): the mechanical bay has a lift, the tyre bay a tyre machine,
    /// the MOT bay runs tests. Put this on the bay's console (Interactable layer); the bay is a box around
    /// <see cref="bay"/>. Drive or tow a car into the box, then use the console.
    /// </summary>
    public class WorkStation : MonoBehaviour, IInteractable
    {
        public static readonly List<WorkStation> All = new List<WorkStation>();

        public StationType stationType;
        public string displayName = "Mechanical bay";
        [Tooltip("Centre of the parking box (empty = this transform).")]
        public Transform bay;
        public Vector3 baySize = new Vector3(4f, 3f, 7f);

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        Transform Box => bay ? bay : transform;

        public bool Contains(Vector3 world)
        {
            var local = Box.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= baySize.x / 2f && Mathf.Abs(local.z) <= baySize.z / 2f && local.y > -1f && local.y < baySize.y;
        }

        CarDisplay cached;
        float cachedAt = -1f;

        /// <summary>The owned car standing in this bay, or null (looked up at most twice a second).</summary>
        public CarDisplay CarInBay()
        {
            if (Time.unscaledTime - cachedAt < 0.5f && (!cached || Contains(cached.transform.position))) return cached;
            cachedAt = Time.unscaledTime;
            cached = FindCar();
            return cached;
        }

        CarDisplay FindCar()
        {
            foreach (var d in FindObjectsByType<CarDisplay>(FindObjectsSortMode.None))
                if (d && d.Data != null && !d.IsForSaleListing && Contains(d.transform.position) && CarRecords.IsOwned(d.Data)) return d;
            return null;
        }

        public static WorkStation BayOf(CarDisplay car, params StationType[] types)
        {
            if (!car) return null;
            foreach (var s in All)
                if (s && System.Array.IndexOf(types, s.stationType) >= 0 && s.Contains(car.transform.position)) return s;
            return null;
        }

        public static bool Exists(StationType type) => All.Exists(s => s && s.stationType == type);

        /// <summary>Which bay a job is done in.</summary>
        public static StationType[] StationsFor(RepairJobDef job) =>
            job.Station == Station.Tyres ? new[] { StationType.Tyres, StationType.Mechanical } : new[] { StationType.Mechanical };

        public bool CanInteract() => true;

        public string GetInteractionText()
        {
            var car = CarInBay();
            return car ? $"[E] {displayName}: {car.Data.Name}" : $"[E] {displayName} (empty - drive a car in)";
        }

        public void Interact(GameObject interactor)
        {
            var ui = CarSalesServices.UI;
            var ws = CarSalesServices.Workshop;
            if (!ui || !ws) return;
            var car = CarInBay();
            if (!car)
            {
                UIFactory.Confirm(displayName, "There's no car of yours in this bay. Drive one in (F), or tow it here with the phone, then come back to this console.",
                                  "Workshop board", UIFactory.Accent, () => ui.computerHub.Workshop.ShowBoard());
                return;
            }
            var c = car.Data;
            switch (stationType)
            {
                case StationType.MotBay:
                    UIFactory.Modal(ui.transform, $"MOT bay - {c.Name}",
                        $"Test the car yourself: free, takes 45 minutes.\nMOT: {CarRecords.MotText(c)}",
                        ("Close", UIFactory.Neutral, null),
                        ("Run the MOT", UIFactory.Accent, () => ws.RunMotAtBay(c)));
                    break;
                default:
                    bool lift = stationType == StationType.Mechanical;
                    UIFactory.Modal(ui.transform, $"{displayName} - {c.Name}",
                        (lift ? "The car is on the lift. Inspect it by hand (including underneath, free), " : "Inspect the car by hand, ") +
                        "or pick a job from the board and do it yourself: hold E on each marker to undo the bolts, swap the part and tighten up.",
                        ("Close", UIFactory.Neutral, null),
                        ("Inspect by hand", UIFactory.Warn, () => ws.Session.StartInspection(car, lift)),
                        ("Jobs for this car", UIFactory.Accent, () => ui.computerHub.Workshop.ShowBoard(c.instanceId)));
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
            Gizmos.matrix = Box.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, baySize.y / 2f, 0f), baySize);
        }
    }
}
