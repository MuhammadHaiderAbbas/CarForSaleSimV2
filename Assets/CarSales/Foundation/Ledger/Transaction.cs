using System;
using System.Collections.Generic;

namespace CarSales.Foundation
{
    /// <summary>
    /// What a transaction was for. Values are stored in save files as numbers:
    /// only ever ADD new entries at the end, never reorder or remove.
    /// </summary>
    public enum TxCategory
    {
        Other = 0,
        VehiclePurchase = 1,
        VehicleSale = 2,
        Delivery = 3,
        Inspection = 4,
        Repair = 5,
        Cleaning = 6,
        Fuel = 7,
        Transport = 8,      // tow trucks, taxis
        Auction = 9,
        Upgrades = 10,
        PropertyPurchase = 11,
        PropertyIncome = 12,
        Rent = 13,
        LoanDrawdown = 14,
        LoanRepayment = 15,
        LoanInterest = 16,
        Fines = 17,
        Rewards = 18,
        Debug = 19,
        Adjustment = 20,
        Parts = 21,          // spare parts (workshop)
        Labour = 22,         // garage labour
        Paperwork = 23,      // MOT, logbook, history checks, keys
        FinanceSettlement = 24,
        Wages = 25,
        Vat = 26,           // VAT on auction fees
        Deposits = 27,      // refundable auction deposits
        FinanceCommission = 28,
        Warranty = 29,      // warranties sold (+) and claims paid (-)
        Utilities = 30,
        BusinessRates = 31,
        Insurance = 32,     // premiums (-) and claims paid out to you (+)
        Marketing = 33,
        Security = 34,
        PropertyDevelopment = 35,
    }

    public static class TxCategories
    {
        public static readonly int Count = Enum.GetValues(typeof(TxCategory)).Length;

        public static string DisplayName(TxCategory c)
        {
            switch (c)
            {
                case TxCategory.VehiclePurchase: return "Car purchases";
                case TxCategory.VehicleSale: return "Car sales";
                case TxCategory.Delivery: return "Delivery";
                case TxCategory.Inspection: return "Inspections";
                case TxCategory.Repair: return "Repairs";
                case TxCategory.Cleaning: return "Car wash";
                case TxCategory.Fuel: return "Fuel";
                case TxCategory.Transport: return "Tow & taxi";
                case TxCategory.Auction: return "Auctions";
                case TxCategory.Upgrades: return "Upgrades";
                case TxCategory.PropertyPurchase: return "Property purchases";
                case TxCategory.PropertyIncome: return "Property income";
                case TxCategory.Rent: return "Rent & bills";
                case TxCategory.LoanDrawdown: return "Loans taken";
                case TxCategory.LoanRepayment: return "Loan repayments";
                case TxCategory.LoanInterest: return "Loan interest";
                case TxCategory.Fines: return "Fines & fees";
                case TxCategory.Rewards: return "Goal rewards";
                case TxCategory.Debug: return "Debug";
                case TxCategory.Adjustment: return "Adjustments";
                case TxCategory.Parts: return "Spare parts";
                case TxCategory.Labour: return "Garage labour";
                case TxCategory.Paperwork: return "MOT & paperwork";
                case TxCategory.FinanceSettlement: return "Finance settlements";
                case TxCategory.Wages: return "Wages";
                case TxCategory.Vat: return "VAT";
                case TxCategory.Deposits: return "Deposits";
                case TxCategory.FinanceCommission: return "Finance commission";
                case TxCategory.Warranty: return "Warranties";
                case TxCategory.Utilities: return "Utilities";
                case TxCategory.BusinessRates: return "Business rates";
                case TxCategory.Insurance: return "Insurance";
                case TxCategory.Marketing: return "Marketing";
                case TxCategory.Security: return "Security";
                case TxCategory.PropertyDevelopment: return "Property development";
                default: return "Other";
            }
        }

        /// <summary>Loans move cash but are not income or expenses in a profit report.</summary>
        public static bool IsFinancing(TxCategory c) => c == TxCategory.LoanDrawdown || c == TxCategory.LoanRepayment;
    }

    /// <summary>One money movement. Amount is signed: positive = money in, negative = money out.</summary>
    [Serializable]
    public class Transaction
    {
        public long id;
        public int day;
        public float hour;
        public int amount;
        public TxCategory category;
        public string memo;
        public string refId;     // e.g. car instance id, property id

        public bool IsIncome => amount > 0;
    }

    /// <summary>Everything the ledger needs to be saved (serialised by the game's JSON serializer).</summary>
    [Serializable]
    public class LedgerState
    {
        public int version = 1;
        public long nextId = 1;
        public int openingBalance;        // balance before the oldest kept entry
        public int openingDay = 1;
        public List<Transaction> entries = new List<Transaction>();
        public long[] lifetimeIn = new long[0];    // per category, never compacted
        public long[] lifetimeOut = new long[0];
    }
}
