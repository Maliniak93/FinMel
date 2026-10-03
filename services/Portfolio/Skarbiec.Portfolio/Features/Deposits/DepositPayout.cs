using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Deposits;

public sealed record DepositPayoutInfo(DateOnly PaidOutOn, string? DestinationAssetName);

internal static class DepositPayout
{
    public static async Task<Result<PayoutTransfer>> PlanPayoutAsync(
        this PortfolioDbContext dbContext,
        Asset deposit,
        IReadOnlyCollection<Transaction> depositHistory,
        Guid destinationAssetId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var destination = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == destinationAssetId, cancellationToken);

        if (destination is null
            || !TransferRoutes.IsAllowed(deposit.AssetClass, destination.AssetClass)
            || destination.Currency != deposit.Currency
            || destination.IsArchived
            || await dbContext.IsPortfolioArchivedAsync(destination.PortfolioId, cancellationToken))
        {
            return TransferErrors.InvalidCounterpart;
        }

        // A payout always moves the whole balance: the bank closes the deposit and pays everything out.
        var balance = TransactionQuantityCalculator.Recompute(depositHistory);
        if (balance.IsFailure)
        {
            return balance.Error;
        }

        var (outLeg, inLeg) = TransferLegs.Create(deposit.Id, destination.Id, balance.Value, date);

        var depositQuantity = TransactionQuantityCalculator.Recompute([.. depositHistory, outLeg]);
        if (depositQuantity.IsFailure)
        {
            return depositQuantity.Error;
        }

        var destinationHistory = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == destination.Id)
            .ToListAsync(cancellationToken);

        var destinationQuantity = TransactionQuantityCalculator.Recompute([.. destinationHistory, inLeg]);
        if (destinationQuantity.IsFailure)
        {
            return destinationQuantity.Error;
        }

        return new PayoutTransfer(destination, outLeg, inLeg, depositQuantity.Value, destinationQuantity.Value);
    }

    public static async Task<IReadOnlyDictionary<Guid, DepositPayoutInfo>> LoadPayoutsAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<Guid> depositIds, CancellationToken cancellationToken)
    {
        // A plain list parameter, whatever collection the caller passed.
        var ids = depositIds.ToList();

        // The deposit's Withdraw is the Out leg; the destination is the In leg's asset, missing once detached.
        var rows = await (
                from outLeg in dbContext.Transactions.AsNoTracking()
                where ids.Contains(outLeg.AssetId) && outLeg.Type == TransactionType.Withdraw
                select new
                {
                    DepositId = outLeg.AssetId,
                    outLeg.Date,
                    DestinationName = (
                            from inLeg in dbContext.Transactions
                            where outLeg.TransferId != null && inLeg.TransferId == outLeg.TransferId && inLeg.Id != outLeg.Id
                            join destination in dbContext.Assets on inLeg.AssetId equals destination.Id
                            select destination.Name)
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);

        return rows
            .DistinctBy(r => r.DepositId)
            .ToDictionary(r => r.DepositId, r => new DepositPayoutInfo(r.Date, r.DestinationName));
    }

    public static async Task<DepositPayoutInfo?> LoadPayoutAsync(
        this PortfolioDbContext dbContext, Guid depositId, CancellationToken cancellationToken)
        => (await dbContext.LoadPayoutsAsync([depositId], cancellationToken)).GetValueOrDefault(depositId);
}

internal sealed record PayoutTransfer(
    Asset Destination, Transaction OutLeg, Transaction InLeg, decimal DepositQuantity, decimal DestinationQuantity)
{
    public void Stage(PortfolioDbContext dbContext, Asset deposit, decimal? fxRateToPln)
    {
        OutLeg.FxRateToPln = fxRateToPln;
        InLeg.FxRateToPln = fxRateToPln;
        dbContext.Transactions.Add(OutLeg);
        dbContext.Transactions.Add(InLeg);
        deposit.Quantity = DepositQuantity;
        Destination.Quantity = DestinationQuantity;
    }

    public DepositPayoutInfo ToInfo() => new(OutLeg.Date, Destination.Name);
}
