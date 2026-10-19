using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountCashClassTimelineTests
{
    private static readonly DateOnly Day0 = new(2026, 1, 1);

    private static readonly DateOnly Day1 = new(2026, 1, 2);

    private static readonly DateOnly Day2 = new(2026, 1, 3);

    private static readonly DateOnly Day3 = new(2026, 1, 4);

    private static readonly DateOnly Day4 = new(2026, 1, 5);

    private static readonly Guid StockId = Guid.NewGuid();

    private static readonly Guid EtfId = Guid.NewGuid();

    private static AccountCashClassTimeline.Trade Trade(
        Guid assetId, AssetClass assetClass, TransactionType type, DateOnly date, long entryOrder, decimal quantity = 5m) =>
        new(assetId, assetClass, type, quantity, date, entryOrder);

    private static IReadOnlyList<(DateOnly Date, AssetClass AssetClass)> Pairs(IReadOnlyList<ClassPoint> points) =>
        [.. points.Select(p => (p.Date, p.AssetClass))];

    [Fact]
    public void SameDate_UsesEntryOrder()
    {
        var stockThenEtf = AccountCashClassTimeline.Build(
            Day0,
            [
                Trade(StockId, AssetClass.Stock, TransactionType.Buy, Day1, entryOrder: 1),
                Trade(EtfId, AssetClass.Etf, TransactionType.Buy, Day1, entryOrder: 2)
            ]);
        var etfThenStock = AccountCashClassTimeline.Build(
            Day0,
            [
                Trade(StockId, AssetClass.Stock, TransactionType.Buy, Day1, entryOrder: 2),
                Trade(EtfId, AssetClass.Etf, TransactionType.Buy, Day1, entryOrder: 1)
            ]);

        Assert.Equal([(Day0, AssetClass.Cash), (Day1, AssetClass.Etf)], Pairs(stockThenEtf));
        Assert.Equal([(Day0, AssetClass.Cash), (Day1, AssetClass.Stock)], Pairs(etfThenStock));
    }

    [Fact]
    public void FullySold_FallsBack()
    {
        var points = AccountCashClassTimeline.Build(
            Day0,
            [
                Trade(StockId, AssetClass.Stock, TransactionType.Buy, Day1, entryOrder: 1),
                Trade(EtfId, AssetClass.Etf, TransactionType.Buy, Day2, entryOrder: 2),
                Trade(EtfId, AssetClass.Etf, TransactionType.Sell, Day3, entryOrder: 3),
                Trade(StockId, AssetClass.Stock, TransactionType.Sell, Day4, entryOrder: 4)
            ]);

        Assert.Equal(
            [(Day0, AssetClass.Cash), (Day1, AssetClass.Stock), (Day2, AssetClass.Etf), (Day3, AssetClass.Stock), (Day4, AssetClass.Cash)],
            Pairs(points));
    }
}
