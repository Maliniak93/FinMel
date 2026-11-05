using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class FxHistoryBackfilledContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "fx-history-backfilled-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<FxHistoryBackfilled>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(new DateOnly(2023, 11, 16), evt.From);
        Assert.Equal(new DateOnly(2025, 10, 22), evt.To);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), evt.OccurredAtUtc);
    }

    [Fact]
    public void Serialize_UsesCamelCaseIsoShape()
    {
        var evt = new FxHistoryBackfilled
        {
            From = new DateOnly(2023, 11, 16),
            To = new DateOnly(2025, 10, 22),
            OccurredAtUtc = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(["from", "to", "occurredAtUtc"], document.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2023-11-16", document.RootElement.GetProperty("from").GetString());
        Assert.Equal("2025-10-22", document.RootElement.GetProperty("to").GetString());
    }
}
