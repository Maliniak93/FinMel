using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds.GetBondRedemptionPreview;

public sealed class GetBondRedemptionPreviewHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BondRedemptionPreviewResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var planned = await dbContext.PlanRedemptionAsync(
            portfolioId, assetId, WarsawCalendar.Today(timeProvider), forWrite: false, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var amounts = planned.Value.Amounts;

        return new BondRedemptionPreviewResponse
        {
            Date = planned.Value.Date,
            BondCount = amounts.BondCount,
            CapitalisedInterest = amounts.CapitalisedInterest,
            DiscountIncome = amounts.DiscountIncome,
            TaxableIncome = amounts.TaxableIncome,
            Tax = amounts.Tax,
            Proceeds = amounts.Proceeds
        };
    }
}
