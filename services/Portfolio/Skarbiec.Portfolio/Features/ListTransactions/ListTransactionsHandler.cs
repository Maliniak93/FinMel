using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

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

        // The counterpart of every transfer leg on this page, in one query (asset-transfers-deposit-funding).
        // Both legs of a transfer always sit on different assets, so the counterpart is the other asset's leg.
        var transferIds = transactions
            .Where(t => t.TransferId is not null)
            .Select(t => t.TransferId!.Value)
            .ToList();

        var counterparts = await (
                from leg in dbContext.Transactions.AsNoTracking()
                where leg.TransferId != null && transferIds.Contains(leg.TransferId.Value) && leg.AssetId != assetId
                join counterpartAsset in dbContext.Assets on leg.AssetId equals counterpartAsset.Id
                join counterpartPortfolio in dbContext.Portfolios on counterpartAsset.PortfolioId equals counterpartPortfolio.Id
                select new
                {
                    TransferId = leg.TransferId!.Value,
                    AssetId = counterpartAsset.Id,
                    AssetName = counterpartAsset.Name,
                    PortfolioId = counterpartPortfolio.Id,
                    PortfolioName = counterpartPortfolio.Name
                })
            .ToDictionaryAsync(c => c.TransferId, cancellationToken);

        return new PagedResponse<TransactionResponse>
        {
            Items =
            [
                .. transactions.Select(t => t.ToResponse(
                    currency,
                    t.TransferId is { } transferId && counterparts.TryGetValue(transferId, out var counterpart)
                        ? new TransactionTransferResponse
                        {
                            CounterpartAssetId = counterpart.AssetId,
                            CounterpartAssetName = counterpart.AssetName,
                            CounterpartPortfolioId = counterpart.PortfolioId,
                            CounterpartPortfolioName = counterpart.PortfolioName,
                            Direction = TransferLegs.DirectionOf(t)
                        }
                        : null))
            ],
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
