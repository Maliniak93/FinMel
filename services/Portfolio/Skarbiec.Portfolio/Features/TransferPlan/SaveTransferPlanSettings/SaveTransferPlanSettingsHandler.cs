using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.TransferPlan.SaveTransferPlanSettings;

public sealed class SaveTransferPlanSettingsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(SaveTransferPlanSettingsRequest request, CancellationToken cancellationToken)
    {
        var minimum = request.MinimumTransferPln;
        if (minimum != decimal.Truncate(minimum) || minimum < 0m || minimum > TransferPlanSettings.MaxMinimumTransferPln)
        {
            return TransferPlanErrors.InvalidMinimumTransfer;
        }

        var settings = await dbContext.TransferPlanSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            dbContext.TransferPlanSettings.Add(new TransferPlanSettings
            {
                UserId = dbContext.CurrentUserId,
                MinimumTransferPln = (int)minimum
            });
        }
        else
        {
            settings.MinimumTransferPln = (int)minimum;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
