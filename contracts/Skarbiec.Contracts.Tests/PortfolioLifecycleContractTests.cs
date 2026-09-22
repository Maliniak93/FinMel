using System.Text.Json;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Contracts.Tests;

/// <summary>
/// One fact per <c>Portfolio*</c> lifecycle event (spec-02 AC-15) — all three share the same
/// <c>{ PortfolioId, UserId, OccurredAtUtc }</c> shape, so one class covers all three fixtures.
/// </summary>
public sealed class PortfolioLifecycleContractTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_PortfolioArchivedFixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "portfolio-archived-with-extra-fields.json"));

        var evt = JsonSerializer.Deserialize<PortfolioArchived>(json, Options);

        AssertKnownFields(evt?.PortfolioId, evt?.UserId, evt?.OccurredAtUtc);
    }

    [Fact]
    public void Deserialize_PortfolioRestoredFixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "portfolio-restored-with-extra-fields.json"));

        var evt = JsonSerializer.Deserialize<PortfolioRestored>(json, Options);

        AssertKnownFields(evt?.PortfolioId, evt?.UserId, evt?.OccurredAtUtc);
    }

    [Fact]
    public void Deserialize_PortfolioDeletedFixtureWithUnknownFields_StillDeserializesKnownFields()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "portfolio-deleted-with-extra-fields.json"));

        var evt = JsonSerializer.Deserialize<PortfolioDeleted>(json, Options);

        AssertKnownFields(evt?.PortfolioId, evt?.UserId, evt?.OccurredAtUtc);
    }

    private static void AssertKnownFields(Guid? portfolioId, Guid? userId, DateTimeOffset? occurredAtUtc)
    {
        Assert.Equal(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), portfolioId);
        Assert.Equal(Guid.Parse("9c858901-8a57-4791-81fe-4c455b099bc9"), userId);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), occurredAtUtc);
    }
}
