using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class OneOffExpense : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public required DateOnly Date { get; set; }

    /// <summary>The account the expense is expected to be paid from; cleared when that account is deleted.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>What was actually paid; set exactly when the expense is settled.</summary>
    public decimal? ActualAmount { get; set; }

    public DateOnly? ActualDate { get; set; }
    public Guid? ActualAccountId { get; set; }

    /// <summary>The PLN rate of the actual date, 1 for PLN.</summary>
    public decimal? FxRateToPln { get; set; }

    /// <summary>The Withdraw on the settlement account's money asset; null when none was created or it was deleted by hand.</summary>
    public Guid? TransactionId { get; set; }

    public bool IsSettled => ActualAmount is not null;
}
