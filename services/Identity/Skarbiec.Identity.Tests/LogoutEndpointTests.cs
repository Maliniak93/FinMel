using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Register;

namespace Skarbiec.Identity.Tests;

// HandleCookies is disabled so each request's refresh cookie is exactly what the test attaches,
// never one implicitly carried over by the client's own cookie jar from a previous call.
public sealed class LogoutEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string RegisterUri = "/api/identity/register";
    private const string LoginUri = "/api/identity/login";
    private const string LogoutUri = "/api/identity/logout";
    private const string RefreshUri = "/api/identity/refresh";
    private const string Password = "Str0ng!Passw0rd";

    [Fact]
    public async Task Logout_WithValidCookie_ReturnsNoContentAndClearsCookie()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var refreshToken = await RegisterAndLoginAsync(client);

        var response = await SendLogoutWithCookieAsync(client, refreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var refreshCookie = cookies!.Single(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.Equal(string.Empty, refreshCookie.Split(';')[0]["refreshToken=".Length..]);
    }

    [Fact]
    public async Task Logout_ThenRefresh_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var refreshToken = await RegisterAndLoginAsync(client);

        var logoutResponse = await SendLogoutWithCookieAsync(client, refreshToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        refreshRequest.Headers.Add("Cookie", $"refreshToken={refreshToken}");
        var refreshResponse = await client.SendAsync(refreshRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutCookie_ReturnsNoContent()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUri);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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

        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var refreshCookie = cookies!.Single(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));

        return refreshCookie.Split(';')[0]["refreshToken=".Length..];
    }

    private static async Task<HttpResponseMessage> SendLogoutWithCookieAsync(HttpClient client, string refreshTokenValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUri);
        request.Headers.Add("Cookie", $"refreshToken={refreshTokenValue}");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
