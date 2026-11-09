using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace CarSales
{
    /// <summary>Customer state machine: Browse -> Interested -> Negotiate -> Leave.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CustomerAI : MonoBehaviour, IInteractable
    {
        [SerializeField] Vector2 browseTime = new Vector2(3f, 6f);
        [SerializeField] float interestTimeout = 45f;
        [SerializeField] TMP_Text bubble;      // world-space text above head
        [SerializeField] Animator animator;    // optional, expects a float parameter "Speed"
        [SerializeField] Renderer bodyRenderer; // optional, gets a random shirt colour

        public CustomerProfile Profile { get; private set; }
        public CustomerState State { get; private set; } = CustomerState.Arriving;
        public CarDisplay TargetCar { get; private set; }

        NavMeshAgent agent;
        CustomerSpawner spawner;
        int maxCarsToBrowse;
        readonly HashSet<CarDisplay> seen = new HashSet<CarDisplay>();

        void Awake() => agent = GetComponent<NavMeshAgent>();

        /// <summary>
        /// Replaces the built-in capsule look with an imported character (Asset Studio). The capsule's collider stays
        /// for interaction; the character's Animator gets the "Speed" parameter.
        /// </summary>
        public void UseAppearance(GameObject characterPrefab)
        {
            if (!characterPrefab) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!bubble || !r.transform.IsChildOf(bubble.transform)) r.enabled = false;
            var look = Instantiate(characterPrefab, transform);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.identity;
            foreach (var c in look.GetComponentsInChildren<Collider>(true)) Destroy(c);
            UIFactory.SetLayer(look, gameObject.layer);
            animator = look.GetComponentInChildren<Animator>();
            if (animator) animator.applyRootMotion = false;
            bodyRenderer = null;   // keep the character's own clothes
        }

        public void Init(CustomerProfile profile, CustomerSpawner owner)
        {
            Profile = profile;
            spawner = owner;
            name = "Customer_" + profile.customerName;
            maxCarsToBrowse = Random.Range(2, 5);
            if (bodyRenderer)
            {
                var mpb = new MaterialPropertyBlock();
                var c = Random.ColorHSV(0f, 1f, 0.4f, 0.8f, 0.5f, 0.9f);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                bodyRenderer.SetPropertyBlock(mpb);
            }
            marker = MinimapMarker.Attach(gameObject, Color.white, 1.8f);
            StartCoroutine(BrowseRoutine());
        }

        MinimapMarker marker;

        void Update()
        {
            if (animator) animator.SetFloat("Speed", agent.velocity.magnitude);
        }

        // ---------------- STATES ----------------
        IEnumerator BrowseRoutine()
        {
            State = CustomerState.Browsing;
            SetBubble("");
            while (seen.Count < maxCarsToBrowse)
            {
                TargetCar = PickCar();
                if (TargetCar == null) break;

                agent.SetDestination(TargetCar.BrowsePoint.position);
                yield return WaitUntilArrived();
                if (!IsTargetValid()) { TargetCar = null; continue; }

                Face(TargetCar.transform.position);
                SetBubble("...");
                yield return new WaitForSeconds(Random.Range(browseTime.x, browseTime.y) * 0.5f);
                // Phase 7b: walk round the car - tyres, inside, under the bonnet.
                yield return InspectAround();
                if (!IsTargetValid()) { TargetCar = null; continue; }

                if (IsTargetValid() && WantsCar(TargetCar))
                {
                    StartCoroutine(InterestedRoutine());
                    yield break;
                }
                seen.Add(TargetCar);
                SetBubble("");
            }
            Leave();
        }

        IEnumerator InterestedRoutine()
        {
            State = CustomerState.Interested;
            agent.ResetPath();
            SetBubble("!");
            CarSalesServices.PlaySfx(Sfx.Notify);
            if (marker) marker.SetColor(UIFactory.Gold);
            CarSalesServices.UI.Toast(ToastType.Info, "Customer interested!",
                $"{Profile.customerName} wants to talk about the {TargetCar.Definition.displayName}. Look for the '!'.");

            float t = 0f;
            while (t < interestTimeout)
            {
                if (!IsTargetValid()) { seen.Add(TargetCar); StartCoroutine(BrowseRoutine()); yield break; }
                t += Time.deltaTime;
                // Phase 7: a salesperson steps in if you don't come over.
                if (t >= StaffManager.SalespersonDelay && CarSalesServices.Staff)
                {
                    var sp = CarSalesServices.Staff.FreeSalesperson();
                    if (sp != null)
                    {
                        State = CustomerState.Negotiating;
                        SetBubble("$");
                        yield return new WaitForSeconds(4f);
                        if (IsTargetValid()) { CarSalesServices.Staff.HandleCustomer(this, sp); yield break; }
                        seen.Add(TargetCar);
                        StartCoroutine(BrowseRoutine());
                        yield break;
                    }
                }
                yield return null;
            }
            CarSalesServices.Economy.ChangeReputation(-1f); // ignored customer
            SetBubble(":(");
            Leave();
        }

        public void Leave()
        {
            if (State == CustomerState.Leaving) return;
            StopAllCoroutines();
            StartCoroutine(LeaveRoutine());
        }

        IEnumerator LeaveRoutine()
        {
            State = CustomerState.Leaving;
            if (marker) marker.SetColor(new Color(1f, 1f, 1f, 0.35f));
            TargetCar = null;
            var exit = spawner ? spawner.Dealer.exitPoint : null;
            if (exit)
            {
                agent.SetDestination(exit.position);
                yield return WaitUntilArrived(60f);
            }
            if (spawner) spawner.Unregister(this);
            Destroy(gameObject);
        }

        // ---------------- INTERACTION ----------------
        public bool CanInteract() => State == CustomerState.Interested && IsTargetValid();

        public string GetInteractionText() =>
            $"[E] Talk to {Profile.customerName} ({CustomerDeal.Persona(Profile).Name.ToLower()}) about the {TargetCar.Definition.displayName}";

        public void Interact(GameObject interactor)
        {
            StopAllCoroutines();
            State = CustomerState.Negotiating;
            Face(interactor.transform.position);
            CarSalesServices.UI.negotiation.Show(this);
        }

        public void EndNegotiation(bool bought)
        {
            SetBubble(bought ? ":)" : ">:(");
            Leave();
        }

        // ---------------- DECISIONS ----------------
        /// <summary>Phase 7: score every unseen listed car for this buyer type and walk to one of the best.</summary>
        CarDisplay PickCar()
        {
            var options = spawner.Dealer.GetListedCars().Where(c => !seen.Contains(c)).ToList();
            if (options.Count == 0) return null;
            if (!string.IsNullOrEmpty(Profile.targetCarId))
            {
                var wanted = options.Find(c => c.Data.instanceId == Profile.targetCarId);
                if (wanted) return wanted;
            }
            var scored = options.Select(c => (car: c, score: CustomerDeal.Score(Profile, c))).OrderByDescending(x => x.score).Take(3).ToList();
            float total = scored.Sum(x => x.score * x.score) + 0.0001f;
            float r = Random.value * total;
            foreach (var x in scored) { r -= x.score * x.score; if (r <= 0f) return x.car; }
            return scored[0].car;
        }

        bool WantsCar(CarDisplay car)
        {
            float score = CustomerDeal.Score(Profile, car);
            if (score <= 0f) return false;
            float interest = score * 1.3f - 0.15f;
            if (car.Data.instanceId == Profile.targetCarId) interest += CarSales.Sim.Listings.LeadInterestBonus;
            interest += (CarSalesServices.Economy.Reputation - 50f) / 200f;           // dealer reputation
            if (CarSalesServices.Upgrades) interest += CarSalesServices.Upgrades.ShowroomInterestBonus;
            return Random.value < Mathf.Clamp01(interest);
        }

        IEnumerator InspectAround()
        {
            if (!TargetCar) yield break;
            var t = TargetCar.transform;
            string[] lines = { "Checking the tyres...", "Looking inside...", "Under the bonnet..." };
            Vector3[] offsets = { t.right * 1.9f + t.forward * 1.2f, -t.right * 1.9f, t.forward * 3f };
            int stops = Random.Range(1, 3);
            for (int i = 0; i < stops; i++)
            {
                int k = Random.Range(0, offsets.Length);
                if (NavMesh.SamplePosition(t.position + offsets[k], out var hit, 1.5f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    yield return WaitUntilArrived(10f);
                }
                if (!TargetCar) yield break;
                Face(t.position);
                SetBubble(lines[k]);
                yield return new WaitForSeconds(Random.Range(1.5f, 3f));
            }
            SetBubble("");
        }

        // ---------------- HELPERS ----------------
        bool IsTargetValid() => TargetCar != null && TargetCar.Data.state == CarState.Listed;

        IEnumerator WaitUntilArrived(float timeout = 30f)
        {
            float t = 0f;
            yield return null; // let the path calculate
            while (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.2f)
            {
                t += Time.deltaTime;
                if (t > timeout) yield break;
                yield return null;
            }
        }

        void Face(Vector3 point)
        {
            var dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        }

        void SetBubble(string text)
        {
            // Phase 10: speech nearby is also captioned (not the "..." / "!" icons).
            if (!string.IsNullOrEmpty(text) && text.Length > 3 && Camera.main && Vector3.Distance(Camera.main.transform.position, transform.position) < 12f)
                Subtitles.Say(Profile != null ? Profile.customerName : "Customer", text);
            if (!bubble) return;
            bubble.text = text;
            bubble.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
