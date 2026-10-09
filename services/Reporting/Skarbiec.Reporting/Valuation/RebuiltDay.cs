namespace Skarbiec.Reporting.Valuation;

/// <summary>One day of a rebuild that has a snapshot; a day with neither lines nor an archived portfolio is absent.</summary>
public sealed record RebuiltDay
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<ValuedPosition> Lines { get; init; }
    public required decimal TotalPln { get; init; }
    public required bool IsStale { get; init; }
}
