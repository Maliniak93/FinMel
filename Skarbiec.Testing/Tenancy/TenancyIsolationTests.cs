using System.Net;
using System.Net.Http.Headers;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Tenancy;

/// <summary>
/// Reusable HTTP-level tenancy isolation template (T0.14, ADR-006, E1): user A creates a resource,
/// then user B is proven unable to see or touch it — GET/PUT/DELETE all return 404 (never 403,
/// which would leak the resource's existence) and it's absent from user B's own listing. A concrete
/// service test inherits this per resource type (e.g. portfolio, asset, transaction — see T1.x)
/// and only supplies how to create/locate/list that one resource; the four isolation facts below
/// are proven identically every time.
/// </summary>
public abstract class TenancyIsolationTests<TProgram> : IAsyncLifetime where TProgram : class
{
    protected abstract SkarbiecApiFactory<TProgram> Factory { get; }

    /// <summary>Creates the resource as <paramref name="ownerClient"/> and returns its GET/PUT/DELETE URL.</summary>
    protected abstract Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken);

    /// <summary>The URL listing resources of this type for whichever user the request is authenticated as.</summary>
    protected abstract Uri ListUrl { get; }

    /// <summary>Body for the stranger's PUT attempt — any well-formed payload; it must 404 before validation ever runs.</summary>
    protected abstract HttpContent CreateUpdatePayload();

    /// <summary>Asserts the resource created by <see cref="CreateResourceAsync"/> is absent from a stranger's listing response.</summary>
    protected abstract Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken);

    public virtual ValueTask InitializeAsync() => new(Factory.ResetDatabaseAsync());

    public virtual ValueTask DisposeAsync() => Factory.DisposeAsync();

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(resourceUrl, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsync(resourceUrl, CreateUpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(resourceUrl, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStranger_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(ListUrl, cancellationToken);

        await AssertResourceAbsentFromListAsync(response, cancellationToken);
    }

    private async Task<Uri> CreateResourceAsOwnerAsync(CancellationToken cancellationToken)
    {
        using var owner = CreateAuthenticatedClient(Guid.NewGuid());
        return await CreateResourceAsync(owner, cancellationToken);
    }

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Factory.IssueAccessToken(userId));
        return client;
    }
}
