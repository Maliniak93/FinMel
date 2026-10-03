using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Asset : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public decimal Quantity { get; set; }

    public required AssetValuationMode ValuationMode { get; set; }

    public decimal? ManualValueAmount { get; set; }

    public DateOnly? ManualValueDate { get; set; }

    public Guid? InstrumentId { get; set; }

    // App-managed rather than xmin, which does not move when only the portfolio's archive flag changes.
    public long Version { get; set; }

    public bool IsArchived { get; set; }
}
