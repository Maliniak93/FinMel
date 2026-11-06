using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class AssetPositionChangedBondTermsContractTests
{
    [Fact]
    public void Deserialize_FixtureWithBondTermsAndUnknownFields_ReadsTerms()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "asset-position-changed-bond-terms-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<AssetPositionChanged>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(AssetClass.Bond, evt.AssetClass);
        var terms = evt.BondTerms;
        Assert.NotNull(terms);
        Assert.Equal("EDO0235", terms.SeriesCode);
        Assert.Equal(TreasuryBondType.Edo, terms.Type);
        Assert.Equal(new DateOnly(2025, 2, 10), terms.PurchaseDate);
        Assert.Equal(100m, terms.PurchasePricePerBond);
        Assert.Equal(6.5m, terms.FirstPeriodRatePercent);
        Assert.Equal(2m, terms.MarginPercent);
        Assert.Equal(50, terms.BondCountHistory[new DateOnly(2025, 2, 10)]);
        Assert.Equal(6.5m, terms.SettledPeriodRates[1]);
        Assert.False(terms.IsRedeemed);
    }
}
