using System.Net.Http.Headers;
using System.Security.Claims;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Auth;

/// <summary>
/// Creates <see cref="HttpClient"/>s that already carry a bearer token for a given user, so slice
/// tests act as a concrete tenant without going through registration/login (ADR-006).
/// </summary>
public static class AuthenticatedClientExtensions
{
    /// <summary>
    /// A client authenticated as <paramref name="userId"/>, signed with this host's own key
    /// (see <see cref="TestJwtIssuer.IssueAccessToken{TProgram}"/>). Pass a fresh
    /// <see cref="Guid.NewGuid"/> per test to keep tenants isolated from each other.
    /// </summary>
    public static HttpClient CreateAuthenticatedClient<TProgram>(this SkarbiecApiFactory<TProgram> factory, Guid userId)
        where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId));

        return client;
    }

    /// <summary>
    /// A client carrying a <see cref="SystemCaller"/> token (T2.11) — for exercising bulk/cross-user
    /// internal endpoints (e.g. Portfolio's positions-for-valuation) the way Reporting's consumer
    /// calls them, rather than as any particular user.
    /// </summary>
    public static HttpClient CreateSystemAuthenticatedClient<TProgram>(this SkarbiecApiFactory<TProgram> factory)
        where TProgram : class
    {
        var client = factory.CreateClient();
        var token = factory.IssueAccessToken(Guid.NewGuid(), [new Claim(SystemCaller.ClaimType, SystemCaller.ClaimValue)]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
