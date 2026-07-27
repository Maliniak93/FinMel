using System.Net;
using System.Net.Http.Headers;

namespace Skarbiec.ServiceDefaults.Tests;

public sealed class AuthenticationTests(SampleAppFactory factory) : IClassFixture<SampleAppFactory>
{
    [Fact]
    public async Task Secure_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/secure", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Secure_WithGarbageToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync(new Uri("/secure", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_AnonymousEndpoint_DoesNotRequireAuth()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
