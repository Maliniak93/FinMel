using Domain.Common;
using Domain.Entities.Bank;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Common;
public class History : BaseEntity
{
    public History(DateTime date,
        decimal balance,
        int bankAccountId)
    {
        Date = date;
        Balance = balance;
        BankAccountId = bankAccountId;
    }
    [Column(TypeName = "Date")]
    public DateTime Date { get; private set; }
    public decimal Balance { get; private set; }
    public int BankAccountId { get; private set; }
    public BankAccount BankAccount { get; private set; }
}
