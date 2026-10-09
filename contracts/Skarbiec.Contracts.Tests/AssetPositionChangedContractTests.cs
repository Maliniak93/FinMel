using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class AssetPositionChangedContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "asset-position-changed-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<AssetPositionChanged>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.AssetId);
        Assert.Equal(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), evt.PortfolioId);
        Assert.Equal(Guid.Parse("9c858901-8a57-4791-81fe-4c455b099bc9"), evt.UserId);
        Assert.Equal(AssetClass.Stock, evt.AssetClass);
        Assert.Equal(AssetValuationMode.Market, evt.ValuationMode);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), evt.InstrumentId);
        Assert.Equal("USD", evt.Currency);
        Assert.Equal(12.5m, evt.Quantity);
        Assert.Equal(1m, evt.QuoteUnitsPerQuantity);
        Assert.Null(evt.ManualValueAmount);
        Assert.Null(evt.ManualValueDate);
        Assert.False(evt.PortfolioIsArchived);
        Assert.True(evt.IsArchived);
        Assert.Equal(3L, evt.Version);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), evt.OccurredAtUtc);
    }

    [Fact]
    public void Deserialize_FixtureWithUnknownFields_KeepsEveryQuantityPoint()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "asset-position-changed-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<AssetPositionChanged>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(
            new (DateOnly Date, decimal Quantity)[] { (new DateOnly(2024, 3, 4), 15m), (new DateOnly(2025, 1, 10), 12.5m) },
            evt.QuantityHistory.Select(point => (point.Date, point.Quantity)));
    }
}
