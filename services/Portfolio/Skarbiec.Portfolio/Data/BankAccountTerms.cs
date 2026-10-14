using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class BankAccountTerms : IUserOwned
{
    public required Guid AccountId { get; init; }
    public Guid UserId { get; set; }

    public required BankVariant Variant { get; set; }

    public string? BankName { get; set; }

    public required decimal AnnualInterestRatePercent { get; set; }
}
