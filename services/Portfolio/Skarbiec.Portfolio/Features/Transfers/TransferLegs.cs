using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers;

public static class TransferLegs
{
    public static (Transaction Out, Transaction In) Create(Guid sourceAssetId, Guid targetAssetId, decimal amount, DateOnly date)
    {
        var transferId = Guid.NewGuid();

        return (
            NewLeg(sourceAssetId, TransactionType.Withdraw, amount, date, transferId),
            NewLeg(targetAssetId, TransactionType.Deposit, amount, date, transferId));
    }

    public static (Transaction Security, Transaction Cash) CreateTrade(
        Guid securityAssetId, Guid cashAssetId, TransactionType type, decimal quantity, decimal unitPrice, DateOnly date)
    {
        var transferId = Guid.NewGuid();
        var securityUnitPrice = SecurityUnitPrice(type, unitPrice);

        return (
            new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = securityAssetId,
                Type = type,
                Quantity = quantity,
                UnitPriceAmount = securityUnitPrice,
                Date = date,
                TransferId = transferId
            },
            NewLeg(
                cashAssetId,
                type == TransactionType.Buy ? TransactionType.Withdraw : TransactionType.Deposit,
                TradeCashAmount(type, quantity, securityUnitPrice),
                date,
                transferId));
    }

    public static decimal SecurityUnitPrice(TransactionType type, decimal unitPrice) =>
        type == TransactionType.Dividend ? 1m : unitPrice;

    public static decimal TradeCashAmount(TransactionType type, decimal quantity, decimal unitPrice) =>
        type == TransactionType.Dividend ? quantity : Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    public static TransferDirection DirectionOf(Transaction leg) =>
        leg.Type is TransactionType.Withdraw or TransactionType.Sell or TransactionType.Dividend
            ? TransferDirection.Out
            : TransferDirection.In;

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
