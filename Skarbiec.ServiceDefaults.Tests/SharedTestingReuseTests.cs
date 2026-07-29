using System.Net;
using System.Net.Http.Headers;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.ServiceDefaults.Tests;

// A second, independent proof of T0.9's reuse AC alongside Skarbiec.Identity.Tests: this project
// never references Identity, yet gets a working containers fixture and JWT test-auth helper for
// free from Skarbiec.Testing. No ResetDatabaseAsync call here — the Sample host has no EF/DB
// schema of its own (nothing to reset); that path is already exercised by Identity.Tests.
[Collection(TestingDefaults.CollectionName)]
public sealed class SharedTestingReuseTests(SkarbiecContainersFixture containers) : IAsyncDisposable
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
