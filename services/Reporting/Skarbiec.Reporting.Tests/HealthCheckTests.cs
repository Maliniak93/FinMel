using System.Net;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class HealthCheckTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task Ready_ReturnsHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();

        // MassTransit's health check briefly reports not started after the host comes up, so this polls.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        HttpResponseMessage response;
        do
        {
            response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
