using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.ListTransactions;

public sealed class ListTransactionsHandler(PortfolioDbContext dbContext)
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<TransactionResponse>>> HandleAsync(
        Guid assetId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // The asset lookup doubles as the existence check, and its class tells whether a transfer leg is on a manual route.
        var asset = await dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var currency = asset.Currency;

        var query = dbContext.Transactions.AsNoTracking().Where(t => t.AssetId == assetId);

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // The counterpart of every transfer leg on this page in one query: the other asset's leg.
        var transferIds = transactions
            .Where(t => t.TransferId is not null)
            .Select(t => t.TransferId!.Value)
            .ToList();

        var counterparts = await (
                from leg in dbContext.Transactions.AsNoTracking()
                where leg.TransferId != null && transferIds.Contains(leg.TransferId.Value) && leg.AssetId != assetId
                join counterpartAsset in dbContext.Assets on leg.AssetId equals counterpartAsset.Id
                join account in dbContext.Accounts on counterpartAsset.AccountId equals (Guid?)account.Id into accounts
                from counterpartAccount in accounts.DefaultIfEmpty()
                select new
                {
                    TransferId = leg.TransferId!.Value,
                    Asset = counterpartAsset,
                    Amount = leg.Quantity,
                    AccountName = counterpartAccount.Name
                })
            .ToDictionaryAsync(c => c.TransferId, cancellationToken);

        // The settled month of every interest credit on this page, in one query.
        var transactionIds = transactions.Select(t => (Guid?)t.Id).ToList();
        var creditPeriodEnds = await dbContext.BankInterestSettlements
            .AsNoTracking()
            .Where(s => transactionIds.Contains(s.TransactionId))
            .ToDictionaryAsync(s => s.TransactionId!.Value, s => s.PeriodEnd, cancellationToken);
        var creditPeriodIndexes = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => transactionIds.Contains(s.CreditTransactionId))
            .ToDictionaryAsync(s => s.CreditTransactionId!.Value, s => s.PeriodIndex, cancellationToken);

        var incomeNames = await (
                from occurrence in dbContext.RecurringIncomeOccurrences.AsNoTracking()
                where transactionIds.Contains(occurrence.TransactionId)
                join income in dbContext.RecurringIncomes.AsNoTracking() on occurrence.RecurringIncomeId equals income.Id
                select new { TransactionId = occurrence.TransactionId!.Value, income.Name })
            .ToDictionaryAsync(i => i.TransactionId, i => i.Name, cancellationToken);

        return new PagedResponse<TransactionResponse>
        {
            Items =
            [
                .. transactions.Select(t =>
                {
                    var counterpart = t.TransferId is { } transferId && counterparts.TryGetValue(transferId, out var found) ? found : null;
                    var response = t.ToResponse(
                        currency,
                        counterpart is null
                            ? null
                            : new TransactionTransferResponse
                            {
                                TransferId = t.TransferId!.Value,
                                Manual = TransferLegs.DirectionOf(t) == TransferDirection.Out
                                    ? TransferRoutes.IsDeletable(asset, counterpart.Asset)
                                    : TransferRoutes.IsDeletable(counterpart.Asset, asset),
                                CounterpartAssetId = counterpart.Asset.Id,
                                CounterpartAssetName = counterpart.Asset.Name,
                                CounterpartAccountName = counterpart.AccountName,
                                Direction = TransferLegs.DirectionOf(t)
                            },
                        creditPeriodEnds.TryGetValue(t.Id, out var periodEnd) ? periodEnd : null,
                        creditPeriodIndexes.TryGetValue(t.Id, out var periodIndex) ? periodIndex : null,
                        incomeNames.GetValueOrDefault(t.Id));

                    return counterpart is not null && MetalSettlement.Applies(asset.AssetClass) ? response.WithTotal(counterpart.Amount) : response;
                })
            ],
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
