using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class SavingsAccount : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public string? BankName { get; set; }

    public required decimal AnnualInterestRatePercent { get; set; }

    public bool TaxExempt { get; set; }
}
