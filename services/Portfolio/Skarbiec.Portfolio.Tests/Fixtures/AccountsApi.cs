using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.AddAsset;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of an account endpoint calls it directly.
internal static class AccountsApi
{
    public const string AccountsUri = "/api/portfolio/accounts";

    public static string AccountUri(Guid accountId) => $"{AccountsUri}/{accountId}";

    public static string ArchiveAccountUri(Guid accountId) => $"{AccountUri(accountId)}/archive";

    public static string RestoreAccountUri(Guid accountId) => $"{AccountUri(accountId)}/restore";

    public static async Task<Guid> CreateAccountAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "XTB",
        AccountType type = AccountType.Brokerage,
        TaxWrapper wrapper = TaxWrapper.Regular)
    {
        var response = await client.PostAsJsonAsync(
            AccountsUri, new CreateAccountRequest { Name = name, Type = type, Wrapper = wrapper }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!.Id;
    }

    public static async Task ArchiveAccountAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(ArchiveAccountUri(accountId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<AccountResponse> GetAccountAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AccountUri(accountId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!;
    }

    public static async Task<Guid> AddStockInAccountAsync(
        this HttpClient client,
        Guid accountId,
        CancellationToken cancellationToken,
        string name = "Stock",
        decimal quantity = 0m)
    {
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = name,
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };
        var response = await client.PostAsJsonAsync(PortfolioApi.AllAssetsUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var assetId = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;

        if (quantity > 0m)
        {
            await client.RecordTransactionAsync(
                assetId, TransactionType.Buy, quantity, PortfolioApi.DefaultTopUpDate, cancellationToken);
        }

        return assetId;
    }

    public static AddAssetRequest NewAccountlessCashRequest(Guid? accountId = null) => new()
    {
        AccountId = accountId,
        AssetClass = AssetClass.Cash,
        Name = "Wallet",
        Currency = "PLN"
    };
}
