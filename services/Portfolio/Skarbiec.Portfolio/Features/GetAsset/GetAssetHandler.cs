using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetAsset;

public sealed class GetAssetHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId)
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
                    .FirstOrDefault(),
                FineWeightGramsPerPiece = dbContext.MetalHoldings
                    .Where(m => m.AssetId == a.Id)
                    .Select(m => (decimal?)m.FineWeightGramsPerPiece)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var response = row.Asset.ToResponse(
            row.TransactionCount, row.DepositMaturityDate, row.DepositSettled, row.FineWeightGramsPerPiece);

        return await dbContext.WithKpisAsync(response, row.Asset, cancellationToken);
    }
}
