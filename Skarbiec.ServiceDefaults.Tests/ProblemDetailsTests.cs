using System.Net;
using System.Text.Json;

namespace Skarbiec.ServiceDefaults.Tests;

public sealed class ProblemDetailsTests(SampleAppFactory factory) : IClassFixture<SampleAppFactory>
{
    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithTraceId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/boom", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task FailedResult_ReturnsProblemDetailsWithTraceId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/sample-result/not-found", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }
}
