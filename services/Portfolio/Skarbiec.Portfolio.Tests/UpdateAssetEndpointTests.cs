using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class UpdateAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ExistingAsset_ReturnsOkWithUpdatedFieldsAndQuantityUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var instrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Etf, "PLN");
        // The asset has a transaction, so its currency is locked; the update keeps PLN and changes everything else.
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Etf,
            Name = "Renamed",
            Currency = "PLN",
            InstrumentId = instrumentId
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetClass.Etf, body!.AssetClass);
        Assert.Equal(instrumentId, body.InstrumentId);
        Assert.Equal("Renamed", body.Name);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(3m, body.Quantity);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Update_WithSupportedCurrency_ReturnsOk(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = $"Stock in {currency}",
            Currency = currency,
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, currency)
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(currency, body!.Currency);
    }

    [Fact]
    public async Task Update_WithUnsupportedCurrency_ReturnsBadRequestNamingAcceptedSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Swiss cash",
            Currency = "CHF"
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdateAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            problem.Errors.Values.SelectMany(messages => messages),
            message => message.Contains(SupportedCurrencies.Accepted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Missing",
            Currency = "PLN"
        };

        var response = await client.PutAsJsonAsync(AssetUri(Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangeInstrument_KeepsTransactionsAndQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithInstrument(instrumentId, AssetClass.Stock, "PLN");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Now market",
            Currency = "PLN",
            InstrumentId = instrumentId
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(instrumentId, body!.InstrumentId);
        // The switch to Market must not disturb the quantity the earlier Buy produced.
        Assert.Equal(5m, body.Quantity);

        var transactions = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Single(transactions.Items);
    }

    [Fact]
    public async Task Update_ToCashLikeClassWithDisallowedTransactions_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Now cash",
            Currency = "PLN",
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(AssetClass.Stock, unchanged.AssetClass);
        Assert.Equal("Shares", unchanged.Name);
        Assert.Equal(AssetValuationMode.Market, unchanged.ValuationMode);
        Assert.Equal(3m, unchanged.Quantity);
    }

    [Theory]
    [InlineData("stock-to-cash", PortfolioAssertions.AccountNotAllowedErrorCode)]
    [InlineData("cash-to-stock", PortfolioAssertions.AccountRequiredErrorCode)]
    public async Task Update_ClassChangeAcrossTheAccountBoundary_ReturnsBadRequestAndLeavesAsset(string change, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, stockId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(cancellationToken, name: "Wallet");
        var (assetId, request) = change == "stock-to-cash"
            ? (stockId, new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Now cash", Currency = "PLN" })
            : (cashId, new UpdateAssetRequest
            {
                AssetClass = AssetClass.Stock,
                Name = "Now a stock",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
            });

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, errorCode, cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(change == "stock-to-cash" ? AssetClass.Stock : AssetClass.Cash, unchanged.AssetClass);
        Assert.Equal(change == "stock-to-cash" ? accountId : null, unchanged.AccountId);
    }

    [Fact]
    public async Task Update_MarketClassWithoutInstrumentId_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_InstrumentClassMismatch_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithInstrument(instrumentId, AssetClass.Etf, "PLN");
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Wrong class", Currency = "PLN", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.Equal("Validation.InstrumentAssetClassMismatch", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task Update_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangeCurrencyWithTransactions_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Locked", currency: "PLN");
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Locked",
            Currency = "EUR",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "EUR")
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdateAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("PLN", (await client.GetAssetAsync(assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ChangeCurrencyWithoutTransactions_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Free", currency: "PLN");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Free",
            Currency = "EUR",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "EUR")
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal("EUR", body!.Currency);
        Assert.Equal("EUR", (await client.GetAssetAsync(assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ChangeCurrencyInArchivedAccount_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Euro stock", currency: "EUR");
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 3m, new DateOnly(2026, 1, 2), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Euro stock",
            Currency = "USD",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "USD")
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal("EUR", (await client.GetAssetAsync(assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var cashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 100m, name: "Before archive");
        await client.ArchiveAssetAsync(cashId, cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "After archive", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(cashId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(cashId, cancellationToken);
        Assert.Equal("Before archive", unchanged.Name);
        Assert.Equal(100m, unchanged.Quantity);
        Assert.True(unchanged.IsArchived);
    }

    [Fact]
    public async Task Update_ArchivedAssetInArchivedAccount_ReturnsAccountArchivedConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Renamed", Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
    }

    [Fact]
    public async Task UpdateAsset_InArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Before archive");
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "After archive",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal("Before archive", unchanged.Name);
    }

    [Theory]
    [InlineData("cash-to-deposit")]
    [InlineData("deposit-to-cash")]
    [InlineData("deposit-stays-deposit")]
    public async Task Update_ToOrFromDepositClass_ReturnsBadRequest(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-deposit")
        {
            assetId = await client.AddCashAssetAsync(cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Deposit);
        }
        else
        {
            assetId = (await client.AddDepositAsync(cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity) = (AssetClass.Deposit, "Term deposit", 10_000m);
            requestedClass = change == "deposit-to-cash" ? AssetClass.Cash : AssetClass.Deposit;
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertUseDepositEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(originalClass, unchanged.AssetClass);
        Assert.Equal(originalName, unchanged.Name);
        Assert.Equal(originalQuantity, unchanged.Quantity);
    }

    [Theory]
    [InlineData("cash-to-bond")]
    [InlineData("bond-to-cash")]
    [InlineData("bond-stays-bond")]
    public async Task Update_ToOrFromBondClass_ReturnsBadRequest(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, type: AccountType.Bonds);
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-bond")
        {
            assetId = await client.AddCashAssetAsync(cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Bond);
        }
        else
        {
            assetId = (await client.AddBondAsync(accountId, cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity) = (AssetClass.Bond, "EDO1036", 5_000m);
            requestedClass = change == "bond-to-cash" ? AssetClass.Cash : AssetClass.Bond;
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertUseBondEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(originalClass, unchanged.AssetClass);
        Assert.Equal(originalName, unchanged.Name);
        Assert.Equal(originalQuantity, unchanged.Quantity);
    }

    [Theory]
    [InlineData("cash-to-savings")]
    [InlineData("savings-to-cash")]
    public async Task Update_ToOrFromSavings_ReturnsUseSavingsAccountEndpoints(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-savings")
        {
            assetId = await client.AddCashAssetAsync(cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Savings);
        }
        else
        {
            assetId = (await client.AddSavingsAccountAsync(cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Savings, "Savings account", 10_000m, AssetClass.Cash);
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertUseSavingsAccountEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(originalClass, unchanged.AssetClass);
        Assert.Equal(originalName, unchanged.Name);
        Assert.Equal(originalQuantity, unchanged.Quantity);
    }

    [Theory]
    [InlineData("cash-to-metal")]
    [InlineData("metal-to-cash")]
    [InlineData("metal-to-metal")]
    public async Task Update_ToOrFromPreciousMetal_ReturnsUseMetalEndpoints(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        AssetClass requestedClass;
        if (change == "cash-to-metal")
        {
            assetId = await client.AddCashAssetAsync(cancellationToken, name: "Wallet");
            (originalClass, originalName, requestedClass) = (AssetClass.Cash, "Wallet", AssetClass.PreciousMetal);
        }
        else
        {
            assetId = (await client.AddMetalAsync(cancellationToken)).AssetId;
            requestedClass = change == "metal-to-cash" ? AssetClass.Cash : AssetClass.PreciousMetal;
            (originalClass, originalName) = (AssetClass.PreciousMetal, "Maple Leaf 1 oz");
        }

        // An instrument keeps the request valid for PreciousMetal, so only the metal guard can reject it.
        var request = new UpdateAssetRequest
        {
            AssetClass = requestedClass,
            Name = "Edited through assets",
            Currency = "PLN",
            InstrumentId = requestedClass == AssetClass.PreciousMetal ? Guid.NewGuid() : null
        };

        var response = await client.PutAsJsonAsync(AssetUri(assetId), request, cancellationToken);

        await response.AssertUseMetalEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(originalClass, unchanged.AssetClass);
        Assert.Equal(originalName, unchanged.Name);
    }
}
