using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;

/// <summary>
/// The account's terms. The currency is immutable and the balance moves only through its transactions,
/// so neither is here.
/// </summary>
public sealed record UpdateSavingsAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    /// <summary>IKE/IKZE — no Belka tax.</summary>
    public bool TaxExempt { get; init; }
}
