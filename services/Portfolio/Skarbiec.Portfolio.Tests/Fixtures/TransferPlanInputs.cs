using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class TransferPlanInputs
{
    public static readonly Guid IkeId = Guid.NewGuid();

    public static readonly Guid IkzeId = Guid.NewGuid();

    public static readonly Guid InvestmentsId = Guid.NewGuid();

    public static readonly Guid BankBucketId = Guid.NewGuid();

    public static TransferGroup Stocks() => new("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 50m);

    public static TransferGroup Bonds() => new("Obligacje", [AssetClass.Bond, AssetClass.Deposit], 40m);

    public static TransferGroup Metals() => new("Kruszce", [AssetClass.PreciousMetal], 10m);

    public static List<TransferGroup> WorkedGroups() => [Stocks(), Bonds(), Metals()];

    public static Dictionary<string, decimal> WorkedValues() =>
        new() { ["Akcje/ETF"] = 110_000m, ["Obligacje"] = 60_000m, ["Kruszce"] = 12_000m };

    public static TransferBasket Ike(decimal amount = 2_260m, int order = 0) =>
        new(IkeId, "IKE", order, Guid.NewGuid(), "IKE mBank", AccountType.Brokerage, amount);

    public static TransferBasket Ikze(decimal amount = 2_304m, int order = 1) =>
        new(IkzeId, "IKZE", order, Guid.NewGuid(), "IKZE Obligacje", AccountType.Bonds, amount);

    public static TransferBasket Investments(decimal amount = 1_979.27m, int order = 2) =>
        new(InvestmentsId, "Inwestycje", order, null, null, null, amount);

    public static TransferBasket Bank(decimal amount, int order = 0) =>
        new(BankBucketId, "Lokata", order, Guid.NewGuid(), "mBank Lokaty", AccountType.Bank, amount);

    public static List<TransferBasket> WorkedBaskets() => [Ike(), Ikze(), Investments()];
}
