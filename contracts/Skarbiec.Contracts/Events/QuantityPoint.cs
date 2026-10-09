namespace Skarbiec.Contracts.Events;

/// <summary>Quantity at the end of Date, after all of that day's transactions.</summary>
public sealed record QuantityPoint
{
    public required DateOnly Date { get; init; }
    public required decimal Quantity { get; init; }
}
