using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests;

public sealed class HistoryRebuildPropertyTests
{
    private const int CaseCount = 100;

    private static readonly string[] Currencies = ["PLN", "USD", "EUR"];

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
    public void EveryDay_MatchesValuationAlgorithmWithLatestOnOrBefore(int seed)
    {
        var random = new Random(seed);
        var from = new DateOnly(2026, 3, 1).AddDays(random.Next(0, 30));
        var to = from.AddDays(random.Next(15, 36));

        var positions = new List<Position>();
        var priceSeries = new Dictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>();
        var positionCount = random.Next(1, 6);
        for (var i = 0; i < positionCount; i++)
        {
            var market = random.Next(2) == 0;
            var quoteCurrency = Currencies[random.Next(Currencies.Length)];
            var instrumentId = market ? Guid.NewGuid() : (Guid?)null;
            positions.Add(NewPosition(random, from, to, market, instrumentId, market ? quoteCurrency : Currencies[random.Next(Currencies.Length)]));

            if (instrumentId is { } id && random.Next(5) != 0)
            {
                priceSeries[id] = RandomSeries(random, from, to, (date, value) => new InstrumentPriceLookup(quoteCurrency, date, value));
            }
        }

        var fxSeries = new Dictionary<string, IReadOnlyList<FxRateLookup>>();
        foreach (var pair in new[] { "USDPLN", "EURPLN" })
        {
            if (random.Next(5) != 0)
            {
                fxSeries[pair] = RandomSeries(random, from, to, (date, value) => new FxRateLookup(date, value / 10m));
            }
        }

        var days = HistoryRebuild.Compute(positions, priceSeries, fxSeries, from, to);

        var expectedDates = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var held = positions.Where(p => p.QuantityHistory[0].Date <= day).ToList();
            if (held.Count == 0)
            {
                continue;
            }

            expectedDates.Add(day);
            var expected = ValuationAlgorithm.Calculate(
                [.. held.Select(p => new ValuationPosition
                {
                    AssetId = p.AssetId,
                    AssetClass = p.AssetClass,
                    ValuationMode = p.ValuationMode,
                    Currency = p.Currency,
                    Quantity = p.QuantityHistory.Last(q => q.Date <= day).Quantity,
                    InstrumentId = p.InstrumentId,
                    QuoteUnitsPerQuantity = p.QuoteUnitsPerQuantity,
                })],
                priceSeries.Where(s => s.Value.Any(e => e.Date <= day))
                    .ToDictionary(s => s.Key, s => s.Value.Last(e => e.Date <= day)),
                fxSeries.Where(s => s.Value.Any(e => e.Date <= day))
                    .ToDictionary(s => s.Key, s => s.Value.Last(e => e.Date <= day)),
                day);

            var actual = Assert.Single(days, d => d.Date == day);
            Assert.Equal(expected.TotalPln, actual.TotalPln);
            Assert.Equal(expected.IsStale, actual.IsStale);
            Assert.Equal(expected.Lines.Count, actual.Lines.Count);
            foreach (var line in expected.Lines)
            {
                Assert.Equal(line, Assert.Single(actual.Lines, l => l.AssetId == line.AssetId));
            }
        }

        Assert.Equal(expectedDates, days.Select(d => d.Date).Order());
    }

    private static Position NewPosition(Random random, DateOnly from, DateOnly to, bool market, Guid? instrumentId, string currency)
    {
        var span = to.DayNumber - from.DayNumber;
        var pointCount = random.Next(1, 5);
        var dates = Enumerable.Range(0, pointCount)
            .Select(_ => from.AddDays(random.Next(-30, span + 1)))
            .Distinct()
            .Order()
            .ToArray();
        var history = dates.Select(d => new PositionQuantityPoint { Date = d, Quantity = random.Next(0, 1_000) / 10m }).ToList();

        return new Position
        {
            AssetId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            AssetClass = market ? AssetClass.Stock : AssetClass.Cash,
            ValuationMode = market ? AssetValuationMode.Market : AssetValuationMode.CurrencyValued,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = history[^1].Quantity,
            QuoteUnitsPerQuantity = market && random.Next(3) == 0 ? 31.1m : 1m,
            PortfolioIsArchived = false,
            IsArchived = false,
            Version = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = history,
        };
    }

    // Ascending by date, with at most one element before from, as MarketData returns it.
    private static List<T> RandomSeries<T>(Random random, DateOnly from, DateOnly to, Func<DateOnly, decimal, T> create)
    {
        var span = to.DayNumber - from.DayNumber;
        var dates = Enumerable.Range(0, random.Next(0, 9))
            .Select(_ => from.AddDays(random.Next(-20, span + 1)))
            .Distinct()
            .Order()
            .ToList();
        var beforeFrom = dates.Where(d => d < from).TakeLast(1);

        return [.. beforeFrom.Concat(dates.Where(d => d >= from)).Select(d => create(d, random.Next(1, 500)))];
    }
}
