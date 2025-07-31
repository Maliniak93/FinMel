using FinMel.Domain.Abstractions;

namespace FinMel.Domain.Entities.Currency;

/// <summary>
/// Represents a currency entity with a strongly-typed identifier.
/// </summary>
public sealed partial class Currency : Entity<CurrencyId>
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Symbol { get; init; }
}
