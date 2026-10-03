using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// Unique on (PortfolioId, Date), so a redelivery or rerun overwrites; the per-asset detail lives in the AssetValuation lines.
public sealed class ValuationSnapshot : IUserOwned
{
    public required Guid Id { get; init; }

    // Set by the consumer from the Position row, not stamped by UserOwnedSaveInterceptor.
    public Guid UserId { get; set; }

    public required Guid PortfolioId { get; init; }
    public required DateOnly Date { get; set; }
    public decimal TotalPln { get; set; }

    // Any position used a quote or rate more than 7 days before Date.
    public bool IsStale { get; set; }
}
