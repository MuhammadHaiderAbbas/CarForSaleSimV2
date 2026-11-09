using CarSales.Foundation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    public class BankUI : ComputerPage
    {
        [SerializeField] int smallAmount = 5000, largeAmount = 20000;
        [SerializeField] int statementRows = 40;
        TextMeshProUGUI loanText, limitText, availableText, interestText, statementSummary;
        Button borrowSmall, borrowLarge, repaySmall, repayAll;
        RectTransform statementList;

        public override void Build()
        {
            UIFactory.Vertical(Area, 0, 16f);
            var cards = UIFactory.HRow(Area, 112, 14);
            loanText = Card(cards, "Current loan");
            availableText = Card(cards, "Available to borrow");
            interestText = Card(cards, "Interest tonight");

            var info = UIFactory.Panel(Area, "Info", UIFactory.CardBg, 1.4f);
            UIFactory.Vertical(info, 20, 6f);
            limitText = UIFactory.Text(info.transform, "", 19, UIFactory.TextMain, TextAlignmentOptions.Left);

            var row1 = UIFactory.HRow(Area, 58, 14);
            borrowSmall = UIFactory.Btn(row1, $"Borrow {UIFactory.Money(smallAmount)}", UIFactory.Good, () => Borrow(smallAmount), 56f);
            borrowLarge = UIFactory.Btn(row1, $"Borrow {UIFactory.Money(largeAmount)}", UIFactory.Good, () => Borrow(largeAmount), 56f);
            var row2 = UIFactory.HRow(Area, 58, 14);
            repaySmall = UIFactory.Btn(row2, $"Repay {UIFactory.Money(smallAmount)}", UIFactory.Accent, () => Repay(smallAmount), 56f);
            repayAll = UIFactory.Btn(row2, "Repay All", UIFactory.Accent, () => Repay(int.MaxValue), 56f);

            // Statement: every money movement, newest first (from the ledger)
            var head = UIFactory.HRow(Area, 28, 10, "StatementHeader");
            var title = UIFactory.Text(head, "ACCOUNT STATEMENT", 16, UIFactory.TextDim, TextAlignmentOptions.Left, true);
            UIFactory.LE(title, flexW: 1);
            statementSummary = UIFactory.Text(head, "", 16, UIFactory.TextDim, TextAlignmentOptions.Right);
            UIFactory.LE(statementSummary, flexW: 2);
            var box = UIFactory.Panel(Area, "Statement", UIFactory.CardBg, 1.4f);
            UIFactory.LE(box, flexH: 1);
            UIFactory.Vertical(box, 10, 0f);
            statementList = UIFactory.ScrollList(box.transform, 2f, "StatementScroll");
        }

        void RefreshStatement()
        {
            var eco = CarSalesServices.Economy;
            var day = CarSalesServices.DayCycle;
            UIFactory.ClearChildren(statementList);
            var recent = eco.Ledger.Recent(statementRows);
            if (recent.Count == 0)
            {
                var empty = UIFactory.Text(statementList, "No transactions yet.", 17, UIFactory.TextDim, TextAlignmentOptions.Left);
                UIFactory.LE(empty, h: 30);
            }
            foreach (var t in recent)
            {
                var row = UIFactory.HRow(statementList, 30, 12, "Tx");
                int h = Mathf.FloorToInt(t.hour), m = Mathf.FloorToInt((t.hour - h) * 60f);
                UIFactory.LE(UIFactory.Text(row, $"{GameCalendar.WeekdayShort(t.day)} D{t.day} {h:00}:{m:00}", 15, UIFactory.TextDim), w: 130);
                UIFactory.LE(UIFactory.Text(row, TxCategories.DisplayName(t.category), 15, UIFactory.TextMain, TextAlignmentOptions.Left, true), w: 170);
                var memo = UIFactory.Text(row, t.memo, 15, UIFactory.TextDim);
                memo.overflowMode = TextOverflowModes.Ellipsis;
                UIFactory.LE(memo, flexW: 1);
                var amount = UIFactory.Text(row, (t.amount > 0 ? "+" : "") + UIFactory.Money(t.amount), 16,
                                            t.amount > 0 ? UIFactory.Good : UIFactory.Bad, TextAlignmentOptions.Right, true);
                UIFactory.LE(amount, w: 140);
            }
            int today = day ? day.CurrentDay : 1;
            var s = eco.Ledger.Summarize(today, today);
            statementSummary.text = $"Today: <color={UIFactory.Hex(UIFactory.Good)}>+{UIFactory.Money(s.income)}</color>  " +
                                    $"<color={UIFactory.Hex(UIFactory.Bad)}>-{UIFactory.Money(s.expenses)}</color>   Balance <b>{UIFactory.Money(eco.Ledger.Balance)}</b>";
        }

        static TextMeshProUGUI Card(Transform parent, string label)
        {
            var card = UIFactory.Panel(parent, label, UIFactory.CardBg, 1.4f);
            var v = UIFactory.Vertical(card, 18, 6f);
            v.padding = new RectOffset(20, 18, 18, 16);
            UIFactory.Text(card.transform, label.ToUpper(), 14, UIFactory.TextDim, TextAlignmentOptions.Left, true);
            return UIFactory.Text(card.transform, "-", 34, UIFactory.TextMain, TextAlignmentOptions.Left, true);
        }

        void Borrow(int amount)
        {
            if (CarSalesServices.Bank.Borrow(amount, out string msg)) CarSalesServices.UI.Success("Loan approved", msg);
            else CarSalesServices.UI.Error(msg);
            Refresh();
        }

        void Repay(int amount)
        {
            if (CarSalesServices.Bank.Repay(amount, out string msg)) CarSalesServices.UI.Success("Payment received", msg);
            else CarSalesServices.UI.Error(msg);
            Refresh();
        }

        public override void Refresh()
        {
            var bank = CarSalesServices.Bank;
            loanText.text = UIFactory.Money(bank.Loan);
            loanText.color = bank.Loan > 0 ? UIFactory.Warn : UIFactory.Good;
            availableText.text = UIFactory.Money(bank.Available);
            interestText.text = UIFactory.Money(bank.TomorrowInterest);
            interestText.color = bank.TomorrowInterest > 0 ? UIFactory.Bad : UIFactory.TextMain;
            limitText.text =
                $"Loan limit: <b>{UIFactory.Money(bank.LoanLimit)}</b>  (grows with your reputation)\n" +
                $"Interest: <b>{bank.DailyInterestRate * 100f:0.#}% per day</b>, charged at closing time.\n" +
                $"<color={UIFactory.Hex(UIFactory.Warn)}>Ending 3 days in a row with negative cash means bankruptcy.</color>";
            borrowSmall.interactable = borrowLarge.interactable = bank.Available > 0;
            repaySmall.interactable = repayAll.interactable = bank.Loan > 0 && CarSalesServices.Economy.Money > 0;
            RefreshStatement();
        }
    }
}
