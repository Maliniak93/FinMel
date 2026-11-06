using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Securities;

namespace Skarbiec.Portfolio.Features.Accounts;

internal sealed record AccountPerformance(decimal NetContributionsPln, decimal? PositionsCostPln, decimal PaidInterestPln);

internal static class AccountPerformanceLoader
{
    public static async Task<Dictionary<Guid, AccountPerformance>> LoadPerformanceAsync(
        this PortfolioDbContext dbContext,
        IReadOnlyCollection<Account> accounts,
        IReadOnlyDictionary<Guid, Asset> cashByAccount,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var investment = accounts.Where(a => a.Type != AccountType.Bank).ToList();
        if (investment.Count == 0)
        {
            return [];
        }

        var accountIds = investment.Select(a => a.Id).ToList();
        var cashIds = investment.Select(a => cashByAccount[a.Id].Id).ToList();

        var cashTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => cashIds.Contains(t.AssetId) && t.Date <= today)
            .ToListAsync(cancellationToken);

        var transferIds = cashTransactions.Where(t => t.TransferId is not null).Select(t => t.TransferId!.Value).Distinct().ToList();
        var legsByTransfer = transferIds.Count == 0
            ? new Dictionary<Guid, List<(Guid TransactionId, Guid? AccountId)>>()
            : (await (
                from leg in dbContext.Transactions.AsNoTracking()
                join asset in dbContext.Assets.AsNoTracking() on leg.AssetId equals asset.Id
                where leg.TransferId != null && transferIds.Contains(leg.TransferId.Value)
                select new { TransferId = leg.TransferId!.Value, TransactionId = leg.Id, asset.AccountId })
                .ToListAsync(cancellationToken))
                .GroupBy(l => l.TransferId)
                .ToDictionary(g => g.Key, g => g.Select(l => (l.TransactionId, l.AccountId)).ToList());

        var securities = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId != null && accountIds.Contains(a.AccountId.Value) && !a.IsArchived
                && (a.AssetClass == AssetClass.Stock || a.AssetClass == AssetClass.Etf))
            .ToListAsync(cancellationToken);
        var securityIds = securities.Select(a => a.Id).ToList();
        var securityTransactions = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => securityIds.Contains(t.AssetId))
            .ToListAsync(cancellationToken))
            .ToLookup(t => t.AssetId);

        var openBonds = await (
            from bond in dbContext.TreasuryBonds.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on bond.AssetId equals asset.Id
            where asset.AccountId != null && accountIds.Contains(asset.AccountId.Value) && !asset.IsArchived && bond.BondCount > 0
            select new { AccountId = asset.AccountId!.Value, Bond = bond })
            .ToListAsync(cancellationToken);
        var bondIds = openBonds.Select(b => b.Bond.AssetId).ToList();
        var settlements = (await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => bondIds.Contains(s.AssetId))
            .ToListAsync(cancellationToken))
            .ToLookup(s => s.AssetId);

        return investment.ToDictionary(
            account => account.Id,
            account =>
            {
                var cashId = cashByAccount[account.Id].Id;
                var flows = cashTransactions
                    .Where(t => t.AssetId == cashId)
                    .Select(t => new NetContributions.Flow(
                        t.Type,
                        t.Quantity,
                        t.TransferId is not null,
                        t.TransferId is { } transferId && legsByTransfer.TryGetValue(transferId, out var legs)
                            ? legs.FirstOrDefault(l => l.TransactionId != t.Id).AccountId
                            : null))
                    .ToList();

                decimal? cost = 0m;
                foreach (var security in securities.Where(a => a.AccountId == account.Id))
                {
                    var basis = SecurityCostBasis.Compute(securityTransactions[security.Id]);
                    if (basis.Quantity > 0)
                    {
                        cost = cost is null || basis.CostPln is null ? null : cost + basis.CostPln;
                    }
                }

                var accountBonds = openBonds.Where(b => b.AccountId == account.Id).ToList();
                cost += accountBonds.Sum(b => b.Bond.BondCount * b.Bond.PurchasePricePerBond);

                return new AccountPerformance(
                    NetContributions.Sum(account.Id, flows),
                    cost,
                    accountBonds.Sum(b => BondMappingExtensions.PaidInterest(b.Bond.Type, settlements[b.Bond.AssetId])));
            });
    }
}
