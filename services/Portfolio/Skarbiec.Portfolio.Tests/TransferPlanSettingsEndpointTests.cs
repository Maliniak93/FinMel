using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanSettingsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Put_ValidMinimum_IsUsedByThePreview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var byDefault = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var saved = await client.PutAsJsonAsync(TransferPlanSettingsUri, new { minimumTransferPln = 250 }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(100, byDefault.MinimumTransferPln);
        Assert.Equal(250, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).MinimumTransferPln);

        await client.SaveTransferPlanSettingsAsync(2_000, cancellationToken);
        var strict = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var inwestycje = strict.Rows.Where(r => r.BucketName == "Inwestycje").ToList();
        var row = Assert.Single(inwestycje);
        Assert.Equal(1_979.27m, row.AmountPln);
        Assert.True(row.Marker.Is(TransferMarker.BelowMinimum));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12.5)]
    [InlineData(1_000_001)]
    public async Task Put_InvalidMinimum_ReturnsBadRequestAndKeepsTheSetting(double minimum)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await client.SaveTransferPlanSettingsAsync(250, cancellationToken);

        var response = await client.PutAsJsonAsync(TransferPlanSettingsUri, new { minimumTransferPln = minimum }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidMinimumTransferErrorCode, cancellationToken);
        Assert.Equal(250, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).MinimumTransferPln);
    }
}
