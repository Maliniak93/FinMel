using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

/// <summary>
/// One portfolio's valuation on one date (03-domain-model.md §Reporting) — built by the
/// <c>DailyPricesSynced</c> consumer (T2.11) from Portfolio's positions and MarketData's prices/FX,
/// never written from a user request. Unique on (<see cref="PortfolioId"/>, <see cref="Date"/>): a
/// redelivered event or a manual rerun overwrites the same row instead of duplicating it — that,
/// plus the inbox, is what makes the consumer idempotent.
/// </summary>
public sealed class ValuationSnapshot : IUserOwned
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Set directly by the consumer from Portfolio's own data, not stamped by
    /// <see cref="UserOwnedSaveInterceptor"/> — see that type's remarks for why a system-context
    /// writer covering many users in one message needs the escape hatch.
    /// </summary>
    public Guid UserId { get; set; }

    public required Guid PortfolioId { get; init; }
    public required DateOnly Date { get; set; }
    public decimal TotalPln { get; set; }

    /// <summary>Per-<c>AssetClass</c> value in PLN, serialized by <see cref="ValuationBreakdown"/>. First JSONB column in the project (T2.11) — no existing local precedent, see that type's doc comment.</summary>
    public required string BreakdownJson { get; set; }

    /// <summary>True when any position priced into this snapshot used a quote/rate more than 7 days before <see cref="Date"/> (03-domain-model.md §Valuation algorithm).</summary>
    public bool IsStale { get; set; }
}
