using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Transfers;

internal sealed record BrokerageLegs(Asset Cash, Transaction CashLeg, decimal CashQuantity)
{
    public TransactionTransferResponse ToTransferResponse(Transaction securityLeg) => new()
    {
        TransferId = CashLeg.TransferId!.Value,
        Manual = false,
        CounterpartAssetId = Cash.Id,
        CounterpartAssetName = Cash.Name,
        Direction = TransferLegs.DirectionOf(securityLeg)
    };
}

internal static class BrokerageTrade
{
    public static bool Applies(AssetClass assetClass) => assetClass is AssetClass.Stock or AssetClass.Etf;

    public static bool IsIncome(TransactionType type) => type is TransactionType.Dividend or TransactionType.Interest;

    public static Error? FeeError(AssetClass assetClass, TransactionType type, decimal? fee) =>
        fee is null || (Applies(assetClass) && !IsIncome(type) && fee >= 0)
            ? null
            : TransactionErrors.FeeNotAllowed;

    // A dividend or interest is a net PLN amount: the quantity is the amount and the unit price 1.
    public static decimal SecurityUnitPrice(TransactionType type, decimal unitPrice) => IsIncome(type) ? 1m : unitPrice;

    public static async Task<Result<decimal>> ResolveRateAsync(
        this IFxRateLookupClient fxRateLookupClient,
        string currency,
        TransactionType type,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (IsIncome(type))
        {
            return 1m;
        }

        var rate = await fxRateLookupClient.ResolveFxRateToPlnAsync(currency, date, cancellationToken);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        if (rate.Value is not { } known)
        {
            return TransactionErrors.FxRateMissing(currency, date);
        }

        return known;
    }

    public static Result<decimal> CashAmount(Transaction security)
    {
        if (IsIncome(security.Type))
        {
            return security.Quantity;
        }

        var gross = Math.Round(
            security.Quantity * security.UnitPriceAmount * security.FxRateToPln!.Value, 2, MidpointRounding.AwayFromZero);
        var fee = security.FeeAmount ?? 0m;
        if (security.Type == TransactionType.Buy)
        {
            return gross + fee;
        }

        return fee > gross ? TransactionErrors.FeeExceedsProceeds : gross - fee;
    }

    public static TransactionType CashLegType(TransactionType securityType) =>
        securityType == TransactionType.Buy ? TransactionType.Withdraw : TransactionType.Deposit;

    public static async Task<Result<decimal>> ReplayCashAsync(
        this PortfolioDbContext dbContext, Guid cashAssetId, Transaction candidate, CancellationToken cancellationToken)
    {
        var others = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == cashAssetId && t.Id != candidate.Id)
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute([.. others, candidate], _ => TransferErrors.InsufficientFunds);
    }

    public static async Task<Result<BrokerageLegs>> PlanAsync(
        this PortfolioDbContext dbContext,
        IFxRateLookupClient fxRateLookupClient,
        Asset security,
        Transaction trade,
        CancellationToken cancellationToken)
    {
        var rate = await fxRateLookupClient.ResolveRateAsync(security.Currency, trade.Type, trade.Date, cancellationToken);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        trade.FxRateToPln = rate.Value;

        var cashAmount = CashAmount(trade);
        if (cashAmount.IsFailure)
        {
            return cashAmount.Error;
        }

        var cash = await dbContext.LoadAccountCashAsync(security.AccountId!.Value, cancellationToken);
        var transferId = Guid.NewGuid();
        trade.TransferId = transferId;

        var cashLeg = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = cash.Id,
            Type = CashLegType(trade.Type),
            Quantity = cashAmount.Value,
            UnitPriceAmount = 1m,
            FxRateToPln = 1m,
            Date = trade.Date,
            TransferId = transferId
        };

        var cashQuantity = await dbContext.ReplayCashAsync(cash.Id, cashLeg, cancellationToken);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        return new BrokerageLegs(cash, cashLeg, cashQuantity.Value);
    }
}
