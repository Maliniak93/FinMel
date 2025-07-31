using FinMel.Domain.Abstractions;

namespace FinMel.Domain.Entities.BankStatement;

public sealed class BankStatement : Entity<BankStatementId>
{
    public int MyProperty { get; set; }
}
