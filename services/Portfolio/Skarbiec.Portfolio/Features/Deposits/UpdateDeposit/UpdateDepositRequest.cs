using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;

/// <summary>The same terms as <see cref="AddDeposit.AddDepositRequest"/>, minus the currency, which is immutable.</summary>
public sealed record UpdateDepositRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal Principal { get; init; }

    public required DateOnly StartDate { get; init; }

    /// <summary>At most 3650 days or 120 months — the months cap is checked in <see cref="Validate"/>.</summary>
    [Range(1, DepositTermsValidation.MaxTermDays)]
    public required int TermLength { get; init; }

    public required DepositTermUnit TermUnit { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    public required DepositCapitalization Capitalization { get; init; }

    /// <summary>IKE/IKZE — no Belka tax.</summary>
    public bool TaxExempt { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal EarlyBreakInterestLossPercent { get; init; } = 100m;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        DepositTermsValidation.Validate(
            Principal, TermLength, TermUnit, AnnualInterestRatePercent, Capitalization, EarlyBreakInterestLossPercent);
}
