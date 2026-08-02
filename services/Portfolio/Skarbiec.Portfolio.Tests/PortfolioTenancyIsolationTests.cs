using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// T1.6: Portfolio is a flat, top-level resource, so it plugs directly into the T0.14
/// <see cref="TenancyIsolationTests{TProgram}"/> template (same shape as the Notes sample).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioTenancyIsolationTests(SkarbiecContainersFixture containers) : TenancyIsolationTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    protected override async Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken)
    {
        var portfolioId = await ownerClient.CreatePortfolioAsync(cancellationToken);

        return new Uri(PortfolioUri(portfolioId), UriKind.Relative);
    }

    protected override Uri ListUrl { get; } = new(PortfoliosUri, UriKind.Relative);

    protected override HttpContent CreateUpdatePayload() =>
        JsonContent.Create(new UpdatePortfolioRequest { Name = "Stranger's edit", Currency = "PLN" });

    protected override async Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken)
    {
        var portfolios = await listResponse.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Empty(portfolios!);
    }
}
