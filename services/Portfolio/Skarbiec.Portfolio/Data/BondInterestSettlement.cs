using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class BondInterestSettlement : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    public required Guid AssetId { get; init; }

    public required int PeriodIndex { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal RatePercent { get; init; }
    public required int BondCount { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }

    public Guid? CreditTransactionId { get; init; }
    public Guid? TransferId { get; init; }
}
