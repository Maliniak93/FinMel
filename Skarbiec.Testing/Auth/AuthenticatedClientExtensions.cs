using System.Net.Http.Headers;
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
}
