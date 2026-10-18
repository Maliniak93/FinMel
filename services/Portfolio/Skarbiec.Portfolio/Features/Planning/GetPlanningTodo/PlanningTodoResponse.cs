using System.Text.Json.Serialization;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;

public sealed record PlanningTodoResponse
{
    /// <summary>In the display order: overdue incomes, transfer plans, upcoming payments.</summary>
    public required IReadOnlyList<TodoItemResponse> Items { get; init; }

    /// <summary>Days ahead an upcoming payment is listed; 0 means payments are not listed.</summary>
    public required int PaymentLeadDays { get; init; }
}

public sealed record TodoItemResponse
{
    public required TodoItemKind Kind { get; init; }

    /// <summary>The income or expense name; null for a transfer plan.</summary>
    public required string? Name { get; init; }

    /// <summary>The due date of an overdue income or upcoming payment; null for a transfer plan.</summary>
    public required DateOnly? Date { get; init; }

    /// <summary>The month (yyyy-MM) of a transfer plan; null for an income or payment.</summary>
    public required string? Month { get; init; }

    /// <summary>The amount in the item's currency; PLN to transfer for a plan to accept; null for an unchecked plan.</summary>
    public required decimal? Amount { get; init; }

    /// <summary>Null for a transfer plan, whose amount is PLN.</summary>
    public required string? Currency { get; init; }

    public required int? DaysOverdue { get; init; }
    public required int? DaysUntil { get; init; }

    /// <summary>For a plan to accept, whether the amount is a top-up of an accepted plan; null for other kinds.</summary>
    public required bool? IsTopUp { get; init; }

    public required int? UncheckedRows { get; init; }
    public required int? TotalRows { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TodoItemKind>))]
public enum TodoItemKind
{
    OverdueIncome,
    TransferPlanToAccept,
    TransferPlanUnchecked,
    UpcomingPayment,
}
