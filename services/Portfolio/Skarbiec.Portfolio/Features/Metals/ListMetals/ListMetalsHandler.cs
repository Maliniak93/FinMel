using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals.ListMetals;

public sealed class ListMetalsHandler(PortfolioDbContext dbContext)
{
    public async Task<IReadOnlyList<MetalResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from holding in dbContext.MetalHoldings.AsNoTracking()
                join asset in dbContext.Assets on holding.AssetId equals asset.Id
                orderby asset.Name
                select new { Holding = holding, Asset = asset })
            .ToListAsync(cancellationToken);

        var assetIds = rows.Select(r => r.Asset.Id).ToList();
        var trades = await dbContext.Transactions.AsNoTracking()
            .Where(t => assetIds.Contains(t.AssetId))
            .ToListAsync(cancellationToken);

        var transferIds = trades.Where(t => t.TransferId != null).Select(t => t.TransferId).Distinct().ToList();
        var cashLegs = await dbContext.Transactions.AsNoTracking()
            .Where(t => t.Type == TransactionType.Withdraw && transferIds.Contains(t.TransferId))
            .ToListAsync(cancellationToken);

        var tradesByAsset = trades.ToLookup(t => t.AssetId);

        return rows
            .Select(r => r.Holding.ToResponse(
                r.Asset, MetalCostBasis.Compute(tradesByAsset[r.Asset.Id].ToList(), cashLegs).CostPln))
            .ToList();
    }
}
