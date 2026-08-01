using System.Net;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Register;

namespace Skarbiec.Identity.Tests.Fixtures;

/// <summary>
/// Identity's HTTP surface as arrange-step helpers: route constants, the "get me a registered /
/// logged-in user" calls, and the refresh-cookie plumbing every token-rotation test needs.
/// </summary>
/// <remarks>
/// Arrange only — <see cref="RegisterAsync"/> and <see cref="RegisterAndLoginAsync"/> assert
/// success, so a test asserting on register or login itself must call those endpoints directly.
/// </remarks>
internal static class IdentityApi
{
    public const string RegisterUri = "/api/identity/register";
    public const string LoginUri = "/api/identity/login";
    public const string LogoutUri = "/api/identity/logout";
    public const string RefreshUri = "/api/identity/refresh";
    public const string MeUri = "/api/identity/me";

    /// <summary>Meets the Identity password policy — use for any user a test isn't specifically testing the policy with.</summary>
    public const string Password = "Str0ng!Passw0rd";

    public const string DisplayName = "Ada Lovelace";

    /// <summary>
    /// Registers a user and returns its e-mail. Defaults to a unique address so tests running
    /// against the same database never collide on the unique e-mail index; pass
    /// <paramref name="email"/> only when the address itself is part of the fact.
    /// </summary>
    public static async Task<string> RegisterAsync(this HttpClient client, CancellationToken cancellationToken, string? email = null)
    {
        email ??= $"{Guid.NewGuid()}@example.com";
        var request = new RegisterRequest { Email = email, Password = Password, DisplayName = DisplayName };

        var response = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return email;
    }

    /// <summary>Registers a user, logs it in, and returns the refresh token from the login's Set-Cookie.</summary>
    public static async Task<string> RegisterAndLoginAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var email = await client.RegisterAsync(cancellationToken);

        var loginResponse = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        return ExtractRefreshTokenCookieValue(loginResponse);
    }

    /// <summary>
    /// POSTs to <paramref name="requestUri"/> with an explicit <c>refreshToken</c> cookie. Tests
    /// that use this create their client with <c>HandleCookies = false</c>, so the cookie on the
    /// wire is exactly the one passed here rather than one the client carried over implicitly.
    /// </summary>
    public static async Task<HttpResponseMessage> PostWithRefreshCookieAsync(
        this HttpClient client, string requestUri, string refreshTokenValue, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Cookie", $"refreshToken={refreshTokenValue}");

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>The <c>refreshToken</c> value from a response's Set-Cookie header. Fails the test if there isn't one.</summary>
    public static string ExtractRefreshTokenCookieValue(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var refreshCookie = cookies!.Single(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));

        return refreshCookie.Split(';')[0]["refreshToken=".Length..];
    }
}
