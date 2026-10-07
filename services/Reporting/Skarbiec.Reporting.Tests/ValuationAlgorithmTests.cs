using Skarbiec.Contracts;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests;

public sealed class ValuationAlgorithmTests
{
    private static readonly DateOnly SnapshotDate = new(2026, 8, 10);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    [Fact]
    public void Calculate_MarketAssetInForeignCurrency_ConvertsThroughFxRate()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { MarketPosition(assetId, AssetClass.Stock, quantity: 10) };
        var prices = Prices((InstrumentId, "USD", SnapshotDate, 100m));
        var fx = FxRates(("USDPLN", SnapshotDate, 4m));

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        // 10 units x $100 x 4 PLN/USD = 4000 PLN.
        Assert.Equal(4000m, result.TotalPln);
        Assert.False(result.IsStale);

        var line = Assert.Single(result.Lines);
        Assert.Equal(assetId, line.AssetId);
        Assert.Equal(4000m, line.ValuePln);
        Assert.Equal(100m, line.PriceUsed);
        Assert.Equal(SnapshotDate, line.PriceDate);
        Assert.Equal(4m, line.FxRateUsed);
        Assert.False(line.IsStale);
    }

    [Fact]
    public void Calculate_MarketAssetInBaseCurrency_SkipsFxLookup()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { MarketPosition(assetId, AssetClass.Stock, quantity: 5) };
        var prices = Prices((InstrumentId, "PLN", SnapshotDate, 200m));
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(1000m, result.TotalPln);
        Assert.False(result.IsStale);
        Assert.Equal(1000m, Assert.Single(result.Lines).ValuePln);
    }

    [Fact]
    public void Market_AppliesQuoteUnitsPerQuantity()
    {
        var assetId = Guid.NewGuid();
        var position = MarketPosition(assetId, AssetClass.PreciousMetal, quantity: 3) with { QuoteUnitsPerQuantity = 31.1034768m };
        var prices = Prices((InstrumentId, "USD", SnapshotDate, 133.92m));
        var fx = FxRates(("USDPLN", SnapshotDate, 3.9m));

        var result = ValuationAlgorithm.Calculate([position], prices, fx, SnapshotDate);

        var expected = 3m * 31.1034768m * 133.92m * 3.9m;
        Assert.Equal(expected, result.TotalPln);
        Assert.Equal(expected, Assert.Single(result.Lines).ValuePln);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(30, true)]
    public void Calculate_LastKnownPriceFallback_StalenessFollowsAgeInDays(int ageDays, bool expectedStale)
    {
        var quoteDate = SnapshotDate.AddDays(-ageDays);
        var assetId = Guid.NewGuid();
        var positions = new[] { MarketPosition(assetId, AssetClass.Stock, quantity: 1) };
        var prices = Prices((InstrumentId, "PLN", quoteDate, 100m));
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        // The last known price still values the position: staleness is a flag, not an exclusion.
        Assert.Equal(100m, result.TotalPln);
        Assert.Equal(expectedStale, result.IsStale);
        Assert.Equal(expectedStale, Assert.Single(result.Lines).IsStale);
    }

    [Fact]
    public void Calculate_FxRateOlderThanThreshold_MarksStaleEvenWithFreshPrice()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { MarketPosition(assetId, AssetClass.Stock, quantity: 1) };
        var prices = Prices((InstrumentId, "USD", SnapshotDate, 100m));
        var fx = FxRates(("USDPLN", SnapshotDate.AddDays(-10), 4m));

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(400m, result.TotalPln);
        Assert.True(result.IsStale);
        Assert.True(Assert.Single(result.Lines).IsStale);
    }

    [Fact]
    public void Calculate_MarketAssetWithNoQuoteAtAll_ProducesZeroValuedStaleLine_NeverDropped()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { MarketPosition(assetId, AssetClass.Stock, quantity: 10) };
        var prices = Prices();
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(0m, result.TotalPln);
        Assert.True(result.IsStale);

        // A missing quote never drops the line: it produces a zero-valued, stale one.
        var line = Assert.Single(result.Lines);
        Assert.Equal(assetId, line.AssetId);
        Assert.Equal(0m, line.ValuePln);
        Assert.Null(line.PriceUsed);
        Assert.Null(line.PriceDate);
        Assert.True(line.IsStale);
    }

    [Fact]
    public void Calculate_ManualAssetInForeignCurrency_ConvertsAtSnapshotDateRate()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { ManualPosition(assetId, AssetClass.RealEstate, 50_000m, "EUR") };
        var fx = FxRates(("EURPLN", SnapshotDate, 4.3m));

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), fx, SnapshotDate);

        Assert.Equal(215_000m, result.TotalPln);
        Assert.False(result.IsStale);

        var line = Assert.Single(result.Lines);
        Assert.Equal(215_000m, line.ValuePln);
        Assert.Null(line.PriceUsed);
        Assert.Null(line.PriceDate);
        Assert.Equal(4.3m, line.FxRateUsed);
    }

    [Fact]
    public void Calculate_ManualAssetInBaseCurrency_UsesValueDirectly()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { ManualPosition(assetId, AssetClass.Other, 12_345.67m, "PLN") };

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), FxRates(), SnapshotDate);

        Assert.Equal(12_345.67m, result.TotalPln);
        Assert.False(result.IsStale);
        Assert.Equal(12_345.67m, Assert.Single(result.Lines).ValuePln);
    }

    [Fact]
    public void Calculate_MixOfMarketAndManualAcrossAssetClasses_ProducesOneLineEachSummingToTheTotal()
    {
        var stockInstrumentId = Guid.NewGuid();
        var stockAssetId = Guid.NewGuid();
        var realEstateAssetId = Guid.NewGuid();
        var positions = new ValuationPosition[]
        {
            new() { AssetId = stockAssetId, AssetClass = AssetClass.Stock, ValuationMode = AssetValuationMode.Market, Currency = "PLN", Quantity = 2, InstrumentId = stockInstrumentId },
            new() { AssetId = realEstateAssetId, AssetClass = AssetClass.RealEstate, ValuationMode = AssetValuationMode.Manual, Currency = "PLN", Quantity = 0, ManualValueAmount = 300_000m },
        };
        var prices = Prices((stockInstrumentId, "PLN", SnapshotDate, 150m));

        var result = ValuationAlgorithm.Calculate(positions, prices, FxRates(), SnapshotDate);

        Assert.Equal(300_300m, result.TotalPln);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(result.Lines.Sum(l => l.ValuePln), result.TotalPln);

        var stockLine = Assert.Single(result.Lines, l => l.AssetId == stockAssetId);
        Assert.Equal(AssetClass.Stock, stockLine.AssetClass);
        Assert.Equal(300m, stockLine.ValuePln);

        var realEstateLine = Assert.Single(result.Lines, l => l.AssetId == realEstateAssetId);
        Assert.Equal(AssetClass.RealEstate, realEstateLine.AssetClass);
        Assert.Equal(300_000m, realEstateLine.ValuePln);
    }

    [Fact]
    public void Calculate_ThreeValuationModes_ProducesOneLineEach()
    {
        var marketAssetId = Guid.NewGuid();
        var manualAssetId = Guid.NewGuid();
        var cashAssetId = Guid.NewGuid();
        var stockInstrumentId = Guid.NewGuid();

        var positions = new ValuationPosition[]
        {
            new() { AssetId = marketAssetId, AssetClass = AssetClass.Stock, ValuationMode = AssetValuationMode.Market, Currency = "PLN", Quantity = 10, InstrumentId = stockInstrumentId },
            new() { AssetId = manualAssetId, AssetClass = AssetClass.RealEstate, ValuationMode = AssetValuationMode.Manual, Currency = "PLN", Quantity = 0, ManualValueAmount = 300_000m },
            new() { AssetId = cashAssetId, AssetClass = AssetClass.Cash, ValuationMode = AssetValuationMode.CurrencyValued, Currency = "EUR", Quantity = 1_000m },
        };

        var prices = Prices((stockInstrumentId, "PLN", SnapshotDate, 150m));
        var fx = FxRates(("EURPLN", SnapshotDate, 4.30m));

        var result = ValuationAlgorithm.Calculate(positions, prices, fx, SnapshotDate);

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(1_500m + 300_000m + 4_300m, result.TotalPln);
        Assert.False(result.IsStale);

        var marketLine = Assert.Single(result.Lines, l => l.AssetId == marketAssetId);
        Assert.Equal(1_500m, marketLine.ValuePln);
        Assert.Equal(150m, marketLine.PriceUsed);
        Assert.Equal(SnapshotDate, marketLine.PriceDate);
        Assert.False(marketLine.IsStale);

        var manualLine = Assert.Single(result.Lines, l => l.AssetId == manualAssetId);
        Assert.Equal(300_000m, manualLine.ValuePln);
        Assert.Null(manualLine.PriceUsed);
        Assert.Null(manualLine.PriceDate);
        Assert.False(manualLine.IsStale);

        var cashLine = Assert.Single(result.Lines, l => l.AssetId == cashAssetId);
        Assert.Equal(4_300m, cashLine.ValuePln);
        Assert.Null(cashLine.PriceUsed);
        Assert.Null(cashLine.PriceDate);
        Assert.Equal(4.30m, cashLine.FxRateUsed);
        Assert.False(cashLine.IsStale);
    }

    [Fact]
    public void Calculate_CurrencyValuedAssetInBaseCurrency_SkipsFxLookup()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { CurrencyValuedPosition(assetId, AssetClass.Cash, quantity: 1_500m, currency: "PLN") };

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), FxRates(), SnapshotDate);

        Assert.Equal(1_500m, result.TotalPln);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void Calculate_CurrencyValuedAssetInForeignCurrency_ConvertsThroughFxRate()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { CurrencyValuedPosition(assetId, AssetClass.Cash, quantity: 1_000m, currency: "EUR") };
        var fx = FxRates(("EURPLN", SnapshotDate, 4.30m));

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), fx, SnapshotDate);

        // 1000 EUR x 4.30 PLN/EUR = 4300 PLN.
        Assert.Equal(4_300m, result.TotalPln);
        Assert.False(result.IsStale);
        Assert.Equal(4.30m, Assert.Single(result.Lines).FxRateUsed);
    }

    [Fact]
    public void Calculate_CurrencyValuedAssetWithStaleRate_ValuesButMarksStale()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { CurrencyValuedPosition(assetId, AssetClass.Deposit, quantity: 500m, currency: "USD") };
        var fx = FxRates(("USDPLN", SnapshotDate.AddDays(-10), 3.65m));

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), fx, SnapshotDate);

        // The last known rate still values the position: staleness is a flag, not an exclusion.
        Assert.Equal(1_825m, result.TotalPln);
        Assert.True(result.IsStale);
        Assert.True(Assert.Single(result.Lines).IsStale);
    }

    [Fact]
    public void Calculate_CurrencyValuedAssetWithNoRateAtAll_ProducesZeroValuedStaleLine_NeverZeroByAccident()
    {
        var assetId = Guid.NewGuid();
        var positions = new[] { CurrencyValuedPosition(assetId, AssetClass.Cash, quantity: 1_000m, currency: "EUR") };
        var fx = FxRates();

        var result = ValuationAlgorithm.Calculate(positions, ImmutablePrices(), fx, SnapshotDate);

        // A missing rate still gives exactly one line, valued at 0 and flagged stale.
        Assert.Equal(0m, result.TotalPln);
        Assert.True(result.IsStale);

        var line = Assert.Single(result.Lines);
        Assert.Equal(assetId, line.AssetId);
        Assert.Equal(0m, line.ValuePln);
        Assert.True(line.IsStale);
    }

    private static ValuationPosition MarketPosition(Guid assetId, AssetClass assetClass, decimal quantity) => new()
    {
        AssetId = assetId,
        AssetClass = assetClass,
        ValuationMode = AssetValuationMode.Market,
        Currency = "PLN",
        Quantity = quantity,
        InstrumentId = InstrumentId,
    };

    private static ValuationPosition ManualPosition(Guid assetId, AssetClass assetClass, decimal manualValue, string currency) => new()
    {
        AssetId = assetId,
        AssetClass = assetClass,
        ValuationMode = AssetValuationMode.Manual,
        Currency = currency,
        Quantity = 0,
        ManualValueAmount = manualValue,
    };

    private static ValuationPosition CurrencyValuedPosition(Guid assetId, AssetClass assetClass, decimal quantity, string currency) => new()
    {
        AssetId = assetId,
        AssetClass = assetClass,
        ValuationMode = AssetValuationMode.CurrencyValued,
        Currency = currency,
        Quantity = quantity,
    };

    private static Dictionary<Guid, InstrumentPriceLookup> Prices(params (Guid InstrumentId, string Currency, DateOnly Date, decimal Close)[] entries) =>
        entries.ToDictionary(e => e.InstrumentId, e => new InstrumentPriceLookup(e.Currency, e.Date, e.Close));

    private static Dictionary<Guid, InstrumentPriceLookup> ImmutablePrices() => [];

    private static Dictionary<string, FxRateLookup> FxRates(params (string Pair, DateOnly Date, decimal Rate)[] entries) =>
        entries.ToDictionary(e => e.Pair, e => new FxRateLookup(e.Date, e.Rate));
}
