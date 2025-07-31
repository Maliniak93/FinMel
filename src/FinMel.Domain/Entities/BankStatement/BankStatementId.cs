namespace FinMel.Domain.Entities.BankStatement;

/// <summary>
/// Strongly-typed identifier for BankStatement entity.
/// </summary>
public readonly record struct BankStatementId(Guid Value)
{
    public static BankStatementId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
