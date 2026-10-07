using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

// Mirrored by allowedTransactionTypes in web/src/app/features/transactions/transaction-type.ts.
public static class AssetTransactionTypes
{
    private static readonly TransactionType[] CashLikeTypes = [TransactionType.Deposit, TransactionType.Withdraw];

    private static readonly TransactionType[] MetalTypes = [TransactionType.Buy, TransactionType.Sell];

    private static readonly TransactionType[] AllTypes = Enum.GetValues<TransactionType>();

    public static IReadOnlyList<TransactionType> Allowed(AssetClass assetClass) =>
        assetClass == AssetClass.PreciousMetal ? MetalTypes
            : AssetValuationModes.CurrencyValuedClasses.Contains(assetClass) ? CashLikeTypes
            : AllTypes;

    public static bool IsAllowed(AssetClass assetClass, TransactionType type) => Allowed(assetClass).Contains(type);
}
