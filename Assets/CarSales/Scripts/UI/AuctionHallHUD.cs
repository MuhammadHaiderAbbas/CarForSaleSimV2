using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Shown while you stand in the auction hall during a sale: the lot on the rostrum, the current bid and who
    /// holds it, the auctioneer's calls, and "[E] Raise paddle" for the next increment. Added by AuctionManager.
    /// </summary>
    public class AuctionHallHUD : MonoBehaviour
    {
        RectTransform panel;
        TextMeshProUGUI lotText, bidText, leaderText, callText, keyText;
        Image callBar;

        void Build()
        {
            var ui = CarSalesServices.UI;
            if (!ui) return;
            var p = UIFactory.Panel(ui.transform, "AuctionHallHUD", UIFactory.HudBgStrong, 1.6f);
            panel = p.rectTransform;
            p.raycastTarget = false;
            UIFactory.Place(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(720f, 210f));
            var v = UIFactory.Vertical(p, 16, 2f);
            v.padding = new RectOffset(22, 22, 12, 12);
            lotText = UIFactory.Text(p.transform, "", 17, UIFactory.Accent, TextAlignmentOptions.Left, true);
            UIFactory.LE(lotText, h: 24);
            bidText = UIFactory.Text(p.transform, "", 44, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(bidText, h: 52);
            leaderText = UIFactory.Text(p.transform, "", 18, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(leaderText, h: 24);
            callText = UIFactory.Text(p.transform, "", 17, UIFactory.TextDim, TextAlignmentOptions.Left);
            UIFactory.LE(callText, h: 24);
            var bar = UIFactory.ProgressBar(p.transform, UIFactory.Good, 6f);
            callBar = bar.fill;
            keyText = UIFactory.Text(p.transform, "", 17, UIFactory.TextMain, TextAlignmentOptions.Left, true);
            UIFactory.LE(keyText, h: 26);
        }

        void LateUpdate()
        {
            var a = CarSalesServices.Auction;
            var lot = a ? a.LiveLot : null;
            bool show = a && (lot != null || !string.IsNullOrEmpty(a.LiveMessage)) && AuctionManager.PlayerInHall()
                        && !(CarSalesServices.UI && CarSalesServices.UI.IsMenuOpen);
            if (!panel && show) Build();
            if (!panel) return;
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;
            panel.SetAsLastSibling();
            if (lot == null)
            {
                lotText.text = "AUCTION";
                bidText.text = "";
                leaderText.text = a.LiveMessage;
                callText.text = "Next lot coming up...";
                keyText.text = "";
                return;
            }
            var car = a.CarOf(lot);
            lotText.text = $"LOT {lot.lotNo}  -  {car?.Name}  -  grade {lot.report.grade}  -  guide {UIFactory.Money(lot.estimate)}";
            bool noBids = string.IsNullOrEmpty(lot.leader);
            bidText.text = noBids ? $"{UIFactory.Money(lot.startPrice)}?" : UIFactory.Money(lot.currentBid);
            bool mine = lot.leader == AuctionRules.PlayerId;
            leaderText.text = noBids ? "No bids yet" : mine ? $"<color={UIFactory.Hex(UIFactory.Good)}>The bid is with you!</color>" : $"With {a.BidderName(lot.leader)}";
            callText.text = a.LiveMessage;
            int stage = a.LiveCall == AuctionManager.Call.GoingOnce ? 1 : a.LiveCall == AuctionManager.Call.GoingTwice ? 2 : 0;
            callBar.rectTransform.anchorMax = new Vector2(stage / 3f + 0.02f, 1f);
            callBar.color = stage == 0 ? UIFactory.Good : stage == 1 ? UIFactory.Warn : UIFactory.Bad;
            var acc = a.Account("hall");
            keyText.text = acc == null ? $"<color={UIFactory.Hex(UIFactory.Warn)}>Register at the desk to bid</color>"
                         : mine ? $"Paddle {acc.buyerNo}" + (lot.playerMax > 0 ? $"   -   absentee max {UIFactory.Money(lot.playerMax)}" : "")
                         : $"[E] Raise paddle {acc.buyerNo}: bid {UIFactory.Money(AuctionRules.NextBid(lot))}";
        }

        void OnDestroy() { if (panel) Destroy(panel.gameObject); }
    }
}
