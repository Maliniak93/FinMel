using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Accounts;

public sealed record ContributionResponse
{
    public required int Year { get; init; }
    public required decimal ContributedPln { get; init; }
    public decimal? LimitPln { get; init; }
}

internal static class ContributionPeriod
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    public static int Resolve(int? year, TimeProvider timeProvider)
        => year is >= MinYear and <= MaxYear ? year.Value : WarsawCalendar.Today(timeProvider).Year;

    public static async Task<Dictionary<Guid, ContributionResponse>> LoadContributionsAsync(
        this PortfolioDbContext dbContext,
        IReadOnlyCollection<Account> accounts,
        IReadOnlyDictionary<Guid, Asset> cashByAccount,
        int year,
        CancellationToken cancellationToken)
    {
        var wrapped = accounts.Where(a => a.Wrapper != TaxWrapper.Regular).ToList();
        if (wrapped.Count == 0)
        {
            return [];
        }

        var wrappedIds = wrapped.Select(a => a.Id).ToList();
        var cashIds = wrapped.Select(a => cashByAccount[a.Id].Id).ToList();
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        var deposits = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => cashIds.Contains(t.AssetId) && t.Type == TransactionType.Deposit && t.Date >= from && t.Date <= to)
            .ToListAsync(cancellationToken);

        var transferIds = deposits.Where(t => t.TransferId is not null).Select(t => t.TransferId!.Value).Distinct().ToList();
        var sourceAccountByTransfer = new Dictionary<Guid, Guid>();
        if (transferIds.Count > 0)
        {
            var sources = await (
                from leg in dbContext.Transactions.AsNoTracking()
                join asset in dbContext.Assets.AsNoTracking() on leg.AssetId equals asset.Id
                where leg.TransferId != null && transferIds.Contains(leg.TransferId.Value)
                    && leg.Type == TransactionType.Withdraw
                    && (asset.AssetClass == AssetClass.Cash || asset.AssetClass == AssetClass.BankAccount) && asset.AccountId != null
                select new { TransferId = leg.TransferId!.Value, AccountId = asset.AccountId!.Value })
                .ToListAsync(cancellationToken);
            sourceAccountByTransfer = sources.ToDictionary(s => s.TransferId, s => s.AccountId);
        }

        var limits = await dbContext.AccountContributionLimits
            .AsNoTracking()
            .Where(l => wrappedIds.Contains(l.AccountId) && l.Year == year)
            .ToDictionaryAsync(l => l.AccountId, l => l.Amount, cancellationToken);

        return wrapped.ToDictionary(
            account => account.Id,
            account =>
            {
                var cashId = cashByAccount[account.Id].Id;
                var contributed = deposits
                    .Where(t => t.AssetId == cashId)
                    .Where(t => t.TransferId is null
                        || (sourceAccountByTransfer.TryGetValue(t.TransferId.Value, out var source) && source != account.Id))
                    .Sum(t => t.Quantity);

                return new ContributionResponse
                {
                    Year = year,
                    ContributedPln = contributed,
                    LimitPln = limits.TryGetValue(account.Id, out var limit) ? limit : null
                };
            });
    }
}
