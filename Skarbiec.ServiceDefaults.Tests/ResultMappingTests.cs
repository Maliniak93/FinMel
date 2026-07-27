using System.Net;

namespace Skarbiec.ServiceDefaults.Tests;

public sealed class ResultMappingTests(SampleAppFactory factory) : IClassFixture<SampleAppFactory>
{
    [Theory]
    [InlineData("ok", HttpStatusCode.OK)]
    [InlineData("not-found", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("anything-else", HttpStatusCode.BadRequest)]
    public async Task SampleResult_MapsToExpectedStatusCode(string outcome, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/sample-result/{outcome}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }
}
