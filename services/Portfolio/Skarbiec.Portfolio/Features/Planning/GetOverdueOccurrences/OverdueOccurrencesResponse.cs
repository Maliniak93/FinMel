namespace Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;

public sealed record OverdueOccurrencesResponse
{
    public required IReadOnlyList<OverdueOccurrenceResponse> Items { get; init; }
}

public sealed record OverdueOccurrenceResponse
{
    /// <summary>Set for a recurring income occurrence, null for a one-off income.</summary>
    public required Guid? RecurringIncomeId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM); null for a one-off income.</summary>
    public required string? NominalMonth { get; init; }

    /// <summary>Set for a one-off income, null for a recurring income occurrence.</summary>
    public required Guid? OneOffIncomeId { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
