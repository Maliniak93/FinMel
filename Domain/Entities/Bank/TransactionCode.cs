using Domain.Common;
using Domain.Enums;

namespace Domain.Entities.Bank;

public class TransactionCode : BaseEntity
{
    public TransactionCode(string code,
        string description,
        TransactionTypes type)
    {
        Code = code;
        Description = description;
        Type = type;
    }
    public string Code { get; private set; }
    public string Description { get; private set; }
    public TransactionTypes Type { get; private set; }

    public void UpdateTransactionCode(string code,
        string description,
        TransactionTypes type)
    {
        Code = code;
        Description = description;
        Type = type;
    }
}
