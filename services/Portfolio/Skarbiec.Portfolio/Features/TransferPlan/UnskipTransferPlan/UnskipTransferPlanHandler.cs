using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.TransferPlan.UnskipTransferPlan;

public sealed class UnskipTransferPlanHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var first = parsed.Value.FirstDay;
        var plan = await dbContext.TransferPlans.FirstOrDefaultAsync(p => p.Month == first && p.IsSkipped, cancellationToken);
        if (plan is null)
        {
            return TransferPlanErrors.NotFound(month);
        }

        dbContext.TransferPlans.Remove(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
