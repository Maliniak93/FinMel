using Microsoft.EntityFrameworkCore;
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

        return rows
            .Select(r => r.Holding.ToResponse(r.Asset))
            .ToList();
    }
}
