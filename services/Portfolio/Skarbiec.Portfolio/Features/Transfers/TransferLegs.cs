using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>
/// Leg construction and detach for the generic transfer core (asset-transfers-deposit-funding). A
/// transfer is exactly two transactions sharing a <see cref="Transaction.TransferId"/>: a Withdraw on
/// the source and a Deposit on the target, same quantity, same date, unit price 1. The legs reuse the
/// ordinary Deposit/Withdraw types, so each one drives its own asset's quantity through
/// <see cref="TransactionQuantityCalculator"/> like any other transaction (ADR-009).
/// </summary>
public static class TransferLegs
{
    /// <summary>
    /// Two unsaved legs with a fresh <see cref="Transaction.TransferId"/>. The entry point freezes
    /// their PLN rate (ADR-026) once both assets have been recomputed — same currency, so one rate.
    /// </summary>
    public static (Transaction Out, Transaction In) Create(Guid sourceAssetId, Guid targetAssetId, decimal amount, DateOnly date)
    {
        var transferId = Guid.NewGuid();

        return (
            NewLeg(sourceAssetId, TransactionType.Withdraw, amount, date, transferId),
            NewLeg(targetAssetId, TransactionType.Deposit, amount, date, transferId));
    }

    /// <summary>The source's Withdraw is the Out leg, the target's Deposit the In leg.</summary>
    public static TransferDirection DirectionOf(Transaction leg) =>
        leg.Type == TransactionType.Withdraw ? TransferDirection.Out : TransferDirection.In;

    /// <summary>
    /// Detach, never reverse, on removal: every leg that survives the removal of
    /// <paramref name="removed"/> — one on another asset, outside the deleted asset or portfolio — loses
    /// its <see cref="Transaction.TransferId"/> and becomes an ordinary, editable transaction. Its
    /// asset's quantity does not change, so nothing is published for it. Stages the change only; the
    /// caller saves it together with the removal.
    /// </summary>
    public static async Task DetachCounterpartsAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<Transaction> removed, CancellationToken cancellationToken)
    {
        var transferIds = removed
            .Where(t => t.TransferId is not null)
            .Select(t => t.TransferId!.Value)
            .Distinct()
            .ToList();

        if (transferIds.Count == 0)
        {
            return;
        }

        var removedIds = removed.Select(t => t.Id).ToList();

        // Tenancy-filtered like every load here: a transfer only ever links one user's transactions.
        var survivors = await dbContext.Transactions
            .Where(t => t.TransferId != null && transferIds.Contains(t.TransferId.Value) && !removedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        foreach (var survivor in survivors)
        {
            survivor.TransferId = null;
        }
    }

    private static Transaction NewLeg(Guid assetId, TransactionType type, decimal amount, DateOnly date, Guid transferId) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = assetId,
        Type = type,
        Quantity = amount,
        UnitPriceAmount = 1m,
        Date = date,
        TransferId = transferId
    };
}
