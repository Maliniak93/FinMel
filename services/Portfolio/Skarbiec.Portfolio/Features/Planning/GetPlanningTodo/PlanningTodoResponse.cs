using System.Text.Json.Serialization;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;

public sealed record PlanningTodoResponse
{
    /// <summary>In the display order: overdue incomes, upcoming payments.</summary>
    public required IReadOnlyList<TodoItemResponse> Items { get; init; }

    /// <summary>Days ahead an upcoming payment is listed; 0 means payments are not listed.</summary>
    public required int PaymentLeadDays { get; init; }
}

public sealed record TodoItemResponse
{
    public required TodoItemKind Kind { get; init; }

    /// <summary>The income or expense name.</summary>
    public required string Name { get; init; }

    /// <summary>The due date of an overdue income or upcoming payment.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The amount in the item's currency.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required int? DaysOverdue { get; init; }
    public required int? DaysUntil { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TodoItemKind>))]
public enum TodoItemKind
{
    OverdueIncome,
    UpcomingPayment,
}
