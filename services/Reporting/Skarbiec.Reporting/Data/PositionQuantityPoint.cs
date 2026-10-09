namespace Skarbiec.Reporting.Data;

/// <summary>Quantity at the end of Date, after all of that day's transactions.</summary>
public sealed record PositionQuantityPoint
{
    public required DateOnly Date { get; init; }
    public required decimal Quantity { get; init; }
}
