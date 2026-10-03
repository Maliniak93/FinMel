using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.AddDeposit;

public sealed record AddDepositRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    /// <summary>Immutable once the deposit exists.</summary>
    [Required, SupportedCurrency]
    public required string Currency { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal Principal { get; init; }

    public required DateOnly StartDate { get; init; }

    /// <summary>At most 3650 days or 120 months.</summary>
    [Range(1, DepositTermsValidation.MaxTermDays)]
    public required int TermLength { get; init; }

    public required DepositTermUnit TermUnit { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    public required DepositCapitalization Capitalization { get; init; }

    /// <summary>IKE/IKZE: no Belka tax.</summary>
    public bool TaxExempt { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal EarlyBreakInterestLossPercent { get; init; } = 100m;

    /// <summary>The Cash asset the principal moves out of; omitted for new money from outside the app.</summary>
    public Guid? FundingAssetId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        DepositTermsValidation.Validate(
            Principal, TermLength, TermUnit, AnnualInterestRatePercent, Capitalization, EarlyBreakInterestLossPercent);
}
