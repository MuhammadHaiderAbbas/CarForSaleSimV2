using CarSales.Foundation;
using System;
using UnityEngine;

namespace CarSales
{
    /// <summary>Loans: borrow up to a limit (grows with reputation), pay daily interest, repay any time.</summary>
    public class BankManager : MonoBehaviour
    {
        [SerializeField] float dailyInterest = 0.02f;     // 2% per day
        [SerializeField] int baseLimit = 25000;
        [SerializeField] int limitPerReputationPoint = 1000;

        public int Loan { get; private set; }
        public float DailyInterestRate => dailyInterest;
        public int LoanLimit => baseLimit + Mathf.RoundToInt(limitPerReputationPoint * CarSalesServices.Economy.Reputation);
        public int Available => Mathf.Max(0, LoanLimit - Loan);
        public int TomorrowInterest => Mathf.CeilToInt(Loan * dailyInterest);
        public event Action OnLoanChanged;

        void Awake() => CarSalesServices.Bank = this;

        public bool Borrow(int amount, out string message)
        {
            amount = Mathf.Min(amount, Available);
            if (amount <= 0) { message = "Your loan limit is reached. Improve your reputation to borrow more."; return false; }
            Loan += amount;
            CarSalesServices.Economy.AddMoney(amount, TxCategory.LoanDrawdown, "Loan");
            OnLoanChanged?.Invoke();
            CarSalesServices.PlaySfx(Sfx.Coin);
            message = $"Borrowed ${amount:N0}. Interest: {dailyInterest * 100f:0.#}% per day.";
            return true;
        }

        public bool Repay(int amount, out string message)
        {
            amount = Mathf.Min(amount, Loan, CarSalesServices.Economy.Money);
            if (Loan <= 0) { message = "You have no loan."; return false; }
            if (amount <= 0 || !CarSalesServices.Economy.TrySpend(amount, TxCategory.LoanRepayment, "Loan repayment")) { message = "Not enough money to repay."; return false; }
            Loan -= amount;
            OnLoanChanged?.Invoke();
            CarSalesServices.PlaySfx(Sfx.Purchase);
            message = Loan == 0 ? "Loan fully repaid!" : $"Repaid ${amount:N0}. Remaining loan: ${Loan:N0}.";
            return true;
        }

        /// <summary>Called at the end of each day. Returns the interest charged.</summary>
        public int ChargeInterest()
        {
            if (Loan <= 0) return 0;
            int interest = TomorrowInterest;
            CarSalesServices.Economy.ForceSpend(interest, TxCategory.LoanInterest, $"Interest on {UIFactory.Money(Loan)} loan");
            return interest;
        }

        public void SetLoan(int loan)
        {
            Loan = Mathf.Max(0, loan);
            OnLoanChanged?.Invoke();
        }
    }
}
