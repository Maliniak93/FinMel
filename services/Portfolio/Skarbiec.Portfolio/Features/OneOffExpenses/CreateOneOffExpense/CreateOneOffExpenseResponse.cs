namespace Skarbiec.Portfolio.Features.OneOffExpenses.CreateOneOffExpense;

public sealed record CreateOneOffExpenseResponse
{
    public required Guid Id { get; init; }
}
