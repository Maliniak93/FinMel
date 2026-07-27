using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Register;

namespace Skarbiec.Identity.Tests;

public sealed class LoginEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string RegisterUri = "/api/identity/register";
    private const string LoginUri = "/api/identity/login";
    private const string MeUri = "/api/identity/me";
    private const string Password = "Str0ng!Passw0rd";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndSetsRefreshCookie()
    {
        using var client = factory.CreateClient();
        var email = await RegisterUserAsync(client);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
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
        using var client = factory.CreateClient();
        var email = await RegisterUserAsync(client);

        var loginResponse = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, TestContext.Current.CancellationToken);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, MeUri);
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var meResponse = await client.SendAsync(meRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Login_AccessToken_HasFifteenMinuteLifetime()
    {
        using var client = factory.CreateClient();
        var email = await RegisterUserAsync(client);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = Password }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.AccessToken);
        var lifetimeMinutes = (jwt.ValidTo - jwt.ValidFrom).TotalMinutes;

        Assert.InRange(lifetimeMinutes, 14.9, 15.1);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        var email = await RegisterUserAsync(client);

        var response = await client.PostAsJsonAsync(
            LoginUri, new LoginRequest { Email = email, Password = "WrongPassword!1" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            LoginUri,
            new LoginRequest { Email = $"{Guid.NewGuid()}@example.com", Password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> RegisterUserAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var request = new RegisterRequest { Email = email, Password = Password, DisplayName = "Ada Lovelace" };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return email;
    }
}
