using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.Payments.SettleExpensePayment;

public sealed record SettleExpensePaymentRequest : IValidatableObject
{
    /// <summary>What was actually paid, in the currency of the paying account.</summary>
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal ActualAmount { get; init; }

    public required DateOnly ActualDate { get; init; }

    /// <summary>The account the money left.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>Whether the settlement also debits the account's cash with a Withdraw of the actual amount.</summary>
    public bool CreateWithdrawal { get; init; }

    /// <summary>Whether the account becomes the expense's account from the payment's nominal month on.</summary>
    public bool RememberAccount { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ActualDate < RecurringIncomeLimits.FirstDay)
        {
            yield return new ValidationResult(
                $"The actual date can't be before {RecurringIncomeLimits.FirstDay:yyyy-MM-dd}.", [nameof(ActualDate)]);
        }
    }
}
