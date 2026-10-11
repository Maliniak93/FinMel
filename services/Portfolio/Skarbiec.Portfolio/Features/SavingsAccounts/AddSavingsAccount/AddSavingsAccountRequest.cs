using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;

public sealed record AddSavingsAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    /// <summary>Immutable once the account exists.</summary>
    [Required, SupportedCurrency]
    public required string Currency { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    /// <summary>IKE/IKZE: no Belka tax.</summary>
    public bool TaxExempt { get; init; }

    /// <summary>Omitted: the account starts at 0.</summary>
    public OpeningDepositRequest? OpeningDeposit { get; init; }
}

/// <summary>Its date must not be after today (Europe/Warsaw), which the handler checks.</summary>
public sealed record OpeningDepositRequest
{
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal Amount { get; init; }

    public required DateOnly Date { get; init; }
}
