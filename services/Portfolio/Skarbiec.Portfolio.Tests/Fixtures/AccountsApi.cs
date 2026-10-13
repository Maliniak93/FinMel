using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.Securities;
using Skarbiec.Portfolio.Features.UpdateTransaction;

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

    public static readonly DateOnly FundingDay = new(2000, 1, 1);

    public static readonly DateOnly DrainDay = new(2099, 12, 31);

    // Covers any trade a generic test records; dated before every date those tests use.
    public static async Task FundAccountAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken, decimal amount = 1_000_000m)
    {
        var cashAssetId = await client.GetAccountCashAssetIdAsync(accountId, cancellationToken);
        await client.RecordTransactionAsync(
            cashAssetId, TransactionType.Deposit, amount, FundingDay, cancellationToken, unitPrice: 1m);
    }

    // An account archives only with its cash at zero, so the balance a test left behind is withdrawn first.
    public static async Task ArchiveAccountAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await client.GetAccountAsync(accountId, cancellationToken);
        if (account.CashBalance > 0m)
        {
            await client.RecordTransactionAsync(
                account.CashAssetId, TransactionType.Withdraw, account.CashBalance, DrainDay, cancellationToken, unitPrice: 1m);
        }

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
            await client.FundAccountAsync(accountId, cancellationToken, amount: quantity * 10m);
            await client.RecordTransactionAsync(
                assetId, TransactionType.Buy, quantity, PortfolioApi.DefaultTopUpDate, cancellationToken);
        }

        return assetId;
    }

    public static IQueryable<Asset> ExcludingAccountCash(this IQueryable<Asset> assets) =>
        assets.Where(a => a.AssetClass != AssetClass.Cash || a.AccountId == null);

    public static readonly DateTimeOffset AccountCashTodayUtc = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly CashDay1 = new(2026, 10, 12);

    public static readonly DateOnly CashDay2 = new(2026, 10, 13);

    public static readonly DateOnly CashDay3 = new(2026, 10, 14);

    public static readonly DateOnly CashDay4 = new(2026, 10, 15);

    public static async Task<Guid> GetAccountCashAssetIdAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken) =>
        (await client.GetAccountAsync(accountId, cancellationToken)).CashAssetId;

    public sealed record AccountWithCash(Guid AccountId, Guid CashAssetId);

    public static async Task<AccountWithCash> CreateAccountWithCashAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal balance = 0m,
        string name = "XTB",
        DateOnly? depositedOn = null,
        TaxWrapper wrapper = TaxWrapper.Regular)
    {
        var accountId = await client.CreateAccountAsync(cancellationToken, name, wrapper: wrapper);
        var cashAssetId = await client.GetAccountCashAssetIdAsync(accountId, cancellationToken);
        if (balance > 0m)
        {
            await client.RecordTransactionAsync(
                cashAssetId, TransactionType.Deposit, balance, depositedOn ?? CashDay1, cancellationToken, unitPrice: 1m);
        }

        return new AccountWithCash(accountId, cashAssetId);
    }

    public static Task<HttpResponseMessage> PostCashTransactionAsync(
        this HttpClient client,
        Guid cashAssetId,
        TransactionType type,
        decimal amount,
        DateOnly date,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            PortfolioApi.TransactionsUri(cashAssetId),
            new RecordTransactionRequest { Type = type, Quantity = amount, UnitPrice = 1m, Date = date },
            cancellationToken);

    public static Task<HttpResponseMessage> PutCashTransactionAsync(
        this HttpClient client,
        Guid cashAssetId,
        Guid transactionId,
        TransactionType type,
        decimal amount,
        DateOnly date,
        CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(
            PortfolioApi.TransactionUri(cashAssetId, transactionId),
            new UpdateTransactionRequest { Type = type, Quantity = amount, UnitPrice = 1m, Date = date },
            cancellationToken);

    public static async Task<decimal> GetCashBalanceAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken) =>
        (await client.GetAccountAsync(accountId, cancellationToken)).CashBalance;

    public sealed record BrokerageHolding(Guid AccountId, Guid CashAssetId, Guid AssetId);

    public static async Task<BrokerageHolding> CreateBrokerageHoldingAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m,
        AssetClass assetClass = AssetClass.Etf,
        string currency = "PLN",
        string accountName = "XTB IKE",
        DateOnly? depositedOn = null)
    {
        var (accountId, cashAssetId) = await client.CreateAccountWithCashAsync(
            cancellationToken, cashBalance, accountName, depositedOn);
        var assetId = await client.AddMarketAssetAsync(
            accountId, cancellationToken, assetClass, currency, FakeInstrumentLookupClient.IdFor(assetClass, currency), "Holding");

        return new BrokerageHolding(accountId, cashAssetId, assetId);
    }

    public static RecordTransactionRequest NewBrokerageTradeRequest(
        TransactionType type = TransactionType.Buy,
        decimal quantity = 10m,
        decimal unitPrice = 120m,
        DateOnly? date = null,
        decimal? feeAmount = null,
        Guid? cashAssetId = null,
        decimal? amountPln = null) => new()
        {
            Type = type,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Date = date ?? CashDay1,
            FeeAmount = feeAmount,
            CashAssetId = cashAssetId,
            AmountPln = amountPln
        };

    public static async Task<TransactionResponse> RecordBrokerageTradeAsync(
        this HttpClient client,
        Guid assetId,
        CancellationToken cancellationToken,
        TransactionType type = TransactionType.Buy,
        decimal quantity = 10m,
        decimal unitPrice = 120m,
        DateOnly? date = null,
        decimal? feeAmount = null,
        decimal? amountPln = null)
    {
        var response = await client.PostAsJsonAsync(
            PortfolioApi.TransactionsUri(assetId),
            NewBrokerageTradeRequest(type, quantity, unitPrice, date, feeAmount, amountPln: amountPln),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
    }

    public static async Task<SecuritiesResponse> GetSecuritiesAsync(
        this HttpClient client, AssetClass assetClass, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(PortfolioApi.AllSecuritiesUri(assetClass), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
    }

    public static string ContributionLimitUri(Guid accountId, int year) => $"{AccountUri(accountId)}/contribution-limits/{year}";

    public static string AccountContributionUri(Guid accountId, int year) => $"{AccountUri(accountId)}?year={year}";

    public sealed record ContributionView(int Year, decimal ContributedPln, decimal? LimitPln);

    public static async Task SetContributionLimitAsync(
        this HttpClient client, Guid accountId, int year, decimal amount, CancellationToken cancellationToken)
    {
        var response = await client.PutAsJsonAsync(ContributionLimitUri(accountId, year), new { amount }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<ContributionView?> GetContributionAsync(
        this HttpClient client, Guid accountId, int year, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AccountContributionUri(accountId, year), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.ReadContributionAsync(cancellationToken);
    }

    public static async Task<ContributionView?> ReadContributionAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var account = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);

        return account!["contribution"]?.Deserialize<ContributionView>(WebJson);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static AddAssetRequest NewAccountlessCashRequest(Guid? accountId = null) => new()
    {
        AccountId = accountId,
        AssetClass = AssetClass.Cash,
        Name = "Wallet",
        Currency = "PLN"
    };
}
