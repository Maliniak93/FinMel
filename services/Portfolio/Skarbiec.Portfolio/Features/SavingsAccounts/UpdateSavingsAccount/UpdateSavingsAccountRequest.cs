using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;

public sealed record UpdateSavingsAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    /// <summary>IKE/IKZE: no Belka tax.</summary>
    public bool TaxExempt { get; init; }
}
