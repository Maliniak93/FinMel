using Skarbiec.Contracts;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class MfBondFileParserTests
{
    private static readonly string[] CurrentTypePrefixes = ["OTS", "ROR", "DOR", "TOS", "COI", "EDO", "ROS", "ROD"];

    private static IReadOnlyList<ParsedBondSeries> ParseFixture()
    {
        using var stream = new MemoryStream(RecordedResponse.ReadBytes(FakeMfBondSource.FixtureFileName));
        return MfBondFileParser.Parse(stream);
    }

    [Fact]
    public void Parse_ReadsTermsAndEveryPeriodRate()
    {
        var series = ParseFixture().ToDictionary(s => s.Code);

        var edo = series["EDO1036"];
        Assert.Equal(TreasuryBondType.Edo, edo.Type);
        Assert.Equal(new DateOnly(2026, 10, 1), edo.SaleStart);
        Assert.Equal(new DateOnly(2026, 10, 31), edo.SaleEnd);
        Assert.Equal(100.00m, edo.IssuePrice);
        Assert.Equal(99.90m, edo.SwapPrice);
        Assert.Equal(2.00m, edo.MarginPercent);
        Assert.Equal([5.35m], edo.PeriodRates);

        var ror = series["ROR0623"];
        Assert.Equal(TreasuryBondType.Ror, ror.Type);
        Assert.Equal([5.25m, 6.00m, 6.50m, 6.50m, .. Enumerable.Repeat(6.75m, 8)], ror.PeriodRates);
        Assert.Equal(0.00m, ror.MarginPercent);
        Assert.Equal(99.90m, ror.SwapPrice);

        var dor = series["DOR0624"];
        Assert.Equal(24, dor.PeriodRates.Count);
        Assert.Equal(0.25m, dor.MarginPercent);
    }

    [Fact]
    public void Parse_HandlesFixedMissingAndArchivalSheets()
    {
        var parsed = ParseFixture();
        var series = parsed.ToDictionary(s => s.Code);

        var ots = series["OTS0127"];
        Assert.Equal(100.00m, ots.SwapPrice);
        Assert.Equal([2.00m], ots.PeriodRates);
        Assert.Null(ots.MarginPercent);

        var ros = series["ROS1032"];
        Assert.Null(ros.SwapPrice);
        Assert.Equal(2.00m, ros.MarginPercent);

        var coi = series["COI0528"];
        Assert.Equal([6.55m, 6.15m, 4.25m], coi.PeriodRates);
        Assert.Equal(1.25m, coi.MarginPercent);

        Assert.All(parsed, s => Assert.Contains(s.Code[..3], CurrentTypePrefixes));
    }
}
