using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.GetAsset;

public sealed class GetAssetHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId && a.PortfolioId == portfolioId)
            .Select(a => new
            {
                Asset = a,
                TransactionCount = dbContext.Transactions.Count(t => t.AssetId == a.Id),
                DepositMaturityDate = dbContext.TermDeposits
                    .Where(t => t.AssetId == a.Id)
                    .Select(t => (DateOnly?)t.MaturityDate)
                    .FirstOrDefault(),
                DepositSettled = dbContext.TermDeposits
                    .Where(t => t.AssetId == a.Id)
                    .Select(t => (bool?)(t.SettledOn != null))
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var savingsInterestDue = await dbContext.SavingsInterestDueAsync(row.Asset, WarsawCalendar.Today(timeProvider), cancellationToken);

        return row.Asset.ToResponse(row.TransactionCount, row.DepositMaturityDate, row.DepositSettled, savingsInterestDue);
    }
}
