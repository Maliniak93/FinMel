using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetAsset;

public sealed class GetAssetHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // transactionCount as a correlated subquery inside this one query (spec-02) — see ListAssets.
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
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? AssetErrors.NotFound(assetId)
            : row.Asset.ToResponse(row.TransactionCount, row.DepositMaturityDate);
    }
}
