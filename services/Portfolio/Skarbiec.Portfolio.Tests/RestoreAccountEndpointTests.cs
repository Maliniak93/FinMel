using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class RestoreAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Restore_ArchivedAccount_ClearsFlagAndAllowsWritesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var stockId = await client.AddStockInAccountAsync(accountId, cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.PostAsync(RestoreAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!.IsArchived);

        var record = await client.PostAsJsonAsync(
            TransactionsUri(stockId),
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = DefaultTopUpDate },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, record.StatusCode);

        var addAsset = await client.PostAsJsonAsync(
            AllAssetsUri,
            new AddAssetRequest
            {
                AccountId = accountId,
                AssetClass = AssetClass.Stock,
                Name = "Another stock",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, addAsset.StatusCode);
    }

    [Fact]
    public async Task Restore_NotArchivedAccount_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);

        var response = await client.PostAsync(RestoreAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!.IsArchived);
    }

    [Fact]
    public async Task Restore_NonExistentAccount_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(
            RestoreAccountUri(Guid.NewGuid()), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
