using System.Net;

namespace Skarbiec.ServiceDefaults.Tests;

public sealed class HealthCheckTests(SampleAppFactory factory) : IClassFixture<SampleAppFactory>
{
    [Fact]
    public async Task Live_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
