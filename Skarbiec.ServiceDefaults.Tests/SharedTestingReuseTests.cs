using System.Net;
using System.Net.Http.Headers;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.ServiceDefaults.Tests;

// Never references Identity, yet gets a containers fixture and JWT helper from Skarbiec.Testing; there is no database to reset.
public sealed class SharedTestingReuseTests(SkarbiecContainersFixture containers) : IAsyncDisposable, IClassFixture<SkarbiecContainersFixture>
{
    private readonly SampleContainerApiFactory _factory = new(containers);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Secure_WithTokenFromSharedJwtIssuer_ReturnsThatUserId()
    {
        var userId = Guid.NewGuid();
        var token = _factory.IssueAccessToken(userId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(new Uri("/secure", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal($"\"{userId}\"", body);
    }
}
