using System.Net;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Tenancy;

// A stranger gets 404, never 403, which would leak that the resource exists.
public abstract class TenancyIsolationTests<TProgram> : ServiceEndpointTests<TProgram> where TProgram : class
{
    protected abstract Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken);

    protected abstract Uri ListUrl { get; }

    // Any well-formed payload: the PUT must 404 before validation runs.
    protected abstract HttpContent CreateUpdatePayload();

    protected abstract Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken);

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(resourceUrl, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsync(resourceUrl, CreateUpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceUrl = await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(resourceUrl, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStranger_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateResourceAsOwnerAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(ListUrl, cancellationToken);

        await AssertResourceAbsentFromListAsync(response, cancellationToken);
    }

    private async Task<Uri> CreateResourceAsOwnerAsync(CancellationToken cancellationToken)
    {
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        return await CreateResourceAsync(owner, cancellationToken);
    }
}
