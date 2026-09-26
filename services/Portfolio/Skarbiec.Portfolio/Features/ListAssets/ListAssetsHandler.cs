using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ListAssets;

public sealed class ListAssetsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<AssetResponse>>> HandleAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolioExists = await dbContext.Portfolios
            .AsNoTracking()
            .AnyAsync(p => p.Id == portfolioId, cancellationToken);

        if (!portfolioExists)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        // transactionCount as a correlated subquery inside this one query (spec-02) — no N+1, no
        // second round trip, and no counter column to drift out of sync with the Transactions table.
        var rows = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                Asset = a,
                TransactionCount = dbContext.Transactions.Count(t => t.AssetId == a.Id),
                DepositMaturityDate = dbContext.TermDeposits
                    .Where(t => t.AssetId == a.Id)
                    .Select(t => (DateOnly?)t.MaturityDate)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<AssetResponse> response = rows
            .Select(r => r.Asset.ToResponse(r.TransactionCount, r.DepositMaturityDate))
            .ToList();
        return Result<IReadOnlyList<AssetResponse>>.Success(response);
    }
}
