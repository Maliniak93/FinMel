using System.Text.Json.Serialization;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringIncomeOccurrence : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid RecurringIncomeId { get; init; }

    /// <summary>The first day of the month the rule produced the date for.</summary>
    public required DateOnly NominalMonth { get; init; }

    public required OccurrenceStatus Status { get; set; }
    public required DateOnly PlannedDate { get; init; }
    public required decimal PlannedAmount { get; init; }
    public required Guid PlannedAccountId { get; init; }

    public decimal? ActualAmount { get; set; }
    public DateOnly? ActualDate { get; set; }
    public Guid? AccountId { get; set; }

    /// <summary>The PLN rate of the actual date, 1 for PLN; null while skipped.</summary>
    public decimal? FxRateToPln { get; set; }

    /// <summary>The system-managed Deposit on the settlement account's money asset; null when none was created.</summary>
    public Guid? TransactionId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<OccurrenceStatus>))]
public enum OccurrenceStatus
{
    Pending,
    Settled,
    Skipped,
}
