using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Skarbiec.Identity.Features.Refresh;
using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Identity.Tests.Fixtures.IdentityApi;

namespace Skarbiec.Identity.Tests;

// HandleCookies is disabled so each request's refresh cookie is exactly what the test attaches,
// never one implicitly carried over by the client's own cookie jar from a previous call.
[Collection(TestingDefaults.CollectionName)]
public sealed class RefreshEndpointTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    [Fact]
    public async Task Refresh_WithValidCookie_ReturnsNewAccessTokenAndInvalidatesOldRefreshToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var originalRefreshToken = await client.RegisterAndLoginAsync(cancellationToken);

        var refreshResponse = await client.PostWithRefreshCookieAsync(RefreshUri, originalRefreshToken, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var body = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));

        var rotatedRefreshToken = ExtractRefreshTokenCookieValue(refreshResponse);
        Assert.NotEqual(originalRefreshToken, rotatedRefreshToken);

        var reuseResponse = await client.PostWithRefreshCookieAsync(RefreshUri, originalRefreshToken, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_RevokesWholeChain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var originalRefreshToken = await client.RegisterAndLoginAsync(cancellationToken);

        var firstRefresh = await client.PostWithRefreshCookieAsync(RefreshUri, originalRefreshToken, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var rotatedRefreshToken = ExtractRefreshTokenCookieValue(firstRefresh);

        // Reusing the already-rotated token is a reuse-detection signal: it must fail...
        var reuseResponse = await client.PostWithRefreshCookieAsync(RefreshUri, originalRefreshToken, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // ...and it must revoke the descendant it produced too, since that chain can no longer be trusted.
        var descendantResponse = await client.PostWithRefreshCookieAsync(RefreshUri, rotatedRefreshToken, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, descendantResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageCookie_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostWithRefreshCookieAsync(RefreshUri, "not-a-real-token", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
