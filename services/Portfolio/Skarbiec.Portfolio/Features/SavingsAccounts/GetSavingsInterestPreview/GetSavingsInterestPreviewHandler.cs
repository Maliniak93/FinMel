using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsInterestPreview;

public sealed class GetSavingsInterestPreviewHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<SavingsInterestPreviewResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A SavingsAccount row exists only for a Savings-class asset, so any other asset id misses here.
        var terms = await (
                from t in dbContext.SavingsAccounts.AsNoTracking()
                join asset in dbContext.Assets on t.AssetId equals asset.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select t)
            .FirstOrDefaultAsync(cancellationToken);

        if (terms is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        var interest = await dbContext.LoadSavingsInterestStatusAsync(assetId, WarsawCalendar.Today(timeProvider), cancellationToken);
        if (interest.Due is not { } due)
        {
            return SavingsAccountErrors.InterestNotDue;
        }

        return new SavingsInterestPreviewResponse
        {
            PeriodStart = due.PeriodStart,
            PeriodEnd = due.PeriodEnd,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            AverageDailyBalance = due.Accrual.AverageDailyBalance,
            GrossInterest = due.Accrual.GrossInterest,
            Tax = due.Accrual.Tax,
            NetInterest = due.Accrual.NetInterest,
            DuePeriodCount = due.DuePeriodCount
        };
    }
}
