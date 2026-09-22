using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

/// <summary>
/// One asset's value on one date (03-domain-model.md §Reporting) — the per-asset line the
/// <c>DailyPricesSynced</c> consumer writes alongside each <see cref="ValuationSnapshot"/>, and the
/// basis every later insight (P/L, emergency fund, goals) computes from instead of re-pricing
/// history. Replaces <c>ValuationSnapshot.BreakdownJson</c>: the dashboard's per-class breakdown is
/// now a <c>GROUP BY AssetClass</c> over these rows (spec-03).
/// </summary>
/// <remarks>
/// History, not current state: an <c>AssetRemoved</c> deletes the <see cref="Position"/> but keeps
/// these lines (spec-03 design decision 2) — deleting an asset must not silently change what last
/// month's net worth was. Only <c>PortfolioDeleted</c> sweeps them (design decision 1).
/// </remarks>
public sealed class AssetValuation : IUserOwned
{
    public required Guid Id { get; init; }

    /// <summary>Set directly by the consumer from the <see cref="Position"/> row, not stamped by <see cref="UserOwnedSaveInterceptor"/> — see that type's remarks.</summary>
    public Guid UserId { get; set; }

    public required Guid PortfolioId { get; init; }
    public required Guid AssetId { get; init; }
    public required DateOnly Date { get; init; }
    public required AssetClass AssetClass { get; set; }
    public required decimal Quantity { get; set; }

    /// <summary>Market mode only — the last known quote at or before <see cref="Date"/>; null for manual and currency-valued lines, and for a market asset with no quote at all.</summary>
    public decimal? PriceUsed { get; set; }

    /// <summary>The date of <see cref="PriceUsed"/> — how far back the "last known price" fallback had to reach.</summary>
    public DateOnly? PriceDate { get; set; }

    /// <summary>The currency→PLN rate applied (1 for a PLN line); null when no rate could be resolved at all, which is also an <see cref="IsStale"/> line.</summary>
    public decimal? FxRateUsed { get; set; }

    public required decimal ValuePln { get; set; }

    /// <summary>True when the quote or rate used is more than 7 days older than <see cref="Date"/>, or was missing entirely (03-domain-model.md §Valuation algorithm).</summary>
    public required bool IsStale { get; set; }
}
