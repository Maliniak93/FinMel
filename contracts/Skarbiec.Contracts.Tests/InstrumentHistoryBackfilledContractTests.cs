using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class InstrumentHistoryBackfilledContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "instrument-history-backfilled-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<InstrumentHistoryBackfilled>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.InstrumentId);
        Assert.Equal(new DateOnly(2025, 3, 1), evt.From);
        Assert.Equal(new DateOnly(2025, 12, 31), evt.To);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), evt.OccurredAtUtc);
    }
}
