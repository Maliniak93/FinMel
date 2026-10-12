using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

// Mirrored by allowedTransactionTypes in web/src/app/features/transactions/transaction-type.ts.
public static class AssetTransactionTypes
{
    private static readonly TransactionType[] CashLikeTypes = [TransactionType.Deposit, TransactionType.Withdraw];

    private static readonly TransactionType[] MetalTypes = [TransactionType.Buy, TransactionType.Sell];

    private static readonly TransactionType[] SecurityTypes =
        [TransactionType.Buy, TransactionType.Sell, TransactionType.Dividend, TransactionType.Interest];

    public static IReadOnlyList<TransactionType> Allowed(AssetClass assetClass) =>
        assetClass == AssetClass.PreciousMetal ? MetalTypes
            : AssetValuationModes.CurrencyValuedClasses.Contains(assetClass) ? CashLikeTypes
            : SecurityTypes;

    public static bool IsAllowed(AssetClass assetClass, TransactionType type) => Allowed(assetClass).Contains(type);
}
