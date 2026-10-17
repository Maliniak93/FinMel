using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using StoredTransferPlan = Skarbiec.Portfolio.Data.TransferPlan;

namespace Skarbiec.Portfolio.Features.TransferPlan.SkipTransferPlan;

public sealed class SkipTransferPlanHandler(PortfolioDbContext dbContext, GetPlanningMonthHandler planningMonth)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var planning = await planningMonth.HandleAsync(month, cancellationToken);
        if (planning.IsFailure)
        {
            return planning.Error;
        }

        if (planning.Value.TransferPlan.Status != TransferPlanStatus.Preview)
        {
            return TransferPlanErrors.NotPreview(month);
        }

        dbContext.TransferPlans.Add(new StoredTransferPlan { Id = Guid.NewGuid(), Month = parsed.Value.FirstDay, IsSkipped = true });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
