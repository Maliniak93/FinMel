using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The single source of which transaction types an asset class accepts (cash-transaction-types): the
/// cash-like classes in <see cref="AssetValuationModes.CurrencyValuedClasses"/> (Cash, Deposit) accept
/// only Deposit/Withdraw, every other class all types. Keyed on the class, not on
/// <c>Asset.ValuationMode</c>. The frontend mirror is <c>allowedTransactionTypes</c> in
/// <c>web/src/app/features/transactions/transaction-type.ts</c>.
/// </summary>
public static class AssetTransactionTypes
{
    private static readonly TransactionType[] CashLikeTypes = [TransactionType.Deposit, TransactionType.Withdraw];

    private static readonly TransactionType[] AllTypes = Enum.GetValues<TransactionType>();

    public static IReadOnlyList<TransactionType> Allowed(AssetClass assetClass) =>
        AssetValuationModes.CurrencyValuedClasses.Contains(assetClass) ? CashLikeTypes : AllTypes;

    public static bool IsAllowed(AssetClass assetClass, TransactionType type) => Allowed(assetClass).Contains(type);
}
