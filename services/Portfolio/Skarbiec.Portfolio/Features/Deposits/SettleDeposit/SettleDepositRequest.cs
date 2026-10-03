using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Deposits.SettleDeposit;

/// <summary>StartDate ≤ SettledOn ≤ today (Europe/Warsaw) is checked in the handler.</summary>
public sealed record SettleDepositRequest : IValidatableObject
{
    public required DateOnly SettledOn { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public required decimal GrossInterest { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public required decimal Tax { get; init; }

    /// <summary>The Cash or Savings asset the whole balance is paid out to; omitted, the money stays in the deposit.</summary>
    public Guid? DestinationAssetId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Stored as numeric(18,2): a finer value is rejected rather than rounded away from the net-interest transaction.
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
