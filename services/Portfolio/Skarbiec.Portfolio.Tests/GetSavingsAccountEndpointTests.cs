using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetSavingsAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ReturnsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(taxExempt: true), portfolioName: "Retirement");
        await client.RecordTransactionAsync(
            portfolioId, account.AssetId, TransactionType.Withdraw, 2_500m, SavingsToday, cancellationToken, unitPrice: 1m);

        var fetched = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);

        Assert.Equal(account.AssetId, fetched.AssetId);
        Assert.Equal(portfolioId, fetched.PortfolioId);
        Assert.Equal("Retirement", fetched.PortfolioName);
        Assert.False(fetched.PortfolioIsArchived);
        Assert.False(fetched.IsArchived);
        Assert.Equal("Savings account", fetched.Name);
        Assert.Equal("Test bank", fetched.BankName);
        Assert.Equal("PLN", fetched.Currency);
        Assert.Equal(7_500m, fetched.Balance);
        Assert.Equal(5.25m, fetched.AnnualInterestRatePercent);
        Assert.True(fetched.TaxExempt);
    }

    [Fact]
    public async Task Get_ArchivedAccount_ReturnsItWithFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, account.AssetId, cancellationToken);

        var fetched = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);

        Assert.True(fetched.IsArchived);
        Assert.False(fetched.PortfolioIsArchived);
    }

    [Fact]
    public async Task Get_NotASavingsAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var forDeposit = await client.GetAsync(SavingsAccountUri(portfolioId, deposit.AssetId), cancellationToken);
        var forCash = await client.GetAsync(SavingsAccountUri(portfolioId, cashId), cancellationToken);
        var forUnknown = await client.GetAsync(SavingsAccountUri(portfolioId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, forDeposit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forCash.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forUnknown.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(SavingsAccountUri(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
