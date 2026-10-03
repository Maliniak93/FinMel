using System.Net;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Register;

namespace Skarbiec.Identity.Tests.Fixtures;

// Arrange only: RegisterAsync and RegisterAndLoginAsync assert success, so a test of register or login calls the endpoint directly.
internal static class IdentityApi
{
    public const string RegisterUri = "/api/identity/register";
    public const string LoginUri = "/api/identity/login";
    public const string LogoutUri = "/api/identity/logout";
    public const string RefreshUri = "/api/identity/refresh";
    public const string MeUri = "/api/identity/me";

    public const string Password = "Str0ng!Passw0rd";

    public const string DisplayName = "Ada Lovelace";

    // A unique address by default, so tests sharing a database never collide on the unique e-mail index.
    public static async Task<string> RegisterAsync(this HttpClient client, CancellationToken cancellationToken, string? email = null)
    {
        email ??= $"{Guid.NewGuid()}@example.com";
        var request = new RegisterRequest { Email = email, Password = Password, DisplayName = DisplayName };

        var response = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return email;
    }

    public static async Task<string> RegisterAndLoginAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var email = await client.RegisterAsync(cancellationToken);

        var loginResponse = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        return ExtractRefreshTokenCookieValue(loginResponse);
    }

    public static async Task<HttpResponseMessage> PostWithRefreshCookieAsync(
        this HttpClient client, string requestUri, string refreshTokenValue, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Cookie", $"refreshToken={refreshTokenValue}");

        return await client.SendAsync(request, cancellationToken);
    }

    public static string ExtractRefreshTokenCookieValue(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var refreshCookie = cookies!.Single(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));

        return refreshCookie.Split(';')[0]["refreshToken=".Length..];
    }
}
