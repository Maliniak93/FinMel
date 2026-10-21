using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests;

public sealed class HistoryChangePropertyTests
{
    private const int CaseCount = 50;

    private static readonly DateOnly Origin = new(2026, 3, 1);
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EarliestAffectedDate_AnyTwoHistories_IsFirstDateWhereTheStepFunctionsDiffer(int seed)
    {
        var random = new Random(seed);
        var oldHistory = RandomHistory(random);
        var newHistory = random.Next(3) == 0 ? oldHistory : MutatedHistory(random, oldHistory);

        var stored = Stored(history: oldHistory);
        var incoming = Incoming(history: newHistory);

        DateOnly? expected = null;
        for (var day = Origin.AddDays(-2); day <= Origin.AddDays(80); day = day.AddDays(1))
        {
            if (QuantityOn(oldHistory, day) != QuantityOn(newHistory, day))
            {
                expected = day;
                break;
            }
        }

        Assert.Equal(expected, HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EarliestAffectedDate_NewPosition_IsItsStart(int seed)
    {
        var random = new Random(seed);
        var history = RandomHistory(random);
        var incoming = Incoming(history: history);

        Assert.Equal(history[0].Date, HistoryChange.EarliestAffectedDate(null, incoming));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EarliestAffectedDate_ClassInstrumentOrCurrencyChanged_IsEarlierOfOldAndNewStart(int seed)
    {
        var random = new Random(seed);
        var oldHistory = RandomHistory(random);
        var newHistory = random.Next(2) == 0 ? oldHistory : RandomHistory(random);
        var instrumentId = Guid.NewGuid();

        var stored = Stored(history: oldHistory, valuationMode: AssetValuationMode.Market, assetClass: AssetClass.Stock, instrumentId: instrumentId, currency: "USD");
        var incoming = random.Next(3) switch
        {
            0 => Incoming(history: newHistory, valuationMode: AssetValuationMode.Market, assetClass: AssetClass.Etf, instrumentId: instrumentId, currency: "USD"),
            1 => Incoming(history: newHistory, valuationMode: AssetValuationMode.Market, assetClass: AssetClass.Stock, instrumentId: Guid.NewGuid(), currency: "USD"),
            _ => Incoming(history: newHistory, valuationMode: AssetValuationMode.Market, assetClass: AssetClass.Stock, instrumentId: instrumentId, currency: "EUR"),
        };

        var expected = oldHistory[0].Date < newHistory[0].Date ? oldHistory[0].Date : newHistory[0].Date;

        Assert.Equal(expected, HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ClassTimeline(int seed)
    {
        var random = new Random(seed);
        var oldTimeline = RandomTimeline(random);
        var newTimeline = random.Next(3) == 0 ? oldTimeline : RandomTimeline(random);
        var history = new[] { (Origin, 10m) };

        var stored = Stored(history: history, assetClass: oldTimeline[^1].AssetClass, classHistory: oldTimeline);
        var incoming = Incoming(history: history, assetClass: newTimeline[^1].AssetClass, classHistory: newTimeline);

        DateOnly? expected = null;
        for (var day = Origin; day <= Origin.AddDays(80); day = day.AddDays(1))
        {
            if (ClassOn(oldTimeline, day) != ClassOn(newTimeline, day))
            {
                expected = day;
                break;
            }
        }

        Assert.Equal(expected, HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void GroupIsNotACandidate(int seed)
    {
        var random = new Random(seed);
        var history = RandomHistory(random);
        var storedGroup = random.Next(2) == 0 ? (Guid?)null : Guid.NewGuid();
        var incomingGroup = random.Next(2) == 0 ? (Guid?)null : Guid.NewGuid();

        var stored = Stored(history: history, groupId: storedGroup);
        var incoming = Incoming(history: history, groupId: incomingGroup);

        Assert.Null(HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Fact]
    public void EarliestAffectedDate_AssetArchived_IsTheUtcDateOfTheEvent()
    {
        var history = new[] { (Origin, 10m) };
        var stored = Stored(history: history);
        var occurredAt = new DateTimeOffset(2026, 4, 20, 23, 30, 0, TimeSpan.Zero);

        var incoming = Incoming(history: history, isArchived: true, occurredAt: occurredAt);

        Assert.Equal(new DateOnly(2026, 4, 20), HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Fact]
    public void EarliestAffectedDate_AssetRestored_IsTheClearedArchiveDate()
    {
        var history = new[] { (Origin, 10m) };
        var stored = Stored(history: history, isArchived: true, archivedOn: new DateOnly(2026, 4, 10));

        var incoming = Incoming(history: history, isArchived: false);

        Assert.Equal(new DateOnly(2026, 4, 10), HistoryChange.EarliestAffectedDate(stored, incoming));
    }

    [Fact]
    public void EarliestAffectedDate_NothingChanged_IsNull()
    {
        var history = new[] { (Origin, 10m), (Origin.AddDays(5), 12m) };

        Assert.Null(HistoryChange.EarliestAffectedDate(Stored(history: history), Incoming(history: history)));
    }

    private static readonly AssetClass[] TimelineClasses = [AssetClass.Cash, AssetClass.Stock, AssetClass.Etf];

    private static AssetClass ClassOn(IReadOnlyList<(DateOnly Date, AssetClass AssetClass)> timeline, DateOnly day) =>
        timeline.Last(p => p.Date <= day).AssetClass;

    // The first point sits on Origin, where the quantity starts, so every day before it is unheld.
    private static (DateOnly Date, AssetClass AssetClass)[] RandomTimeline(Random random)
    {
        var dates = Enumerable.Range(0, random.Next(0, 4))
            .Select(_ => Origin.AddDays(random.Next(1, 60)))
            .Distinct()
            .Order();

        return [(Origin, TimelineClasses[random.Next(TimelineClasses.Length)]), .. dates.Select(d => (d, TimelineClasses[random.Next(TimelineClasses.Length)]))];
    }

    private static decimal QuantityOn(IReadOnlyList<(DateOnly Date, decimal Quantity)> history, DateOnly day) =>
        history.LastOrDefault(p => p.Date <= day).Quantity;

    private static (DateOnly Date, decimal Quantity)[] RandomHistory(Random random)
    {
        var count = random.Next(1, 5);
        var dates = Enumerable.Range(0, count)
            .Select(_ => Origin.AddDays(random.Next(0, 60)))
            .Distinct()
            .Order()
            .ToArray();

        return [.. dates.Select(d => (d, (decimal)random.Next(0, 4) * 10m))];
    }

    private static (DateOnly Date, decimal Quantity)[] MutatedHistory(Random random, (DateOnly Date, decimal Quantity)[] history)
    {
        var points = history.ToList();
        switch (random.Next(3))
        {
            case 0:
                points.Add((Origin.AddDays(random.Next(0, 60)), (decimal)random.Next(0, 4) * 10m));
                break;
            case 1:
                var index = random.Next(points.Count);
                points[index] = (points[index].Date, (decimal)random.Next(0, 4) * 10m);
                break;
            default:
                points.RemoveAt(random.Next(points.Count));
                break;
        }

        return [.. points.GroupBy(p => p.Date).Select(g => g.Last()).OrderBy(p => p.Date)];
    }

    private static Position Stored(
        IReadOnlyList<(DateOnly Date, decimal Quantity)> history,
        AssetValuationMode valuationMode = AssetValuationMode.CurrencyValued,
        AssetClass assetClass = AssetClass.Cash,
        Guid? instrumentId = null,
        string currency = "PLN",
        bool isArchived = false,
        DateOnly? archivedOn = null,
        IReadOnlyList<(DateOnly Date, AssetClass AssetClass)>? classHistory = null,
        Guid? groupId = null) => new()
        {
            GroupId = groupId,
            ClassHistory = [.. (classHistory ?? []).Select(p => new PositionClassPoint { Date = p.Date, AssetClass = p.AssetClass })],
            AssetId = AssetId,
            UserId = Guid.NewGuid(),
            AccountId = AccountId,
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = history.Count > 0 ? history[^1].Quantity : 0m,
            IsArchived = isArchived,
            Version = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = [.. history.Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            ArchivedOn = archivedOn,
        };

    private static AssetPositionChanged Incoming(
        IReadOnlyList<(DateOnly Date, decimal Quantity)> history,
        AssetValuationMode valuationMode = AssetValuationMode.CurrencyValued,
        AssetClass assetClass = AssetClass.Cash,
        Guid? instrumentId = null,
        string currency = "PLN",
        bool isArchived = false,
        DateTimeOffset? occurredAt = null,
        IReadOnlyList<(DateOnly Date, AssetClass AssetClass)>? classHistory = null,
        Guid? groupId = null) => new()
        {
            GroupId = groupId,
            AssetId = AssetId,
            AccountId = AccountId,
            UserId = Guid.NewGuid(),
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = history.Count > 0 ? history[^1].Quantity : 0m,
            QuoteUnitsPerQuantity = 1m,
            QuantityHistory = [.. history.Select(p => new QuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            ClassHistory = [.. (classHistory ?? []).Select(p => new ClassPoint { Date = p.Date, AssetClass = p.AssetClass })],
            IsArchived = isArchived,
            Version = 1,
            OccurredAtUtc = occurredAt ?? DateTimeOffset.UtcNow,
        };
}
