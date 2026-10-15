using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.CashPlaces;

namespace Skarbiec.Portfolio.Features.Transfers;

internal static class MetalSettlement
{
    public static bool Applies(AssetClass assetClass) => assetClass == AssetClass.PreciousMetal;

    public static Error? TotalError(decimal quantity, decimal? unitPrice, decimal? totalAmount) =>
        unitPrice is null && quantity > 0 && totalAmount is > 0 && decimal.Round(totalAmount.Value, 2) == totalAmount
            ? null
            : TransactionErrors.MetalTotalRequired;

    // Stored at the column's 2 places; the invoice total stays the amount of record on the cash leg.
    public static decimal UnitPrice(decimal totalAmount, decimal quantity) =>
        Math.Round(totalAmount / quantity, 2, MidpointRounding.AwayFromZero);

    public static bool IsSource(Asset money) =>
        money.IsCashPlaceBalance() || (money.IsAccountCash() && money.AssetClass == AssetClass.BankAccount);

    public static TransactionResponse WithTotal(this TransactionResponse response, decimal totalAmount) =>
        response with { TotalAmount = totalAmount, ValuePln = totalAmount };
}
