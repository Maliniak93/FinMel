using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits: <c>GET /api/portfolio/deposits</c> lists every deposit of the user across all their
/// portfolios, with the portfolio's name and archived flag, the projection and the read-time status
/// (<c>Due</c> once the maturity date is on or before today's Europe/Warsaw date).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class ListDepositsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-12.</summary>
    [Fact]
    public async Task List_AcrossPortfolios_ReturnsProjectionAndStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-16 12:00.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 16, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // Matures 2026-04-15 — yesterday.
        var (savingsId, matured) = await client.CreatePortfolioWithDepositAsync(
            cancellationToken, NewDepositRequest(name: "Matured deposit"), portfolioName: "Savings");
        // Matures 2026-05-16 — next month: 12 000.00 at 6 % for 30 days → gross 59.18, tax 11.25, net 47.93.
        var (reserveId, running) = await client.CreatePortfolioWithDepositAsync(
            cancellationToken,
            NewDepositRequest(name: "Running deposit", principal: 12_000m, startDate: new DateOnly(2026, 4, 16), termLength: 1),
            portfolioName: "Reserve");

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        Assert.Equal(2, deposits.Count);

        var due = Assert.Single(deposits, d => d.AssetId == matured.AssetId);
        Assert.Equal(savingsId, due.PortfolioId);
        Assert.Equal("Savings", due.PortfolioName);
        Assert.False(due.PortfolioIsArchived);
        Assert.Equal("Matured deposit", due.Name);
        Assert.Equal(new DateOnly(2026, 4, 15), due.MaturityDate);
        Assert.Equal(147.95m, due.Projection.GrossInterest);
        Assert.Equal(28.12m, due.Projection.Tax);
        Assert.Equal(119.83m, due.Projection.NetInterest);
        Assert.Equal(10_119.83m, due.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Due, due.Status);

        var active = Assert.Single(deposits, d => d.AssetId == running.AssetId);
        Assert.Equal(reserveId, active.PortfolioId);
        Assert.Equal("Reserve", active.PortfolioName);
        Assert.Equal(new DateOnly(2026, 5, 16), active.MaturityDate);
        Assert.Equal(59.18m, active.Projection.GrossInterest);
        Assert.Equal(11.25m, active.Projection.Tax);
        Assert.Equal(47.93m, active.Projection.NetInterest);
        Assert.Equal(12_047.93m, active.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Active, active.Status);
    }

    /// <summary>
    /// "Today" is the Europe/Warsaw date, not the UTC one: at 2026-04-14 22:30 UTC it is already
    /// 2026-04-15 in Warsaw (CEST, UTC+2), so a deposit maturing 2026-04-15 is Due.
    /// </summary>
    [Fact]
    public async Task List_MaturityTodayInWarsawWhileUtcIsStillYesterday_IsDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 22, 30, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(deposit.AssetId, listed.AssetId);
        Assert.Equal(new DateOnly(2026, 4, 15), listed.MaturityDate);
        Assert.Equal(DepositStatus.Due, listed.Status);
    }

    /// <summary>The day before maturity (Warsaw) the deposit is still Active.</summary>
    [Fact]
    public async Task List_DayBeforeMaturityInWarsaw_IsActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-14 23:30 (CEST).
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 21, 30, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(DepositStatus.Active, listed.Status);
    }

    /// <summary>
    /// Only Deposit-class assets are listed, and a deposit whose portfolio has since been archived is
    /// still listed, flagged as archived.
    /// </summary>
    [Fact]
    public async Task List_DepositInArchivedPortfolioAndOtherAssets_ListsOnlyDepositsWithArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken, portfolioName: "Old savings");
        await client.AddCashAssetAsync(portfolioId, cancellationToken);
        await client.AddAssetAsync(portfolioId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock, manualValue: 100m);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(deposit.AssetId, listed.AssetId);
        Assert.Equal("Old savings", listed.PortfolioName);
        Assert.True(listed.PortfolioIsArchived);
    }

    [Fact]
    public async Task List_NoDeposits_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(AllDepositsUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
