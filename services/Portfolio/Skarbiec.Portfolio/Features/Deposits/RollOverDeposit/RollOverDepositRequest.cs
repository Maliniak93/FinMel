using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;

/// <summary>A Due deposit needs GrossInterest and Tax, a Settled one must omit them; the handler checks which applies.</summary>
public sealed record RollOverDepositRequest : IValidatableObject
{
    [Range(typeof(decimal), "0", "100")]
    public required decimal AnnualInterestRatePercent { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? GrossInterest { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? Tax { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The columns are numeric(7,4) and numeric(18,2): a finer value is rejected rather than silently rounded.
        if (decimal.Round(AnnualInterestRatePercent, 4) != AnnualInterestRatePercent)
        {
            yield return new ValidationResult(
                "The interest rate can't have more than 4 decimal places.", [nameof(TermDeposit.AnnualInterestRatePercent)]);
        }

        if (GrossInterest is { } gross && decimal.Round(gross, 2) != gross)
        {
            yield return new ValidationResult(
                "The gross interest can't have more than 2 decimal places.", [nameof(GrossInterest)]);
        }

        if (Tax is { } tax && decimal.Round(tax, 2) != tax)
        {
            yield return new ValidationResult("The tax can't have more than 2 decimal places.", [nameof(Tax)]);
        }

        if (Tax > GrossInterest)
        {
            yield return new ValidationResult("The tax can't exceed the gross interest.", [nameof(Tax)]);
        }
    }
}
