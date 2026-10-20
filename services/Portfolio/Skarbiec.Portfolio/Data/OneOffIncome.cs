using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class OneOffIncome : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public required Guid AccountId { get; set; }
    public required DateOnly Date { get; set; }

    /// <summary>What actually arrived; set exactly when the income is settled.</summary>
    public decimal? ActualAmount { get; set; }

    public DateOnly? ActualDate { get; set; }
    public Guid? ActualAccountId { get; set; }

    /// <summary>The PLN rate of the actual date, 1 for PLN.</summary>
    public decimal? FxRateToPln { get; set; }

    /// <summary>The system-managed Deposit on the settlement account's money asset; null when none was created.</summary>
    public Guid? TransactionId { get; set; }

    public bool IsSettled => ActualAmount is not null;
}
