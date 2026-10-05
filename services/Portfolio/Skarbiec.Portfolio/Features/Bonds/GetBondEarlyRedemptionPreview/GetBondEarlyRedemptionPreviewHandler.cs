using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds.GetBondEarlyRedemptionPreview;

public sealed class GetBondEarlyRedemptionPreviewHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BondEarlyRedemptionPreviewResponse>> HandleAsync(
        Guid portfolioId,
        Guid assetId,
        DateOnly date,
        int bondCount,
        decimal? runningPeriodRatePercent,
        CancellationToken cancellationToken)
    {
        var planned = await dbContext.PlanEarlyRedemptionAsync(
            portfolioId,
            assetId,
            date,
            bondCount,
            runningPeriodRatePercent,
            WarsawCalendar.Today(timeProvider),
            forWrite: false,
            cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var amounts = planned.Value.Amounts;

        return new BondEarlyRedemptionPreviewResponse
        {
            PeriodIndex = planned.Value.Period.Index,
            BondCount = amounts.BondCount,
            InterestDue = amounts.AccruedInterest,
            Fee = amounts.Fee,
            DiscountIncome = amounts.DiscountIncome,
            TaxableIncome = amounts.TaxableIncome,
            Tax = amounts.Tax,
            Proceeds = amounts.Proceeds
        };
    }
}
