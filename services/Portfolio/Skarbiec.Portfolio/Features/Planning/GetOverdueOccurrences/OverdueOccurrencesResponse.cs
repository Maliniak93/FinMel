namespace Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;

public sealed record OverdueOccurrencesResponse
{
    public required IReadOnlyList<OverdueOccurrenceResponse> Items { get; init; }
}

public sealed record OverdueOccurrenceResponse
{
    public required Guid RecurringIncomeId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM).</summary>
    public required string NominalMonth { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
