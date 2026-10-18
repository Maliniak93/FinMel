using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningSettings;

public sealed class GetPlanningSettingsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PlanningSettingsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.PlanningSettings
            .AsNoTracking()
            .Select(s => new PlanningSettingsResponse
            {
                MinimumTransferPln = s.MinimumTransferPln,
                PaymentLeadDays = s.PaymentLeadDays
            })
            .FirstOrDefaultAsync(cancellationToken);

        return settings ?? new PlanningSettingsResponse
        {
            MinimumTransferPln = PlanningSettings.DefaultMinimumTransferPln,
            PaymentLeadDays = PlanningSettings.DefaultPaymentLeadDays
        };
    }
}
