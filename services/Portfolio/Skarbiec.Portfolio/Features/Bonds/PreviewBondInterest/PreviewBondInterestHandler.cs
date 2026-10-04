using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds.PreviewBondInterest;

public sealed class PreviewBondInterestHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BondInterestPreviewResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, SettleBondInterestRequest request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.PlanBondInterestAsync(
            portfolioId, assetId, request, WarsawCalendar.Today(timeProvider), cancellationToken);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var rows = plan.Value.Periods
            .Select(p => new BondInterestPreviewRow
            {
                PeriodIndex = p.Period.Index,
                Start = p.Period.Start,
                End = p.Period.End,
                RatePercent = p.RatePercent,
                BondCount = p.BondCount,
                Gross = p.Amounts.Gross,
                Tax = p.Amounts.Tax,
                Net = p.Amounts.Net
            })
            .ToList();

        return new BondInterestPreviewResponse
        {
            Rows = rows,
            Totals = new BondInterestPreviewTotals
            {
                Gross = rows.Sum(r => r.Gross),
                Tax = rows.Sum(r => r.Tax),
                Net = rows.Sum(r => r.Net)
            }
        };
    }
}
