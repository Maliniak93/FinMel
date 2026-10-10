namespace Skarbiec.Contracts.Events;

public sealed record DailyPricesSynced
{
    public required DateOnly Date { get; init; }
    public required PriceSyncKind Kind { get; init; }
}
