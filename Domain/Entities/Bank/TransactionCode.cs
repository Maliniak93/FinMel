using Domain.Common;

namespace Domain.Entities.Bank;

public class TransactionCode : BaseAuditableEntity
{
    public TransactionCode(string code,
        string description,
        bool isExpenseIncome = default)
    {
        Code = code;
        Description = description;
        IsExpenseIncome = isExpenseIncome;
    }
    public string Code { get; private set; }
    public string Description { get; private set; }
    public bool IsExpenseIncome { get; private set; }

    public void UpdateTransactionCode(string code,
        string description,
        bool isExpenseIncome)
    {
        Code = code;
        Description = description;
        IsExpenseIncome = isExpenseIncome;
    }
}