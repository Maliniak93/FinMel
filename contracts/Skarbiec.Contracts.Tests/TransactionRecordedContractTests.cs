using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class TransactionRecordedContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "transaction-recorded-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<TransactionRecorded>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.TransactionId);
        Assert.Equal(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), evt.AssetId);
        Assert.Equal(Guid.Parse("9c858901-8a57-4791-81fe-4c455b099bc9"), evt.UserId);
        Assert.Equal(TransactionType.Buy, evt.Type);
        Assert.Equal(12.5m, evt.Quantity);
        Assert.Equal(new DateOnly(2026, 1, 15), evt.Date);
    }
}
