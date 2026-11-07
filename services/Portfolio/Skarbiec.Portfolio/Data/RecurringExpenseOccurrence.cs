using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringExpenseOccurrence : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid RecurringExpenseId { get; init; }

    /// <summary>The first day of the month the rule produced the date for.</summary>
    public required DateOnly NominalMonth { get; init; }

    public required DateOnly PlannedDate { get; init; }
    public required decimal PlannedAmount { get; init; }

    public decimal ActualAmount { get; set; }
    public DateOnly ActualDate { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>The PLN rate of the actual date, 1 for PLN.</summary>
    public decimal FxRateToPln { get; set; }

    /// <summary>The Withdraw on the settlement account's money asset; null when none was created or it was deleted by hand.</summary>
    public Guid? TransactionId { get; set; }
}
