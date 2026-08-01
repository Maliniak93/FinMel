using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Identity.Tests.Fixtures.IdentityApi;

namespace Skarbiec.Identity.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class LoginEndpointTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndSetsRefreshCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var email = await client.RegisterAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.StartsWith("refreshToken=", StringComparison.Ordinal)
            && c.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase)
            && c.Contains("Secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_WithValidCredentials_AccessTokenAuthorizesProtectedEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var email = await client.RegisterAsync(cancellationToken);

        var loginResponse = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, cancellationToken);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, MeUri);
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var meResponse = await client.SendAsync(meRequest, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Login_AccessToken_HasFifteenMinuteLifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var email = await client.RegisterAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.AccessToken);
        var lifetimeMinutes = (jwt.ValidTo - jwt.ValidFrom).TotalMinutes;

        Assert.InRange(lifetimeMinutes, 14.9, 15.1);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var email = await client.RegisterAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = "WrongPassword!1" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            LoginUri,
            new LoginRequest { Email = $"{Guid.NewGuid()}@example.com", Password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
