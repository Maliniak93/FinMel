using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.RemoveAsset;

public sealed class RemoveAssetHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        if (await dbContext.AccountArchivedErrorAsync(asset, cancellationToken) is { } accountArchived)
        {
            return accountArchived;
        }

        // portfolio_db has no FKs, so the delete cascades to the asset's transactions explicitly.
        var transactions = await dbContext.Transactions
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        dbContext.Transactions.RemoveRange(transactions);

        // Detach, never reverse: a leg on another asset stays an ordinary transaction, so nothing is published for it.
        await dbContext.DetachCounterpartsAsync(transactions, cancellationToken);

        // A term deposit's terms go here; a savings account's go through the FK cascade alone.
        var termDeposit = await dbContext.TermDeposits.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);
        if (termDeposit is not null)
        {
            dbContext.TermDeposits.Remove(termDeposit);
        }

        // Interest settlements carry no FK, so they go here, with the asset.
        dbContext.SavingsInterestSettlements.RemoveRange(
            await dbContext.SavingsInterestSettlements.Where(s => s.AssetId == assetId).ToListAsync(cancellationToken));
        dbContext.BondInterestSettlements.RemoveRange(
            await dbContext.BondInterestSettlements.Where(s => s.AssetId == assetId).ToListAsync(cancellationToken));
        dbContext.BondRedemptions.RemoveRange(
            await dbContext.BondRedemptions.Where(r => r.AssetId == assetId).ToListAsync(cancellationToken));

        // A bond bought with this one in a swap keeps its value; like its detached opening leg, it stops pointing here.
        foreach (var swapped in await dbContext.TreasuryBonds.Where(b => b.SwappedFromAssetId == assetId).ToListAsync(cancellationToken))
        {
            swapped.SwappedFromAssetId = null;
        }

        dbContext.Assets.Remove(asset);

        // Terminal for this asset: no version, no further position event.
        await publishEndpoint.Publish(new AssetRemoved
        {
            AssetId = asset.Id,
            AccountId = asset.AccountId,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent transaction moved the asset's xmin: nothing is deleted, and the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
