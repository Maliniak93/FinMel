using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TreasuryBond : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public required string SeriesCode { get; set; }
    public required TreasuryBondType Type { get; set; }
    public required DateOnly PurchaseDate { get; set; }
    public required int BondCount { get; set; }
    public required decimal PurchasePricePerBond { get; set; }
    public required decimal FirstPeriodRatePercent { get; set; }
    public decimal? MarginPercent { get; set; }
    public required decimal EarlyRedemptionFeePerBond { get; set; }
    public bool TaxExempt { get; set; }

    public required DateOnly MaturityDate { get; set; }

    public Guid? SwappedFromAssetId { get; set; }
}
