using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdatePortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_WithValidRequest_ChangesFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            PortfolioUri(portfolioId),
            new UpdatePortfolioRequest { Name = "Retirement (renamed)", Description = "Updated", Currency = "USD" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal("Retirement (renamed)", body!.Name);
        Assert.Equal("USD", body.Currency);
    }

    [Fact]
    public async Task Update_ToNameTakenByAnotherOwnPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreatePortfolioAsync(cancellationToken, name: "Taken");
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Free");

        var response = await client.PutAsJsonAsync(
            PortfolioUri(portfolioId), new UpdatePortfolioRequest { Name = "Taken", Currency = "PLN" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            PortfolioUri(Guid.NewGuid()),
            new UpdatePortfolioRequest { Name = "Anything", Currency = "PLN" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Update_WithSupportedCurrency_ReturnsOk(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            PortfolioUri(portfolioId),
            new UpdatePortfolioRequest { Name = "Renamed", Currency = currency },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal(currency, body!.Currency);
    }

    [Fact]
    public async Task Update_WithUnsupportedCurrency_ReturnsBadRequestNamingAcceptedSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            PortfolioUri(portfolioId),
            new UpdatePortfolioRequest { Name = "Renamed", Currency = "GBP" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdatePortfolioRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            problem.Errors.Values.SelectMany(messages => messages),
            message => message.Contains(SupportedCurrencies.Accepted, StringComparison.Ordinal));
    }
}
