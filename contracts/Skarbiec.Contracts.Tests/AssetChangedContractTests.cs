using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

public sealed class AssetChangedContractTests
{
    [Fact]
    public void Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "asset-changed-with-extra-fields.json"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var evt = JsonSerializer.Deserialize<AssetChanged>(json, options);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), evt.AssetId);
        Assert.Equal(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), evt.PortfolioId);
        Assert.Equal(Guid.Parse("9c858901-8a57-4791-81fe-4c455b099bc9"), evt.UserId);
        Assert.Equal(AssetChangeKind.Created, evt.Kind);
    }
}
