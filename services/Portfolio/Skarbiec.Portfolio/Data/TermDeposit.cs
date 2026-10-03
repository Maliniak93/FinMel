using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TermDeposit : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public string? BankName { get; set; }
    public required decimal Principal { get; set; }
    public required DateOnly StartDate { get; set; }
    public required int TermLength { get; set; }
    public required DepositTermUnit TermUnit { get; set; }

    public required DateOnly MaturityDate { get; set; }

    public required decimal AnnualInterestRatePercent { get; set; }
    public required DepositCapitalization Capitalization { get; set; }

    public bool TaxExempt { get; set; }

    public decimal EarlyBreakInterestLossPercent { get; set; } = 100m;

    public DateOnly? SettledOn { get; set; }

    public decimal? SettledGrossInterest { get; set; }

    public decimal? SettledTax { get; set; }

    public int RolloverCount { get; set; }
}
