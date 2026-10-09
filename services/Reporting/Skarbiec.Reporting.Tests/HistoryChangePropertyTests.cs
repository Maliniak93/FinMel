using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests;

public sealed class HistoryChangePropertyTests
{
    private const int CaseCount = 200;

    private static readonly DateOnly Origin = new(2026, 3, 1);
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();

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
        var manual = random.Next(2) == 0;
        var manualDate = Origin.AddDays(random.Next(0, 60));

        var incoming = manual
            ? Incoming(history: [], valuationMode: AssetValuationMode.Manual, assetClass: AssetClass.RealEstate, manualAmount: 100m, manualDate: manualDate)
            : Incoming(history: history);

        var expected = manual ? manualDate : history[0].Date;

        Assert.Equal(expected, HistoryChange.EarliestAffectedDate(null, incoming));
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
    public void EarliestAffectedDate_ManualValueChanged_IsEarlierOfOldAndNewManualDate(int seed)
    {
        var random = new Random(seed);
        var oldDate = Origin.AddDays(random.Next(0, 60));
        var oldAmount = 1_000m * random.Next(1, 5);
        var newDate = random.Next(2) == 0 ? oldDate : Origin.AddDays(random.Next(0, 60));
        var newAmount = random.Next(2) == 0 ? oldAmount : 1_000m * random.Next(1, 5);

        var stored = Stored(history: [], valuationMode: AssetValuationMode.Manual, assetClass: AssetClass.RealEstate, manualAmount: oldAmount, manualDate: oldDate);
        var incoming = Incoming(history: [], valuationMode: AssetValuationMode.Manual, assetClass: AssetClass.RealEstate, manualAmount: newAmount, manualDate: newDate);

        DateOnly? expected = oldDate == newDate && oldAmount == newAmount
            ? null
            : (oldDate < newDate ? oldDate : newDate);

        Assert.Equal(expected, HistoryChange.EarliestAffectedDate(stored, incoming));
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
    public void EarliestAffectedDate_PortfolioArchivedAndRestored_IsTheSetAndClearedDate()
    {
        var history = new[] { (Origin, 10m) };
        var occurredAt = new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero);

        var archiving = HistoryChange.EarliestAffectedDate(
            Stored(history: history),
            Incoming(history: history, portfolioIsArchived: true, occurredAt: occurredAt));
        var restoring = HistoryChange.EarliestAffectedDate(
            Stored(history: history, portfolioIsArchived: true, portfolioArchivedOn: new DateOnly(2026, 4, 12)),
            Incoming(history: history, portfolioIsArchived: false));

        Assert.Equal(new DateOnly(2026, 4, 20), archiving);
        Assert.Equal(new DateOnly(2026, 4, 12), restoring);
    }

    [Fact]
    public void EarliestAffectedDate_NothingChanged_IsNull()
    {
        var history = new[] { (Origin, 10m), (Origin.AddDays(5), 12m) };

        Assert.Null(HistoryChange.EarliestAffectedDate(Stored(history: history), Incoming(history: history)));
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
        decimal? manualAmount = null,
        DateOnly? manualDate = null,
        bool isArchived = false,
        bool portfolioIsArchived = false,
        DateOnly? archivedOn = null,
        DateOnly? portfolioArchivedOn = null) => new()
        {
            AssetId = AssetId,
            UserId = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = history.Count > 0 ? history[^1].Quantity : 0m,
            ManualValueAmount = manualAmount,
            ManualValueDate = manualDate,
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = isArchived,
            Version = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = [.. history.Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            ArchivedOn = archivedOn,
            PortfolioArchivedOn = portfolioArchivedOn,
        };

    private static AssetPositionChanged Incoming(
        IReadOnlyList<(DateOnly Date, decimal Quantity)> history,
        AssetValuationMode valuationMode = AssetValuationMode.CurrencyValued,
        AssetClass assetClass = AssetClass.Cash,
        Guid? instrumentId = null,
        string currency = "PLN",
        decimal? manualAmount = null,
        DateOnly? manualDate = null,
        bool isArchived = false,
        bool portfolioIsArchived = false,
        DateTimeOffset? occurredAt = null) => new()
        {
            AssetId = AssetId,
            PortfolioId = PortfolioId,
            UserId = Guid.NewGuid(),
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = history.Count > 0 ? history[^1].Quantity : 0m,
            QuoteUnitsPerQuantity = 1m,
            ManualValueAmount = manualAmount,
            ManualValueDate = manualDate,
            QuantityHistory = [.. history.Select(p => new QuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = isArchived,
            Version = 1,
            OccurredAtUtc = occurredAt ?? DateTimeOffset.UtcNow,
        };
}
