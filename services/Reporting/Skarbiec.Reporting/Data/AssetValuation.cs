using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// History, not current state: AssetRemoved keeps these lines and only PortfolioDeleted sweeps them.
public sealed class AssetValuation : IUserOwned
{
    public required Guid Id { get; init; }

    // Set by the consumer from the Position row, not stamped by UserOwnedSaveInterceptor.
    public Guid UserId { get; set; }

    public required Guid PortfolioId { get; init; }
    public required Guid AssetId { get; init; }
    public required DateOnly Date { get; init; }
    public required AssetClass AssetClass { get; set; }
    public required decimal Quantity { get; set; }

    // Market mode only; null for manual and currency-valued lines, and for a market asset with no quote.
    public decimal? PriceUsed { get; set; }

    // The date of PriceUsed: how far back the last-known fallback reached.
    public DateOnly? PriceDate { get; set; }

    // 1 for a PLN line; null when no rate resolved, which also makes the line stale.
    public decimal? FxRateUsed { get; set; }

    public required decimal ValuePln { get; set; }

    // The quote or rate is more than 7 days older than Date, or missing.
    public required bool IsStale { get; set; }
}
