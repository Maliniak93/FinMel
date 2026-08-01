using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Identity.Tests.Fixtures.IdentityApi;

namespace Skarbiec.Identity.Tests;

// HandleCookies is disabled so each request's refresh cookie is exactly what the test attaches,
// never one implicitly carried over by the client's own cookie jar from a previous call.
[Collection(TestingDefaults.CollectionName)]
public sealed class LogoutEndpointTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    [Fact]
    public async Task Logout_WithValidCookie_ReturnsNoContentAndClearsCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var refreshToken = await client.RegisterAndLoginAsync(cancellationToken);

        var response = await client.PostWithRefreshCookieAsync(LogoutUri, refreshToken, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, ExtractRefreshTokenCookieValue(response));
    }

    [Fact]
    public async Task Logout_ThenRefresh_ReturnsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var refreshToken = await client.RegisterAndLoginAsync(cancellationToken);

        var logoutResponse = await client.PostWithRefreshCookieAsync(LogoutUri, refreshToken, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await client.PostWithRefreshCookieAsync(RefreshUri, refreshToken, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutCookie_ReturnsNoContent()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUri);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
