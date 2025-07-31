namespace FinMel.Domain.Entities.Currency;

/// <summary>
/// Strongly-typed identifier for Currency entity.
/// </summary>
public readonly record struct CurrencyId(Guid Value)
{
    public static CurrencyId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
