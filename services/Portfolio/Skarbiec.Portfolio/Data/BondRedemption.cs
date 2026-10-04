using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public enum BondRedemptionKind
{
    Maturity,

    Swap,
}

public sealed class BondRedemption : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    public required Guid AssetId { get; init; }

    public required BondRedemptionKind Kind { get; init; }
    public required DateOnly Date { get; init; }
    public required int BondCount { get; init; }
    public required decimal CapitalisedInterest { get; init; }
    public required decimal DiscountIncome { get; init; }
    public required decimal Tax { get; init; }
    public required decimal Proceeds { get; init; }

    public Guid? CreditTransactionId { get; init; }
    public Guid? ChargeTransactionId { get; init; }
    public Guid? CashTransferId { get; init; }
    public Guid? SwapTargetAssetId { get; init; }
    public Guid? SwapTransferId { get; init; }
}
