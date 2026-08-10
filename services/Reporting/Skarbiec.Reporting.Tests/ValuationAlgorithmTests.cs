using Skarbiec.Contracts;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// Pure unit tests on 03-domain-model.md §Valuation algorithm (T2.11 AC) — no Testcontainers,
/// mirrors Portfolio's <c>TransactionQuantityCalculatorTests</c> container-free style.
/// </summary>
public sealed class ValuationAlgorithmTests
{
    private static readonly DateOnly SnapshotDate = new(2026, 8, 10);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    [Fact]
    public void Calculate_MarketAssetInForeignCurrency_ConvertsThroughFxRate()
    {
        var positions = new[] { MarketPosition(AssetClass.Stock, quantity: 10) };
        var prices = Prices((InstrumentId, "USD", SnapshotDate, 100m));
        var fx = FxRates(("USDPLN", SnapshotDate, 4m));

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        // 10 units x $100 x 4 PLN/USD = 4000 PLN.
        Assert.Equal(4000m, result.TotalPln);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void Calculate_MarketAssetInBaseCurrency_SkipsFxLookup()
    {
        var positions = new[] { MarketPosition(AssetClass.Stock, quantity: 5) };
        var prices = Prices((InstrumentId, "PLN", SnapshotDate, 200m));
        var fx = FxRates(); // empty — PLN needs no rate.

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(1000m, result.TotalPln);
        Assert.False(result.IsStale);
    }

    [Theory]
    [InlineData(3, false)] // weekend gap: within threshold, still fresh.
    [InlineData(7, false)] // exactly at the threshold: not yet stale.
    [InlineData(8, true)] // one day past the threshold: stale.
    [InlineData(30, true)]
    public void Calculate_LastKnownPriceFallback_StalenessFollowsAgeInDays(int ageDays, bool expectedStale)
    {
        var quoteDate = SnapshotDate.AddDays(-ageDays);
        var positions = new[] { MarketPosition(AssetClass.Stock, quantity: 1) };
        var prices = Prices((InstrumentId, "PLN", quoteDate, 100m));
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        // The last known price still values the position — staleness is a flag, not an exclusion.
        Assert.Equal(100m, result.TotalPln);
        Assert.Equal(expectedStale, result.IsStale);
    }

    [Fact]
    public void Calculate_FxRateOlderThanThreshold_MarksStaleEvenWithFreshPrice()
    {
        var positions = new[] { MarketPosition(AssetClass.Stock, quantity: 1) };
        var prices = Prices((InstrumentId, "USD", SnapshotDate, 100m));
        var fx = FxRates(("USDPLN", SnapshotDate.AddDays(-10), 4m));

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(400m, result.TotalPln);
        Assert.True(result.IsStale);
    }

    [Fact]
    public void Calculate_MarketAssetWithNoQuoteAtAll_ContributesNothingAndMarksStale()
    {
        var positions = new[] { MarketPosition(AssetClass.Stock, quantity: 10) };
        var prices = Prices(); // never synced.
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(0m, result.TotalPln);
        Assert.True(result.IsStale);
        Assert.Empty(result.Breakdown);
    }

    [Fact]
    public void Calculate_ManualAssetInForeignCurrency_ConvertsAtSnapshotDateRate()
    {
        var positions = new[] { ManualPosition(AssetClass.RealEstate, 50_000m, "EUR") };
        var fx = FxRates(("EURPLN", SnapshotDate, 4.3m));

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), fx, SnapshotDate);

        Assert.Equal(215_000m, result.TotalPln);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void Calculate_ManualAssetInBaseCurrency_UsesValueDirectly()
    {
        var positions = new[] { ManualPosition(AssetClass.Other, 12_345.67m, "PLN") };

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), FxRates(), SnapshotDate);

        Assert.Equal(12_345.67m, result.TotalPln);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void Calculate_MixOfMarketAndManualAcrossAssetClasses_BreaksDownPerAssetClass()
    {
        var stockInstrumentId = Guid.NewGuid();
        var positions = new ValuationPosition[]
        {
            new() { AssetClass = AssetClass.Stock, Currency = "PLN", Quantity = 2, InstrumentId = stockInstrumentId },
            new() { AssetClass = AssetClass.RealEstate, Currency = "PLN", Quantity = 0, ManualValueAmount = 300_000m },
        };
        var prices = Prices((stockInstrumentId, "PLN", SnapshotDate, 150m));

        var result = ValuationAlgorithm.Calculate(positions, prices, FxRates(), SnapshotDate);

        Assert.Equal(300_300m, result.TotalPln);
        Assert.Equal(2, result.Breakdown.Count);
        Assert.Contains(result.Breakdown, e => e.AssetClass == AssetClass.Stock && e.ValuePln == 300m);
        Assert.Contains(result.Breakdown, e => e.AssetClass == AssetClass.RealEstate && e.ValuePln == 300_000m);
    }

    private static ValuationPosition MarketPosition(AssetClass assetClass, decimal quantity) => new()
    {
        AssetClass = assetClass,
        Currency = "PLN", // irrelevant for market assets — the instrument's own quote currency governs FX.
        Quantity = quantity,
        InstrumentId = InstrumentId,
    };

    private static ValuationPosition ManualPosition(AssetClass assetClass, decimal manualValue, string currency) => new()
    {
        AssetClass = assetClass,
        Currency = currency,
        Quantity = 0,
        ManualValueAmount = manualValue,
    };

    private static Dictionary<Guid, InstrumentPriceLookup> Prices(params (Guid InstrumentId, string Currency, DateOnly Date, decimal Close)[] entries) =>
        entries.ToDictionary(e => e.InstrumentId, e => new InstrumentPriceLookup(e.Currency, e.Date, e.Close));

    private static Dictionary<Guid, InstrumentPriceLookup> ImmutablePrices() => [];

    private static Dictionary<string, FxRateLookup> FxRates(params (string Pair, DateOnly Date, decimal Rate)[] entries) =>
        entries.ToDictionary(e => e.Pair, e => new FxRateLookup(e.Date, e.Rate));
}
