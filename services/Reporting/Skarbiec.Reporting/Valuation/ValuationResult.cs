namespace Skarbiec.Reporting.Valuation;

public sealed record ValuationResult
{
    /// <summary>One line per input position, in input order (spec-03) — the source of both <see cref="TotalPln"/> and <see cref="IsStale"/>, and of the dashboard's per-asset-class breakdown once persisted.</summary>
    public required IReadOnlyList<ValuedPosition> Lines { get; init; }

    public required decimal TotalPln { get; init; }

    /// <summary>True when any line is stale — i.e. some position used a quote/rate more than 7 days before the snapshot date, or had none at all (03-domain-model.md §Valuation algorithm).</summary>
    public required bool IsStale { get; init; }
}
