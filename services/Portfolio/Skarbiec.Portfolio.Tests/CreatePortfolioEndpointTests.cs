using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class CreatePortfolioEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Create_WithValidRequest_ReturnsCreatedWithLocation()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreatePortfolioRequest { Name = "Retirement", Description = "Long-term", Currency = "PLN" };

        var response = await client.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(request.Name, body.Name);
        Assert.False(body.IsArchived);
        Assert.Equal($"{PortfoliosUri}/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Create_WithoutCurrency_DefaultsToPln()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.PostAsJsonAsync(PortfoliosUri, new { Name = "Emergency Fund" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("PLN", body!.Currency);
    }

    [Fact]
    public async Task Create_WithDuplicateNameForSameUser_ReturnsConflict()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" };

        var first = await client.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameForDifferentUsers_BothSucceed()
    {
        using var clientA = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var clientB = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" };

        var responseA = await clientA.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);
        var responseB = await clientB.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyName_ReturnsBadRequestWithFieldDetails()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreatePortfolioRequest { Name = "", Currency = "PLN" };

        var response = await client.PostAsJsonAsync(PortfoliosUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(CreatePortfolioRequest.Name), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_WithNamePropertyMissingEntirely_ReturnsBadRequest()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(PortfoliosUri, new { Currency = "PLN" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal(400, problem!.Status);
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
