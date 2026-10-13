using System.Net;
using System.Net.Http.Json;
using Skarbiec.Gateway.Tests.Infrastructure;

namespace Skarbiec.Gateway.Tests;

public sealed class GatewayRateLimitTests
{
    private static async Task<(StubUpstream Upstream, GatewayTestHost Gateway)> StartAsync(params (string Key, string Value)[] settings)
    {
        var upstream = await StubUpstream.StartAsync();
        var gateway = new GatewayTestHost(upstream.Address, upstream.Address, settings.ToDictionary(s => s.Key, s => (string?)s.Value));
        return (upstream, gateway);
    }

    private static async Task<List<HttpStatusCode>> GetStandardAsync(HttpClient client, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync("/api/portfolio/me", TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static async Task<List<HttpStatusCode>> PostLoginAsync(HttpClient client, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/identity/login",
                new { Email = "a@example.com", Password = "x" },
                TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static void AssertLimitedAfter(List<HttpStatusCode> statuses, int permits) =>
        Assert.Multiple(
            () => Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(permits)),
            () => Assert.Equal(HttpStatusCode.TooManyRequests, statuses[permits]));

    [Fact]
    public async Task StandardRoute_WithDefaultConfig_Returns429OnThe101stRequest()
    {
        var (upstream, gateway) = await StartAsync();
        await using var _ = upstream;
        await using var __ = gateway;
        using var client = gateway.CreateClient();

        AssertLimitedAfter(await GetStandardAsync(client, 101), 100);
    }

    [Fact]
    public async Task AuthRoute_WithDefaultConfig_Returns429OnThe6thRequest()
    {
        var (upstream, gateway) = await StartAsync();
        await using var _ = upstream;
        await using var __ = gateway;
        using var client = gateway.CreateClient();

        AssertLimitedAfter(await PostLoginAsync(client, 6), 5);
    }

    [Fact]
    public async Task StandardRoute_WithConfiguredPermitLimit_Returns429OnTheFourthRequest()
    {
        var (upstream, gateway) = await StartAsync(("RateLimiting:Standard:PermitLimit", "3"));
        await using var _ = upstream;
        await using var __ = gateway;
        using var client = gateway.CreateClient();

        AssertLimitedAfter(await GetStandardAsync(client, 4), 3);
    }

    [Fact]
    public async Task AuthRoute_WithConfiguredPermitLimit_Returns429OnTheFourthRequest()
    {
        var (upstream, gateway) = await StartAsync(("RateLimiting:Auth:PermitLimit", "3"));
        await using var _ = upstream;
        await using var __ = gateway;
        using var client = gateway.CreateClient();

        AssertLimitedAfter(await PostLoginAsync(client, 4), 3);
    }
}
