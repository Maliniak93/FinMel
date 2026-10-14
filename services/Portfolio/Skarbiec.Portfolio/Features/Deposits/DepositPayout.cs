using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Deposits;

internal static class DepositPayout
{
    public static async Task<Result<PayoutTransfer>> PlanPayoutAsync(
        this PortfolioDbContext dbContext,
        Asset deposit,
        IReadOnlyCollection<Transaction> depositHistory,
        Guid returnAccountId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var destination = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.AccountId == returnAccountId && a.AssetClass == AssetClass.BankAccount, cancellationToken);

        if (destination is null)
        {
            return TransferErrors.InvalidCounterpart;
        }

        if (await dbContext.ReadOnlyErrorAsync(destination, cancellationToken) is { } readOnly)
        {
            return readOnly;
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

    public static async Task<IReadOnlyDictionary<Guid, DateOnly>> LoadPayoutDatesAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<Guid> depositIds, CancellationToken cancellationToken)
    {
        // A plain list parameter, whatever collection the caller passed.
        var ids = depositIds.ToList();

        // Only a payout writes a Withdraw on a deposit.
        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => ids.Contains(t.AssetId) && t.Type == TransactionType.Withdraw)
            .Select(t => new { t.AssetId, t.Date })
            .ToListAsync(cancellationToken);

        return rows
            .DistinctBy(r => r.AssetId)
            .ToDictionary(r => r.AssetId, r => r.Date);
    }

    public static async Task<DateOnly?> LoadPayoutDateAsync(
        this PortfolioDbContext dbContext, Guid depositId, CancellationToken cancellationToken)
        => (await dbContext.LoadPayoutDatesAsync([depositId], cancellationToken)).TryGetValue(depositId, out var date) ? date : null;
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
}
