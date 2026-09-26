using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Deposits.SettleDeposit;

/// <summary>
/// What the bank actually paid at maturity (term-deposits-settlement). The date rules need the
/// deposit and today's Europe/Warsaw date (<c>StartDate ≤ SettledOn ≤ today</c>), so the handler
/// checks those.
/// </summary>
public sealed record SettleDepositRequest : IValidatableObject
{
    public required DateOnly SettledOn { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public required decimal GrossInterest { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public required decimal Tax { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Both are stored as numeric(18,2): a finer value would be rounded silently by the database
        // while the net-interest transaction kept it, so it is rejected instead.
        if (decimal.Round(GrossInterest, 2) != GrossInterest)
        {
            yield return new ValidationResult(
                "The gross interest can't have more than 2 decimal places.", [nameof(GrossInterest)]);
        }

        if (decimal.Round(Tax, 2) != Tax)
        {
            yield return new ValidationResult("The tax can't have more than 2 decimal places.", [nameof(Tax)]);
        }

        if (Tax > GrossInterest)
        {
            yield return new ValidationResult("The tax can't exceed the gross interest.", [nameof(Tax)]);
        }
    }
}
