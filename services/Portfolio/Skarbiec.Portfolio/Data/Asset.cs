using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Asset : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public Guid? AccountId { get; init; }
    public Guid? CashPlaceId { get; init; }
    public required AssetClass AssetClass { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public decimal Quantity { get; set; }

    public required AssetValuationMode ValuationMode { get; set; }

    public Guid? InstrumentId { get; set; }

    // App-managed rather than xmin, which does not move when only the archive flag changes.
    public long Version { get; set; }

    public bool IsArchived { get; set; }
}
