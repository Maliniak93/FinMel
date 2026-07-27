using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class UserRegisteredContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "user-registered-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<UserRegistered>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.UserId);
        Assert.Equal("ada@example.com", evt.Email);
        Assert.Equal("Ada", evt.DisplayName);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00+00:00"), evt.OccurredAtUtc);
    }
}
