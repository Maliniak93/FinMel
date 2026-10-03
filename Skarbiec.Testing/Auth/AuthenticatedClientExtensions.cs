using System.Net.Http.Headers;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Auth;

public static class AuthenticatedClientExtensions
{
    // Pass a fresh Guid per test to keep tenants isolated.
    public static HttpClient CreateAuthenticatedClient<TProgram>(this SkarbiecApiFactory<TProgram> factory, Guid userId)
        where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId));

        return client;
    }
}
