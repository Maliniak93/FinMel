using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ListTransactions;

public sealed class ListTransactionsHandler(PortfolioDbContext dbContext)
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<TransactionResponse>>> HandleAsync(
        Guid portfolioId, Guid assetId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Every transaction is in its asset's currency, so the lookup doubles as the existence check.
        var currency = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId && a.PortfolioId == portfolioId)
            .Select(a => a.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        if (currency is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var query = dbContext.Transactions.AsNoTracking().Where(t => t.AssetId == assetId);

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TransactionResponse>
        {
            Items = [.. transactions.Select(t => t.ToResponse(currency))],
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
