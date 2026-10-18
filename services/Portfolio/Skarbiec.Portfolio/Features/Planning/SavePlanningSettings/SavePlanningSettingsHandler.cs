using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;

public sealed class SavePlanningSettingsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(SavePlanningSettingsRequest request, CancellationToken cancellationToken)
    {
        var minimum = request.MinimumTransferPln;
        if (minimum != decimal.Truncate(minimum) || minimum < 0m || minimum > PlanningSettings.MaxMinimumTransferPln)
        {
            return TransferPlanErrors.InvalidMinimumTransfer;
        }

        var leadDays = request.PaymentLeadDays;
        if (leadDays != decimal.Truncate(leadDays) || leadDays < 0m || leadDays > PlanningSettings.MaxPaymentLeadDays)
        {
            return PlanningErrors.InvalidPaymentLeadDays;
        }

        var settings = await dbContext.PlanningSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            dbContext.PlanningSettings.Add(new PlanningSettings
            {
                UserId = dbContext.CurrentUserId,
                MinimumTransferPln = (int)minimum,
                PaymentLeadDays = (int)leadDays
            });
        }
        else
        {
            settings.MinimumTransferPln = (int)minimum;
            settings.PaymentLeadDays = (int)leadDays;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
