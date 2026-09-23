using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class DailyPricesSyncedContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "daily-prices-synced-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<DailyPricesSynced>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.RunId);
        Assert.Equal(new DateOnly(2026, 1, 15), evt.SyncDate);
        Assert.Equal(42, evt.SyncedCount);
        Assert.Equal(1, evt.FailedCount);
        Assert.Equal(2, evt.NoDataCount);
    }

    /// <summary>spec-04 AC20: an Fx-kind payload with an unknown extra field still deserializes, and
    /// the new <c>Kind</c> field (design decision 8: <c>PriceSyncKind</c>, edited in place per
    /// ADR-019) maps to <see cref="PriceSyncKind.Fx"/>.</summary>
    [Fact]
    public void Deserialize_FxKindPayload_MapsToFx()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "daily-prices-synced-fx-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<DailyPricesSynced>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(PriceSyncKind.Fx, evt.Kind);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.RunId);
        Assert.Equal(new DateOnly(2026, 1, 15), evt.SyncDate);
        Assert.Equal(4, evt.SyncedCount);
    }
}
