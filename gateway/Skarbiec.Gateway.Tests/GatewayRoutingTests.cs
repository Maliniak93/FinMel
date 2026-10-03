extern alias IdentityAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Skarbiec.Gateway.Tests.Infrastructure;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using LoginRequest = IdentityAssembly::Skarbiec.Identity.Features.Login.LoginRequest;
using LoginResponse = IdentityAssembly::Skarbiec.Identity.Features.Login.LoginResponse;
using RegisterRequest = IdentityAssembly::Skarbiec.Identity.Features.Register.RegisterRequest;

namespace Skarbiec.Gateway.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GatewayRoutingTests : IAsyncLifetime
{
    private const string Password = "Str0ng!Passw0rd";

    private readonly SkarbiecContainersFixture _containers;
    private readonly IdentityTestHost _identity;
    private readonly PortfolioTestHost _portfolio;
    private readonly GatewayTestHost _gateway;

    public GatewayRoutingTests(SkarbiecContainersFixture containers)
    {
        _containers = containers;
        _identity = new IdentityTestHost(containers);
        _portfolio = new PortfolioTestHost(containers);

        // Real Kestrel listeners: YARP proxies over a real HttpClient, which cannot reach an in-memory TestServer.
        _identity.StartServer();
        _portfolio.StartServer();

        _gateway = new GatewayTestHost(_identity.ClientOptions.BaseAddress, _portfolio.ClientOptions.BaseAddress);
    }

    public async ValueTask InitializeAsync() => await _containers.ResetDatabaseAsync();

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _portfolio.DisposeAsync();
        await _identity.DisposeAsync();
    }

    [Fact]
    public async Task ProtectedRoute_WithoutToken_Returns401AtGateway()
    {
        using var client = _gateway.CreateClient();

        var response = await client.GetAsync("/api/portfolio/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ThroughGateway_AccessTokenAuthorizesProtectedCallOnSkeletonService()
    {
        using var client = _gateway.CreateClient();
        var email = $"{Guid.NewGuid()}@example.com";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/identity/register",
            new RegisterRequest { Email = email, Password = Password, DisplayName = "Ada Lovelace" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new LoginRequest { Email = email, Password = Password },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/portfolio/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var meResponse = await client.SendAsync(meRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_ThroughGateway_ReachableWithoutToken()
    {
        using var client = _gateway.CreateClient();

        var response = await client.GetAsync("/api/portfolio/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InternalPath_ThroughGateway_Returns404()
    {
        // Service-only endpoints sit outside the /api/<service> space the Gateway routes, so a browser cannot reach them.
        using var client = _gateway.CreateClient();

        var instrument = await client.GetAsync($"/internal/instruments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var pricesBatch = await client.PostAsJsonAsync(
            "/internal/prices/latest-batch",
            new { InstrumentIds = new[] { Guid.NewGuid() }, AsOfDate = new DateOnly(2026, 8, 10) },
            TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.NotFound, instrument.StatusCode),
            () => Assert.Equal(HttpStatusCode.NotFound, pricesBatch.StatusCode));
    }

    [Fact]
    public async Task Login_BurstBeyondRateLimit_Returns429()
    {
        using var client = _gateway.CreateClient();
        var request = new LoginRequest { Email = $"{Guid.NewGuid()}@example.com", Password = Password };

        HttpStatusCode? lastStatus = null;
        for (var attempt = 0; attempt < 10 && lastStatus != HttpStatusCode.TooManyRequests; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/identity/login", request, TestContext.Current.CancellationToken);
            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }
}
