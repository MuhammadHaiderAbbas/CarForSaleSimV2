using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// A viewing at a private seller's house (spec §6.8): ask questions (they may lie), inspect the car, take it around
    /// the block, run a history check, haggle and pay. The logbook and keys are handed over on the spot.
    /// </summary>
    public class PrivateSellerUI : UIPanel
    {
        PrivateAd ad;
        RawImage photo;
        TextMeshProUGUI titleText, adText, askingText, chatText;
        TMP_InputField offerInput;
        string log = "";
        int current;
        readonly System.Random rng = new System.Random();
        bool testDriven;

        static SourcingManager S => CarSalesServices.Sourcing;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Private sale", new Vector2(1300f, 820f), CloseAll, out titleText, out _);
            UIFactory.Horizontal(body, 0, 24f, TextAnchor.UpperLeft).childForceExpandHeight = true;
            var left = UIFactory.VCol(body, 8f, "Left", 520f);
            photo = UIFactory.CarPicture(left, 280f);
            adText = UIFactory.Text(left, "", 16, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            adText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(adText, h: 110);
            askingText = UIFactory.Text(left, "", 26, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(askingText, h: 36);
            UIFactory.SectionTitle(left, "Check it");
            var r1 = UIFactory.HRow(left, 44, 8, "Checks");
            UIFactory.Btn(r1, "Inspect", UIFactory.Warn, Inspect, 42f, 14f);
            UIFactory.Btn(r1, "Around the block", UIFactory.Accent, AroundTheBlock, 42f, 14f);
            UIFactory.Btn(r1, $"History check {UIFactory.Money(InspectionEngine.HistoryCheckCost)}", UIFactory.Neutral, History, 42f, 13f);
            UIFactory.Spacer(left);
            UIFactory.SectionTitle(left, "Make an offer");
            var r2 = UIFactory.HRow(left, 48, 8, "Offer");
            offerInput = UIFactory.MakeInput(r2, "Your offer", 46f);
            offerInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            var offer = UIFactory.Btn(r2, "Offer", UIFactory.Accent, Offer, 46f, 16f);
            UIFactory.LE(offer, h: 46, w: 110, flexW: 0);
            var r3 = UIFactory.HRow(left, 48, 8, "Deal");
            UIFactory.Btn(r3, "Pay the asking price", UIFactory.Good, () => Pay(current), 46f, 15f);
            UIFactory.Btn(r3, "Leave", UIFactory.Neutral, CloseAll, 46f, 15f);

            var right = UIFactory.VCol(body, 8f, "Right");
            UIFactory.SectionTitle(right, "Ask the seller");
            foreach (SellerQuestion q in System.Enum.GetValues(typeof(SellerQuestion)))
            {
                var qq = q;
                var b = UIFactory.Btn(right, QuestionText(q), UIFactory.CardLight, () => Ask(qq), out var l, 40f, 15f);
                l.alignment = TextAlignmentOptions.MidlineLeft;
                l.margin = new Vector4(12f, 0f, 0f, 0f);
            }
            var chatBg = UIFactory.Panel(right, "Chat", UIFactory.Overlay, 1.4f);
            UIFactory.LE(chatBg, flexH: 1);
            UIFactory.Vertical(chatBg, 14, 0f);
            var scroll = UIFactory.ScrollList(chatBg.transform, 4f, "ChatScroll");
            chatText = UIFactory.Text(scroll, "", 16, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            chatText.textWrappingMode = TextWrappingModes.Normal;
            chatText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static string QuestionText(SellerQuestion q) =>
            q == SellerQuestion.AnyProblems ? "\"Any problems with it?\"" : q == SellerQuestion.ServiceHistory ? "\"Has it got a service history?\"" :
            q == SellerQuestion.WhySelling ? "\"Why are you selling?\"" : q == SellerQuestion.Mileage ? "\"Is the mileage genuine?\"" : "\"Any finance on it?\"";

        public void Show(string adId)
        {
            if (!S) return;
            var a = S.Ad(adId);
            if (a == null) return;
            if (ad == null || ad.id != a.id) { log = ""; current = a.asking; testDriven = false; }
            ad = a;
            OpenSelf();
            Render();
            if (log == "") Say(ad.seller, $"Hi, you must be here about the {S.CarOf(ad)?.Name}. Have a good look.");
        }

        CarInstance Car => S ? S.CarOf(ad) : null;

        void Render()
        {
            var car = Car;
            if (car == null) return;
            var def = CarSalesServices.Database.Get(car.carId);
            titleText.text = $"Private sale  -  {ad.seller}, {S.Address(ad)}";
            UIFactory.SetCarPicture(photo, car);
            adText.text = $"<b>{def.year} {def.brand} {def.displayName}</b>  -  {car.identity.plate}\n\"{ad.claim}\"\n" +
                          $"<size=85%><color={UIFactory.Hex(UIFactory.TextDim)}>Odometer {car.mileage:N0} km  -  faults you've found: {InspectionEngine.KnownCount(car.faults)}" +
                          $"  -  estimated value {UIFactory.Money(CarValueCalculator.MarketValue(car, useReported: true))}</color></size>";
            askingText.text = current < ad.asking ? $"Down to {UIFactory.Money(current)} (asked {UIFactory.Money(ad.asking)})" : $"Asking {UIFactory.Money(ad.asking)} ono";
            chatText.text = log;
        }

        void Say(string who, string text)
        {
            log += $"<b><color={UIFactory.Hex(who == "You" ? UIFactory.ChatPlayer : UIFactory.Gold)}>{who}:</color></b> {text}\n";
            Render();
        }

        void Ask(SellerQuestion q)
        {
            var car = Car;
            if (car == null) return;
            bool bad; string detail = "";
            switch (q)
            {
                case SellerQuestion.AnyProblems:
                    var worst = car.faults.Where(f => f.Def != null).OrderByDescending(f => f.severity).FirstOrDefault();
                    bad = worst != null && worst.severity > 0.3f;
                    detail = worst != null ? worst.Def.Symptom.ToLowerInvariant() : "";
                    if (bad && rng.NextDouble() < ad.honesty) { worst.known = true; worst.foundBy = (int)InspectMethod.Visual; }
                    break;
                case SellerQuestion.ServiceHistory: bad = car.documents.service != ServiceHistory.Full; break;
                case SellerQuestion.Mileage: bad = car.history.clocked; break;
                case SellerQuestion.Finance: bad = car.history.outstandingFinance && !car.history.financeSettled; break;
                default: bad = false; break;
            }
            Say("You", QuestionText(q).Trim('"'));
            var (text, lie) = PrivateSellers.Answer(q, bad, detail, ad.honesty, rng);
            Say(ad.seller, text);
        }

        void Inspect()
        {
            var d = S ? S.ShownCar(ad.id) : null;
            if (Car == null) return;
            CarSalesServices.UI.inspection.Show(Car, d);
        }

        void AroundTheBlock()
        {
            var car = Car;
            if (car == null || testDriven) { CarSalesServices.UI.Notify("You've already driven it."); return; }
            testDriven = true;
            car.knowledge.roadTestKm = WorkshopManager.RoadTestKm;
            var r = InspectionEngine.Run(InspectMethod.RoadTest, car.components, car.faults, car.history, car.knowledge, PrivateSellers.AroundTheBlockSkill, new System.Random());
            car.mileage += 3;
            if (CarSalesServices.DayCycle) CarSalesServices.DayCycle.AdvanceTime(0.25f);
            Say("You", "Mind if I take it round the block?");
            Say(ad.seller, "Go ahead, I'll come with you.");
            Say("You", r.found.Count == 0 ? "(It drives fine.)" : $"(You notice: {string.Join("; ", r.found.Select(f => f.Def.Symptom.ToLowerInvariant()))}.)");
        }

        void History()
        {
            var car = Car;
            if (car == null || !CarSalesServices.Workshop) return;
            var lines = CarSalesServices.Workshop.RunHistoryCheck(car);
            if (lines != null) Say("History check", string.Join(" ", lines));
        }

        void Offer()
        {
            if (!int.TryParse(offerInput.text, out int offer) || offer <= 0) { CarSalesServices.UI.Error("Enter an offer."); return; }
            Say("You", $"Would you take {UIFactory.Money(offer)}?");
            int found = InspectionEngine.KnownCount(Car.faults);
            int floor = ad.minPrice - Mathf.RoundToInt(CarRecords.KnownFaults(Car).Sum(f => CarRecords.RepairEstimate(Car, f)) * 0.3f);
            var (ok, counter) = PrivateSellers.Haggle(offer, current, Mathf.Max(floor, ad.minPrice / 2), rng);
            if (ok) { Say(ad.seller, "Go on then, it's a deal."); Pay(offer); return; }
            ad.patience--;
            if (ad.patience <= 0) { Say(ad.seller, "I don't think we're going to agree. Sorry, I've got other people coming."); current = ad.asking; return; }
            current = counter;
            Say(ad.seller, found > 0 ? $"I know it's not perfect... {UIFactory.Money(counter)}, final offer." : $"I can't go that low. {UIFactory.Money(counter)}?");
        }

        void Pay(int price)
        {
            if (ad.patience <= 0) { CarSalesServices.UI.Error($"{ad.seller} won't sell to you now."); return; }
            if (S.Buy(ad, price)) CloseAll();
        }
    }
}
