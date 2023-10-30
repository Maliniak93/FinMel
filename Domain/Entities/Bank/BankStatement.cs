using Domain.Common;
using Domain.Entities.Files;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Bank;
public class BankStatement : BaseAuditableEntity
{
    private readonly List<StatementTransaction> _statementTransactions = new();
    public BankStatement()
    {

    }
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
