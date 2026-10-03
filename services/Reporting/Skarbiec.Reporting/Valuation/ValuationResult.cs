namespace Skarbiec.Reporting.Valuation;

public sealed record ValuationResult
{
    /// <summary>One line per input position, in input order.</summary>
    public required IReadOnlyList<ValuedPosition> Lines { get; init; }

    public required decimal TotalPln { get; init; }

    /// <summary>Any line used a quote or rate more than 7 days before the snapshot date, or had none.</summary>
    public required bool IsStale { get; init; }
}
