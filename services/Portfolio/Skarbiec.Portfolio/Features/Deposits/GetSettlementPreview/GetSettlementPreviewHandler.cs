using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;

public sealed class GetSettlementPreviewHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<DepositSettlementPreviewResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A TermDeposit row exists only for a Deposit-class asset, so any other asset id misses here.
        var terms = await (
                from t in dbContext.TermDeposits.AsNoTracking()
                join asset in dbContext.Assets on t.AssetId equals asset.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select t)
            .FirstOrDefaultAsync(cancellationToken);

        if (terms is null)
        {
            return DepositErrors.NotFound(assetId);
        }

        // Settled wins over Due, as in the read-time status.
        if (terms.SettledOn is not null)
        {
            return DepositErrors.AlreadySettled;
        }

        if (terms.MaturityDate > WarsawCalendar.Today(timeProvider))
        {
            return DepositErrors.NotDue;
        }

        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization,
            TaxExempt = terms.TaxExempt
        });

        return new DepositSettlementPreviewResponse
        {
            SettledOn = terms.MaturityDate,
            GrossInterest = projection.GrossInterest,
            Tax = projection.Tax,
            NetInterest = projection.NetInterest,
            FinalAmount = projection.FinalAmount
        };
    }
}
