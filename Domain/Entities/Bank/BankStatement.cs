using Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Entities.File;

namespace Domain.Entities.Bank;
public class BankStatement : BaseAuditableEntity
{
    private readonly List<StatementTransaction> _statementTransactions = new();
    // ReSharper disable once NotNullOrRequiredMemberIsNotInitialized
    public BankStatement()
    {

    }
    // ReSharper disable once NotNullOrRequiredMemberIsNotInitialized
    public BankStatement(StatementFile statementFile,
        string statementNumber,
        DateTime statementFrom,
        decimal beginValue,
        DateTime statementTo,
        decimal endValue,
        int bankAccountId)
    {
        StatementFile = statementFile;
        StatementNumber = statementNumber;
        StatementFrom = statementFrom;
        BeginValue = beginValue;
        StatementTo = statementTo;
        EndValue = endValue;
        BankAccountId = bankAccountId;
    }

    public StatementFile StatementFile { get; private set; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    public int StatementFileId { get; private set; }
    public string StatementNumber { get; private set; }
    [Column(TypeName = "Date")]
    public DateTime StatementFrom { get; private set; }
    public decimal BeginValue { get; private set; }
    [Column(TypeName = "Date")]
    public DateTime StatementTo { get; private set; }
    public decimal EndValue { get; private set; }
    public IReadOnlyCollection<StatementTransaction> StatementTransactions => _statementTransactions;
    public int BankAccountId { get; private set; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    public BankAccount BankAccount { get; private set; }

    public void AddStatementTransaction(StatementTransaction statementTransaction)
    {
        _statementTransactions.Add(statementTransaction);
    }

    public void AddStatementTransactions(List<StatementTransaction> statementTransactions)
    {
        _statementTransactions.AddRange(statementTransactions);
    }
}
