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

        // MassTransit's own health check (T2.11: Reporting's first real consumer, with the T0.12
        // inbox middleware on its receive endpoint) briefly reports "not started" right after the
        // host comes up — the same window a real readiness probe is meant to ride out, so this
        // polls instead of asserting on the very first response.
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
