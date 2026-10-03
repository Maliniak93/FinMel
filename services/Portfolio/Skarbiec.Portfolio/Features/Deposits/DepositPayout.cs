using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Deposits;

/// <summary>
/// A deposit's payout (deposit-payout-to-cash): the date of its Withdraw leg and the name of the Cash
/// or Savings asset holding the matching Deposit leg — <see langword="null"/> once that asset was removed and the
/// leg detached.
/// </summary>
public sealed record DepositPayoutInfo(DateOnly PaidOutOn, string? DestinationAssetName);

/// <summary>
/// The Deposit → Cash or Deposit → Savings (deposit-payout-to-savings) transfer that pays a settled deposit's whole balance out (deposit-payout-to-cash),
/// shared by SettleDeposit's <c>destinationAssetId</c> and PayOutDeposit, plus the read side of it. No
/// stored column: a deposit is paid out when it has a Withdraw — only a payout creates one — so the
/// status survives a detach, which clears the link, not the leg.
/// </summary>
internal static class DepositPayout
{
    /// <summary>
    /// Loads the destination through the tenancy filter and checks it against the transfer rules —
    /// anything that is not one of the user's same-currency, non-archived assets on an allowed route, in
    /// an active portfolio, is <see cref="TransferErrors.InvalidCounterpart"/> (a stranger's id and the deposit
    /// itself get the same answer). Then builds both legs for the deposit's whole balance, as replayed
    /// from <paramref name="depositHistory"/>, and recomputes both assets with them. Stages nothing.
    /// </summary>
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

        // The deposit's Withdraw is the Out leg; the destination is the asset of the In leg sharing its
        // TransferId, missing once the transfer was detached.
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

/// <param name="Destination">The tracked destination Cash or Savings asset.</param>
/// <param name="OutLeg">The deposit's Withdraw leg of its whole balance.</param>
/// <param name="InLeg">The destination's Deposit leg.</param>
/// <param name="DepositQuantity">The deposit's quantity with <paramref name="OutLeg"/> replayed — 0.</param>
/// <param name="DestinationQuantity">The destination's quantity with <paramref name="InLeg"/> replayed.</param>
internal sealed record PayoutTransfer(
    Asset Destination, Transaction OutLeg, Transaction InLeg, decimal DepositQuantity, decimal DestinationQuantity)
{
    /// <summary>
    /// Stages both legs with the one PLN rate of their date (same currency, ADR-026) and both
    /// quantities. The caller publishes both assets and saves them in one <c>SaveChangesAsync</c>.
    /// </summary>
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
