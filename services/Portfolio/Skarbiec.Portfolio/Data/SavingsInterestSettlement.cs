using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class SavingsInterestSettlement : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    public required Guid AssetId { get; init; }

    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }

    public Guid? TransactionId { get; init; }
}
