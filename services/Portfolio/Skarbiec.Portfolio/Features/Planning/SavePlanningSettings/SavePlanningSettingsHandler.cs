using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;

public sealed class SavePlanningSettingsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(SavePlanningSettingsRequest request, CancellationToken cancellationToken)
    {
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
                PaymentLeadDays = (int)leadDays
            });
        }
        else
        {
            settings.PaymentLeadDays = (int)leadDays;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
