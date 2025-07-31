namespace FinMel.Domain.Entities.BankAccount;

/// <summary>
/// Strongly-typed identifier for BankAccount entity.
/// </summary>
public readonly record struct BankAccountId(Guid Value)
{
    public static BankAccountId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
