using System.Net.Http.Headers;
using Skarbiec.Testing.Auth;

namespace Skarbiec.Portfolio.Tests;

internal static class AuthenticatedClientExtensions
{
    public static HttpClient CreateAuthenticatedClient(this PortfolioApiFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId));
        return client;
    }
}
