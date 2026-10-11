using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondTransactionsManagedTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.Withdraw)]
    public async Task Record_OnBond_ReturnsConflict(TransactionType type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, bond) = await client.CreateAccountWithBondAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = type, Quantity = 500m, UnitPrice = 1m, Date = new DateOnly(2026, 10, 2) };

        var response = await client.PostAsJsonAsync(TransactionsUri(bond.AssetId), request, cancellationToken);

        await response.AssertBondTransactionsManagedAsync(cancellationToken);
        Assert.Equal(5_000m, (await client.GetAssetAsync(bond.AssetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(bond.AssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Update_OnBond_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, bond) = await client.CreateAccountWithBondAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(bond.AssetId, cancellationToken)).Items).Id;
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 9_000m, UnitPrice = 1m, Date = DefaultBondPurchaseDate };

        var response = await client.PutAsJsonAsync(TransactionUri(bond.AssetId, openingId), update, cancellationToken);

        await response.AssertBondTransactionsManagedAsync(cancellationToken);
        var opening = Assert.Single((await client.ListTransactionsAsync(bond.AssetId, cancellationToken)).Items);
        Assert.Equal(5_000m, opening.Quantity);
    }

    [Fact]
    public async Task Delete_OnBond_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, bond) = await client.CreateAccountWithBondAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(bond.AssetId, cancellationToken)).Items).Id;

        var response = await client.DeleteAsync(TransactionUri(bond.AssetId, openingId), cancellationToken);

        await response.AssertBondTransactionsManagedAsync(cancellationToken);
        Assert.Contains((await client.ListTransactionsAsync(bond.AssetId, cancellationToken)).Items, t => t.Id == openingId);
        Assert.Equal(5_000m, (await client.GetAssetAsync(bond.AssetId, cancellationToken)).Quantity);
    }
}
