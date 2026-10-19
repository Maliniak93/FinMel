using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountCashClassTimelinePropertyTests
{
    private const int CaseCount = 200;

    private const int DayCount = 30;

    private static readonly DateOnly Origin = new(2026, 1, 1);

    private static readonly AssetClass[] SecurityClasses = [AssetClass.Stock, AssetClass.Etf];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private static List<AccountCashClassTimeline.Trade> RandomTrades(Random random)
    {
        var assets = Enumerable.Range(0, random.Next(1, 5))
            .Select(_ => (Id: Guid.NewGuid(), Class: SecurityClasses[random.Next(SecurityClasses.Length)]))
            .ToList();
        var trades = new List<AccountCashClassTimeline.Trade>();
        var held = assets.ToDictionary(a => a.Id, _ => 0m);
        var entryOrder = 0L;
        foreach (var day in Enumerable.Range(0, DayCount).Where(_ => random.Next(3) == 0))
        {
            foreach (var _ in Enumerable.Range(0, random.Next(1, 4)))
            {
                var asset = assets[random.Next(assets.Count)];
                var sell = held[asset.Id] > 0m && random.Next(3) == 0;
                var quantity = sell ? random.Next(1, (int)held[asset.Id] + 1) : random.Next(1, 6);
                held[asset.Id] += sell ? -quantity : quantity;
                trades.Add(new AccountCashClassTimeline.Trade(
                    asset.Id, asset.Class, sell ? TransactionType.Sell : TransactionType.Buy, quantity, Origin.AddDays(day), ++entryOrder));
            }
        }

        return trades;
    }

    private static AssetClass BruteForceClassOn(DateOnly day, List<AccountCashClassTimeline.Trade> trades)
    {
        var heldOn = trades
            .Where(t => t.Date <= day)
            .GroupBy(t => t.AssetId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Type == TransactionType.Buy ? t.Quantity : -t.Quantity));
        var decisive = trades
            .Where(t => t.Type == TransactionType.Buy && t.Date <= day && heldOn[t.AssetId] > 0m)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.EntryOrder)
            .FirstOrDefault();

        return decisive?.AssetClass ?? AssetClass.Cash;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MatchesBruteForce(int seed)
    {
        var random = new Random(seed);
        var trades = RandomTrades(random);
        var firstDate = trades.Count == 0 ? Origin : trades.Min(t => t.Date);

        var timeline = AccountCashClassTimeline.Build(firstDate, trades);

        Assert.NotEmpty(timeline);
        Assert.Equal(firstDate, timeline[0].Date);
        Assert.Equal(timeline.Select(p => p.Date).OrderBy(d => d), timeline.Select(p => p.Date));
        Assert.Equal(timeline.Count, timeline.Select(p => p.Date).Distinct().Count());
        for (var i = 1; i < timeline.Count; i++)
        {
            Assert.NotEqual(timeline[i - 1].AssetClass, timeline[i].AssetClass);
        }

        for (var offset = 0; offset <= DayCount; offset++)
        {
            var day = Origin.AddDays(offset);
            if (day < firstDate)
            {
                continue;
            }

            var fromTimeline = timeline.Last(p => p.Date <= day).AssetClass;
            Assert.Equal(BruteForceClassOn(day, trades), fromTimeline);
        }
    }
}
