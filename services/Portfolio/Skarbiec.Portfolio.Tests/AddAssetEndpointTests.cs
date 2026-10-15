using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Account_RulesPerClass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var brokerageId = await client.CreateAccountAsync(cancellationToken, "XTB", AccountType.Brokerage);
        var bondsId = await client.CreateAccountAsync(cancellationToken, "Obligacje", AccountType.Bonds);
        var stock = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Stock",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };

        var withoutAccount = await client.PostAsJsonAsync(AllAssetsUri, stock, cancellationToken);
        var intoBonds = await client.PostAsJsonAsync(AllAssetsUri, stock with { AccountId = bondsId }, cancellationToken);
        var intoBrokerage = await client.PostAsJsonAsync(AllAssetsUri, stock with { AccountId = brokerageId }, cancellationToken);

        await withoutAccount.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.AccountRequiredErrorCode, cancellationToken);
        await intoBonds.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.AccountTypeMismatchErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, intoBrokerage.StatusCode);
        var created = await intoBrokerage.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(brokerageId, created!.AccountId);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(1, await dbContext.Assets.ExcludingAccountCash().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task CashRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);

        var free = await client.PostAsJsonAsync(AllAssetsUri, NewAccountlessCashRequest(), cancellationToken);
        var inAccount = await client.PostAsJsonAsync(AllAssetsUri, NewAccountlessCashRequest(accountId), cancellationToken);

        await free.AssertProblemAsync(HttpStatusCode.BadRequest, CashPlacesApi.UseCashPlaceEndpointsErrorCode, cancellationToken);
        await inAccount.AssertProblemAsync(HttpStatusCode.BadRequest, CashPlacesApi.UseCashPlaceEndpointsErrorCode, cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.ExcludingAccountCash().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_IntoUnknownAccount_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddAssetRequest
        {
            AccountId = Guid.NewGuid(),
            AssetClass = AssetClass.Stock,
            Name = "Stock",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_Bond_UseBondEndpoints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Bond, Name = "Treasury bond", Currency = "PLN" };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        await response.AssertUseBondEndpointsAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_PreciousMetal_UseMetalEndpoints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.PreciousMetal,
            Name = "Gold coin",
            Currency = "PLN",
            InstrumentId = Guid.NewGuid()
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        await response.AssertUseMetalEndpointsAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithInitialTransaction_QuantityMatchesRecomputeAndTransactionIsListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("USD", 4.00m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = 5m,
                UnitPrice = 150m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(5m, body.Quantity);
        Assert.Equal(1, body.TransactionCount);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(body.Id, cancellationToken);

        var listResponse = await client.GetAsync(TransactionsUri(body.Id), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listedJson = Assert.Single((await listResponse.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray());
        var listed = listedJson.Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        Assert.Null(listed.FeeAmount);
        Assert.Equal(TransactionType.Buy, listed.Type);
        Assert.Equal(5m, listed.Quantity);
        Assert.Equal(150m, listed.UnitPrice);
        Assert.Equal("USD", listed.Currency);
        Assert.Equal(3000.00m, listed.ValuePln);
        Assert.Equal(new DateOnly(2026, 1, 1), listed.Date);
        Assert.Equal(("USD", new DateOnly(2026, 1, 1)), Assert.Single(Factory.FxRateLookupClient.Calls));
    }

    [Fact]
    public async Task Add_WithInvalidInitialTransaction_ReturnsBadRequestAndCreatesNoAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Oversold from birth",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Sell,
                Quantity = 5m,
                UnitPrice = 100m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.ExcludingAccountCash().CountAsync(a => a.AccountId == accountId, cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithNegativeInitialTransactionQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Negative quantity",
            Currency = "PLN",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = -5m,
                UnitPrice = 10m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(
            problem.Errors,
            e => e.Key.Contains(nameof(RecordTransactionRequest.Quantity), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Unauthorized",
            Currency = "PLN"
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithValidInstrument_ReturnsCreatedAsMarketAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = instrumentId
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(instrumentId, body.InstrumentId);
    }

    [Fact]
    public async Task Add_InstrumentClassMismatch_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithInstrument(instrumentId, AssetClass.Etf, "PLN");
        var request = new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Wrong class", Currency = "PLN", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.Equal("Validation.InstrumentAssetClassMismatch", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task Add_InstrumentCurrencyMismatch_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithInstrument(instrumentId, AssetClass.Stock, "EUR");
        var request = new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Wrong currency", Currency = "PLN", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.Equal("Validation.InstrumentCurrencyMismatch", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task Add_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Add_MarketClassWithoutInstrumentId_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var request = new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithValidInstrument_ReturnsCreatedWithMarketValuationMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetValuationMode.Market, body!.ValuationMode);
    }

    [Fact]
    public async Task AddAsset_ToArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Stock in an archived account",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = 10m,
                UnitPrice = 100m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.ExcludingAccountCash().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AddAsset_ToStrangersArchivedAccount_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);
        await owner.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Sneaky stock",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };

        var response = await stranger.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(0, await dbContext.Assets.IgnoreQueryFilters().ExcludingAccountCash().CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("currency-valued")]
    [InlineData("currency-valued-with-opening-deposit")]
    public async Task Add_DepositClass_ReturnsBadRequest(string shape)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Deposit, Name = "Term deposit", Currency = "PLN" };
        request = shape switch
        {
            "currency-valued" => request,
            "currency-valued-with-opening-deposit" => request with
            {
                InitialTransaction = new RecordTransactionRequest
                {
                    Type = TransactionType.Deposit,
                    Quantity = 1_000m,
                    UnitPrice = 1m,
                    Date = new DateOnly(2026, 1, 1)
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        await response.AssertUseDepositEndpointsAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("currency-valued")]
    [InlineData("currency-valued-with-opening-deposit")]
    public async Task Add_BankAccountClass_ReturnsUseAccountEndpoints(string shape)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = new AddAssetRequest { AssetClass = AssetClass.BankAccount, Name = "Bank account", Currency = "PLN" };
        request = shape switch
        {
            "currency-valued" => request,
            "currency-valued-with-opening-deposit" => request with
            {
                InitialTransaction = new RecordTransactionRequest
                {
                    Type = TransactionType.Deposit,
                    Quantity = 1_000m,
                    UnitPrice = 1m,
                    Date = new DateOnly(2026, 1, 1)
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        await response.AssertUseAccountEndpointsAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task InitialBuy_UsesAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var poor = await client.CreateAccountWithCashAsync(cancellationToken, balance: 100m, name: "Poor");
        var rich = await client.CreateAccountWithCashAsync(cancellationToken, balance: 200m, name: "Rich");
        AddAssetRequest NewStock(Guid accountId) => new()
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Stock",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = 1m,
                UnitPrice = 120m,
                Date = CashDay1
            }
        };

        var rejected = await client.PostAsJsonAsync(AllAssetsUri, NewStock(poor.AccountId), cancellationToken);

        await rejected.AssertInsufficientFundsAsync(cancellationToken);
        await using (var dbContext = CreateDbContext(userId))
        {
            Assert.Empty(await dbContext.Assets.ExcludingAccountCash().ToListAsync(cancellationToken));
            Assert.Equal(2, await dbContext.Transactions.CountAsync(cancellationToken));
            Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        }

        var created = await client.PostAsJsonAsync(AllAssetsUri, NewStock(rich.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var asset = (await created.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        Assert.Equal(1m, asset.Quantity);
        Assert.Equal(80m, await client.GetCashBalanceAsync(rich.AccountId, cancellationToken));
        await using var db = CreateDbContext(userId);
        var legs = await db.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(TransactionType.Buy, Assert.Single(legs, l => l.AssetId == asset.Id).Type);
        var withdraw = Assert.Single(legs, l => l.AssetId == rich.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(120m, withdraw.Quantity);
    }

    [Fact]
    public async Task AddAsset_InitialTransactionWithAmountPln_StoresIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (accountId, _) = await client.CreateAccountWithCashAsync(cancellationToken, 5_000m);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Etf,
            Name = "VWCE",
            Currency = "EUR",
            InstrumentId = instrumentId,
            InitialTransaction = NewBrokerageTradeRequest(
                quantity: 10m, unitPrice: 100m, date: CashDay1, feeAmount: 5m, amountPln: 4_312.50m)
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        var listed = Assert.Single((await client.ListTransactionsAsync(asset.Id, cancellationToken)).Items);
        Assert.Equal(4_312.50m, listed.AmountPln);
        await using var dbContext = CreateDbContext(userId);
        var stored = await dbContext.Transactions.SingleAsync(t => t.AssetId == asset.Id, cancellationToken);
        Assert.Equal(4_312.50m, stored.AmountPln);
        Assert.Equal(4.3125m, stored.FxRateToPln);
        Assert.Equal(682.50m, await client.GetCashBalanceAsync(accountId, cancellationToken));
    }
}
