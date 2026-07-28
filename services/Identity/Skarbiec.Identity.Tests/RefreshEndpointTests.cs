using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Refresh;
using Skarbiec.Identity.Features.Register;

namespace Skarbiec.Identity.Tests;

// HandleCookies is disabled so each request's refresh cookie is exactly what the test attaches,
// never one implicitly carried over by the client's own cookie jar from a previous call.
public sealed class RefreshEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string RegisterUri = "/api/identity/register";
    private const string LoginUri = "/api/identity/login";
    private const string RefreshUri = "/api/identity/refresh";
    private const string Password = "Str0ng!Passw0rd";

    [Fact]
    public async Task Refresh_WithValidCookie_ReturnsNewAccessTokenAndInvalidatesOldRefreshToken()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var originalRefreshToken = await RegisterAndLoginAsync(client);

        var refreshResponse = await SendRefreshWithCookieAsync(client, originalRefreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var body = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));

        var rotatedRefreshToken = ExtractRefreshTokenCookieValue(refreshResponse);
        Assert.NotEqual(originalRefreshToken, rotatedRefreshToken);

        var reuseResponse = await SendRefreshWithCookieAsync(client, originalRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_RevokesWholeChain()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var originalRefreshToken = await RegisterAndLoginAsync(client);

        var firstRefresh = await SendRefreshWithCookieAsync(client, originalRefreshToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var rotatedRefreshToken = ExtractRefreshTokenCookieValue(firstRefresh);

        // Reusing the already-rotated token is a reuse-detection signal: it must fail...
        var reuseResponse = await SendRefreshWithCookieAsync(client, originalRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // ...and it must revoke the descendant it produced too, since that chain can no longer be trusted.
        var descendantResponse = await SendRefreshWithCookieAsync(client, rotatedRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, descendantResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageCookie_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await SendRefreshWithCookieAsync(client, "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequest { Email = email, Password = Password, DisplayName = "Ada Lovelace" };
        var registerResponse = await client.PostAsJsonAsync(RegisterUri, registerRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        return ExtractRefreshTokenCookieValue(loginResponse);
    }

    private static async Task<HttpResponseMessage> SendRefreshWithCookieAsync(HttpClient client, string refreshTokenValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        request.Headers.Add("Cookie", $"refreshToken={refreshTokenValue}");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string ExtractRefreshTokenCookieValue(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var refreshCookie = cookies!.Single(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));

        return refreshCookie.Split(';')[0]["refreshToken=".Length..];
    }
}
