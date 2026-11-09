using CarSales.Foundation;
using CarSales.Sim;
using TMPro;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Paperwork and handover after a deal (spec §6.10): invoice, logbook (V5C) transfer, service records, keys, and an
    /// optional 3-month warranty. Complete paperwork earns a better review; a warranty earns money but may bring claims.
    /// </summary>
    public class HandoverUI : UIPanel
    {
        public struct Deal
        {
            public CustomerAI customer;
            public int price, marketValue, disclosed, hiddenKnown, rounds, tradeInCredit;
            public CarInstance tradeIn;
            public bool finance;
        }

        Deal deal;
        bool pending, warrantyOffered, warrantySold;
        TextMeshProUGUI checklist, totalText, warrantyText;
        UnityEngine.UI.Button warrantyButton;
        readonly System.Random rng = new System.Random();

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Paperwork & handover", new Vector2(820f, 640f), null);
            UIFactory.Vertical(body, 0, 12f);
            checklist = UIFactory.Text(body, "", 18, UIFactory.TextMain, TextAlignmentOptions.TopLeft);
            checklist.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(checklist, h: 230);
            warrantyText = UIFactory.Text(body, "", 16, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            warrantyText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(warrantyText, h: 60);
            warrantyButton = UIFactory.Btn(body, "Offer a warranty", UIFactory.Accent, OfferWarranty, 46f, 16f);
            UIFactory.Spacer(body);
            totalText = UIFactory.Text(body, "", 26, UIFactory.Gold, TextAlignmentOptions.Center, true);
            UIFactory.LE(totalText, h: 40);
            UIFactory.Btn(body, "Hand over the keys", UIFactory.Good, Complete, 56f, 20f);
        }

        public void Show(Deal d)
        {
            deal = d;
            pending = true;
            warrantyOffered = warrantySold = false;
            OpenSelf();
            Render();
        }

        CarInstance Car => deal.customer && deal.customer.TargetCar ? deal.customer.TargetCar.Data : null;

        float PaperworkScore()
        {
            var c = Car;
            return c == null ? 0f : AfterSale.Paperwork(c.documents.v5cPresent, c.documents.service != ServiceHistory.None, c.documents.keys, true);
        }

        void Render()
        {
            var c = Car;
            if (c == null) return;
            string ok = UIFactory.Hex(UIFactory.Good), bad = UIFactory.Hex(UIFactory.Bad);
            string Tick(bool v, string yes, string no) => v ? $"<color={ok}>[x]</color> {yes}" : $"<color={bad}>[ ]</color> {no}";
            checklist.text =
                $"<b>{c.Name}</b> to <b>{deal.customer.Profile.customerName}</b>\n\n" +
                Tick(true, "Sales invoice printed", "") + "\n" +
                Tick(c.documents.v5cPresent, "Logbook (V5C): new keeper section filled in and sent", "Logbook missing: the buyer has to apply for one (they won't be pleased)") + "\n" +
                Tick(c.documents.service != ServiceHistory.None, $"Service records handed over ({c.documents.serviceStamps} stamps)", "No service records to hand over") + "\n" +
                Tick(c.documents.keys >= 2, $"{c.documents.keys} keys handed over", c.documents.keys == 1 ? "Only one key" : "No key!") + "\n" +
                Tick(CarRecords.MotValid(c), "MOT certificate valid", "MOT expired") + "\n" +
                (deal.tradeIn != null ? $"\nTrade-in: their {deal.tradeIn.Name} (credited {UIFactory.Money(deal.tradeInCredit)}) - logbook signed over to you." : "") +
                (deal.finance ? "\nFinance: paperwork sent to the lender." : "");
            int wPrice = AfterSale.WarrantyPrice(deal.price);
            warrantyText.text = warrantySold ? $"3-month warranty sold for {UIFactory.Money(wPrice)}. If something goes wrong in the next {AfterSale.WarrantyDays} days, you pay the repair."
                              : warrantyOffered ? "They don't want a warranty." : $"Offer a 3-month warranty for {UIFactory.Money(wPrice)}? Extra profit, but you pay any repairs that come up.";
            warrantyButton.interactable = !warrantyOffered;
            int cash = deal.price - (deal.tradeIn != null ? deal.tradeInCredit : 0) + (warrantySold ? wPrice : 0);
            totalText.text = $"You receive {UIFactory.Money(cash)}" + (deal.tradeIn != null ? " + their car" : "");
        }

        void OfferWarranty()
        {
            warrantyOffered = true;
            var persona = CustomerDeal.Persona(deal.customer.Profile);
            float chance = 0.25f + persona.WCondition + (persona.Id == PersonaId.FirstTimeBuyer || persona.Id == PersonaId.FamilyBuyer ? 0.2f : 0f) - persona.Haggling * 0.2f;
            warrantySold = rng.NextDouble() < chance;
            Render();
        }

        void Complete()
        {
            if (!pending) return;
            pending = false;
            var c = Car;
            if (c == null) { CloseAll(); return; }
            int wPrice = AfterSale.WarrantyPrice(deal.price);
            float bonus = AfterSale.ReviewBonus(PaperworkScore());
            if (warrantySold)
            {
                CarSalesServices.Economy.AddMoney(wPrice, TxCategory.Warranty, $"Warranty sold - {c.Name}", c.instanceId);
                if (CarSalesServices.Sourcing) CarSalesServices.Sourcing.RegisterWarranty(c, deal.customer.Profile.customerName);
                bonus += 0.3f;
            }
            CustomerDeal.CompleteSale(deal.customer, deal.price, deal.marketValue, deal.disclosed, deal.hiddenKnown, deal.rounds, null,
                                      deal.tradeIn, deal.tradeInCredit, deal.finance, bonus);
            CloseAll();
        }

        void OnDisable()
        {
            // Closed some other way: the deal still goes through (the customer has already agreed).
            if (pending && deal.customer && deal.customer.TargetCar) Complete();
        }
    }
}
