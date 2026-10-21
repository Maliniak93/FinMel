using System.Net;
using System.Text.Json;

namespace Skarbiec.ServiceDefaults.Tests;

public sealed class ProblemDetailsTests(SampleAppFactory factory) : IClassFixture<SampleAppFactory>
{
    [Theory]
    [InlineData("/boom", HttpStatusCode.InternalServerError)]
    [InlineData("/sample-result/not-found", HttpStatusCode.NotFound)]
    public async Task Error_ReturnsProblemDetailsWithTraceId(string path, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }
}
