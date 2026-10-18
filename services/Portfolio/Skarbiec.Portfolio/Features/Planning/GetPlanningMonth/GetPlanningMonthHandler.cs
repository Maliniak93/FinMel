using System.Globalization;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed class GetPlanningMonthHandler(PlanningMonthLoader loader)
{
    private static readonly YearMonth FirstMonth = YearMonth.From(RecurringIncomeLimits.FirstDay);
    private static readonly YearMonth LastMonth = YearMonth.From(RecurringIncomeLimits.LastDay);

    public async Task<Result<PlanningMonthResponse>> HandleAsync(string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var viewed = YearMonth.From(parsed);
        if (viewed < FirstMonth || viewed > LastMonth)
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var data = await loader.LoadAsync(viewed, viewed, cancellationToken);

        return PlanningMonthBuilder.Build(data, viewed);
    }
}
