namespace FinMel.Domain.Entities.ExchangeRate;

/// <summary>
/// Strongly-typed identifier for ExchangeRate entity.
/// </summary>
public readonly record struct ExchangeRateId(Guid Value)
{
    public static ExchangeRateId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}