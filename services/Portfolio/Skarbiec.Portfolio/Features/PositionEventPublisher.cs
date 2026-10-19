using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features;

// One event's construction shared by every mutating slice, not a service layer.
public sealed class PositionEventPublisher(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task PublishCreatedAsync(Asset asset, CancellationToken cancellationToken)
    {
        var transactions = await LoadTransactionsAsync([asset.Id], cancellationToken);
        await PublishAsync(asset, HistoryOf(transactions, asset.Id), cancellationToken);
    }

    public async Task PublishChangedAsync(Asset asset, CancellationToken cancellationToken)
    {
        var transactions = await LoadTransactionsAsync([asset.Id], cancellationToken);

        asset.Version++;
        await PublishAsync(asset, HistoryOf(transactions, asset.Id), cancellationToken);
    }

    // Tracked rows win over stored ones: the event is published before the save that writes them.
    private async Task<Dictionary<Guid, List<Transaction>>> LoadTransactionsAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        var tracked = dbContext.ChangeTracker.Entries<Transaction>()
            .Where(e => assetIds.Contains(e.Entity.AssetId))
            .ToList();
        var trackedIds = tracked.Select(e => e.Entity.Id).ToList();

        var stored = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => assetIds.Contains(t.AssetId) && !trackedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return stored
            .Concat(tracked.Where(e => e.State != EntityState.Deleted).Select(e => e.Entity))
            .GroupBy(t => t.AssetId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static IReadOnlyList<QuantityPoint> HistoryOf(Dictionary<Guid, List<Transaction>> transactions, Guid assetId)
    {
        if (!transactions.TryGetValue(assetId, out var list))
        {
            return [];
        }

        var history = TransactionQuantityCalculator.History(list);
        return history.IsSuccess
            ? history.Value
            : throw new InvalidOperationException($"Asset {assetId} has an invalid transaction history: {history.Error.Code}.");
    }

    private async Task PublishAsync(
        Asset asset, IReadOnlyList<QuantityPoint> quantityHistory, CancellationToken cancellationToken)
    {
        var (assetClass, classHistory) = await EffectiveClassAsync(asset, quantityHistory, cancellationToken);

        // The interceptor stamps Asset.UserId only during SaveChangesAsync, which has not run yet.
        await publishEndpoint.Publish(new AssetPositionChanged
        {
            AssetId = asset.Id,
            AccountId = asset.AccountId,
            UserId = dbContext.CurrentUserId,
            AssetClass = assetClass,
            ValuationMode = asset.ValuationMode,
            InstrumentId = asset.InstrumentId,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            QuoteUnitsPerQuantity = await QuoteUnitsPerQuantityAsync(asset, cancellationToken),
            FirstTransactionDate = quantityHistory.Count == 0 ? null : quantityHistory[0].Date,
            QuantityHistory = quantityHistory,
            ClassHistory = classHistory,
            IsArchived = asset.IsArchived,
            GroupId = asset.GroupId,
            Version = asset.Version,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);
    }

    private async Task<(AssetClass AssetClass, IReadOnlyList<ClassPoint> ClassHistory)> EffectiveClassAsync(
        Asset asset, IReadOnlyList<QuantityPoint> quantityHistory, CancellationToken cancellationToken)
    {
        if (asset.AccountId is not { } accountId || asset.AssetClass != AssetClass.Cash)
        {
            return (asset.AssetClass, []);
        }

        var accountType = await AccountTypeAsync(accountId, cancellationToken);
        if (accountType == AccountType.Bonds)
        {
            return (AssetClass.Bond, []);
        }

        if (accountType != AccountType.Brokerage || quantityHistory.Count == 0)
        {
            return (asset.AssetClass, []);
        }

        var trades = await LoadTradesAsync(accountId, cancellationToken);
        var timeline = AccountCashClassTimeline.Build(quantityHistory[0].Date, trades);
        var today = WarsawCalendar.Today(timeProvider);

        return ((timeline.LastOrDefault(p => p.Date <= today) ?? timeline[0]).AssetClass, timeline);
    }

    // Local first: CreateAccount publishes the cash before the account is saved.
    private async Task<AccountType> AccountTypeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var local = dbContext.Accounts.Local.FirstOrDefault(a => a.Id == accountId);
        if (local is not null)
        {
            return local.Type;
        }

        return await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Type)
            .FirstAsync(cancellationToken);
    }

    private async Task<List<AccountCashClassTimeline.Trade>> LoadTradesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var trackedAssets = dbContext.ChangeTracker.Entries<Asset>()
            .Where(e => e.Entity.AccountId == accountId)
            .ToList();
        var trackedIds = trackedAssets.Select(e => e.Entity.Id).ToList();

        var stored = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId == accountId && !trackedIds.Contains(a.Id))
            .Select(a => new { a.Id, a.AssetClass })
            .ToListAsync(cancellationToken);

        var classes = stored
            .Select(a => (a.Id, a.AssetClass))
            .Concat(trackedAssets.Where(e => e.State != EntityState.Deleted).Select(e => (e.Entity.Id, e.Entity.AssetClass)))
            .Where(a => !AccountCash.MoneyClasses.Contains(a.AssetClass))
            .ToDictionary(a => a.Id, a => a.AssetClass);

        var transactions = await LoadTransactionsAsync(classes.Keys.ToList(), cancellationToken);
        var all = transactions.Values.SelectMany(list => list).ToList();

        // A row not yet inserted has no sequence value: it is the newest entry, in tracking order.
        var pendingOrder = all.Count == 0 ? 0 : all.Max(t => t.EntryOrder);

        return [.. all.Select(t => new AccountCashClassTimeline.Trade(
            t.AssetId, classes[t.AssetId], t.Type, t.Quantity, t.Date, t.EntryOrder != 0 ? t.EntryOrder : ++pendingOrder))];
    }

    private async Task<decimal> QuoteUnitsPerQuantityAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (asset.AssetClass != AssetClass.PreciousMetal)
        {
            return 1m;
        }

        // Local first: AddMetal and UpdateMetal publish before the holding they staged is saved.
        var tracked = dbContext.MetalHoldings.Local.FirstOrDefault(h => h.AssetId == asset.Id);
        if (tracked is not null)
        {
            return tracked.FineWeightGramsPerPiece;
        }

        return await dbContext.MetalHoldings
            .AsNoTracking()
            .Where(h => h.AssetId == asset.Id)
            .Select(h => h.FineWeightGramsPerPiece)
            .FirstAsync(cancellationToken);
    }
}
