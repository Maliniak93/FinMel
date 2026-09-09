using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Portfolio;

/// <summary>
/// Local mirror of Portfolio's <c>PositionForValuationResponse</c> wire shape (services don't
/// reference each other's projects, ADR-003) — only the fields the valuation algorithm actually
/// needs; <c>ManualValueDate</c> is a UI-only reminder, not part of the formula, so it's omitted.
/// </summary>
public sealed record PositionForValuation
{
    public required Guid UserId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }
    public decimal? ManualValueAmount { get; init; }
}
