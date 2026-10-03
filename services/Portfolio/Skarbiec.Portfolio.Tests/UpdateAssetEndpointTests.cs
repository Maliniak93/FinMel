using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ExistingAsset_ReturnsOkWithUpdatedFieldsAndQuantityUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        // The asset has a transaction, so its currency is locked; the update keeps PLN and changes everything else.
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Crypto,
            Name = "Renamed",
            Currency = "PLN",
            ManualValue = 42.42m,
            ManualValueDate = new DateOnly(2026, 6, 15)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetClass.Crypto, body!.AssetClass);
        Assert.Equal("Renamed", body.Name);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(3m, body.Quantity);
        Assert.Equal(42.42m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 6, 15), body.ManualValueDate);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Update_WithSupportedCurrency_ReturnsOk(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = $"Cash in {currency}",
            Currency = currency,
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(currency, body!.Currency);
    }

    [Fact]
    public async Task Update_WithUnsupportedCurrency_ReturnsBadRequestNamingAcceptedSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Swiss cash",
            Currency = "CHF",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdateAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            problem.Errors.Values.SelectMany(messages => messages),
            message => message.Contains(SupportedCurrencies.Accepted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Still here",
            Currency = "PLN",
            ManualValue = -10m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Missing",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SwitchManualAssetToMarket_NullsManualValueAndKeepsTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var instrumentId = Guid.NewGuid();
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Now market",
            Currency = "PLN",
            InstrumentId = instrumentId
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(instrumentId, body!.InstrumentId);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
        // The switch to Market must not disturb the quantity the earlier Buy produced.
        Assert.Equal(5m, body.Quantity);

        var transactions = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Single(transactions.Items);
    }

    [Fact]
    public async Task Update_SwitchMarketAssetBackToManual_ClearsInstrumentId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.PutAsJsonAsync(
            AssetUri(portfolioId, assetId),
            new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Market first", Currency = "USD", InstrumentId = Guid.NewGuid() },
            cancellationToken);

        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Back to manual",
            Currency = "PLN",
            ManualValue = 250m,
            ManualValueDate = new DateOnly(2026, 2, 1)
        };
        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Null(body!.InstrumentId);
        Assert.Equal(250m, body.ManualValue);
    }

    [Fact]
    public async Task Update_SwitchManualCashToCurrencyValued_ClearsManualValueAndSetsModeQuantityFromTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 500m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Now currency-valued",
            Currency = "PLN",
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetValuationMode.CurrencyValued, body!.ValuationMode);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
        Assert.Null(body.InstrumentId);
        Assert.Equal(500m, body.Quantity);
    }

    [Fact]
    public async Task Update_ToCashLikeClassWithDisallowedTransactions_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock, manualValue: 100m);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Now cash",
            Currency = "PLN",
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(AssetClass.Stock, unchanged.AssetClass);
        Assert.Equal("Shares", unchanged.Name);
        Assert.Equal(AssetValuationMode.Manual, unchanged.ValuationMode);
        Assert.Equal(100m, unchanged.ManualValue);
        Assert.Equal(3m, unchanged.Quantity);
    }

    [Fact]
    public async Task Update_ToCashLikeClassWithOnlyDepositTransactions_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Parked money", assetClass: AssetClass.Stock, manualValue: 100m);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 200m, new DateOnly(2026, 1, 2), cancellationToken, unitPrice: 1m);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Now cash",
            Currency = "PLN",
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetClass.Cash, body!.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, body.ValuationMode);
        Assert.Equal(500m, body.Quantity);
    }

    [Fact]
    public async Task Update_NonCurrencyValuedClassWithNeitherInstrumentIdNorManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Update_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Wrong parent",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(otherPortfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangeCurrencyWithTransactions_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Locked", manualValue: 100m, currency: "PLN");
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Locked",
            Currency = "EUR",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdateAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("PLN", (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ChangeCurrencyWithoutTransactions_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Free", manualValue: 100m, currency: "PLN");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Free",
            Currency = "EUR",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal("EUR", body!.Currency);
        Assert.Equal("EUR", (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ChangeCurrencyOnArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Euro stock", manualValue: 100m, currency: "EUR");
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Euro stock",
            Currency = "USD",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal("EUR", (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Currency);
    }

    [Fact]
    public async Task Update_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: 100m, name: "Before archive");
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "After archive", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, cashId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.Equal("Before archive", unchanged.Name);
        Assert.Equal(100m, unchanged.Quantity);
        Assert.True(unchanged.IsArchived);
    }

    [Fact]
    public async Task Update_ArchivedAssetInArchivedPortfolio_ReturnsPortfolioArchivedConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Renamed", Currency = "PLN", ManualValue = 1m, ManualValueDate = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
    }

    [Fact]
    public async Task UpdateAsset_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Before archive", manualValue: 100m);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "After archive",
            Currency = "PLN",
            ManualValue = 999m,
            ManualValueDate = new DateOnly(2026, 6, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal("Before archive", unchanged.Name);
        Assert.Equal(100m, unchanged.ManualValue);
    }

    [Theory]
    [InlineData("cash-to-deposit")]
    [InlineData("deposit-to-cash")]
    [InlineData("deposit-stays-deposit")]
    public async Task Update_ToOrFromDepositClass_ReturnsBadRequest(string change)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-deposit")
        {
            assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Deposit);
        }
        else
        {
            assetId = (await client.AddDepositAsync(portfolioId, cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity) = (AssetClass.Deposit, "Term deposit", 10_000m);
            requestedClass = change == "deposit-to-cash" ? AssetClass.Cash : AssetClass.Deposit;
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertUseDepositEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
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
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-bond")
        {
            assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Bond);
        }
        else
        {
            assetId = (await client.AddBondAsync(portfolioId, cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity) = (AssetClass.Bond, "EDO1036", 5_000m);
            requestedClass = change == "bond-to-cash" ? AssetClass.Cash : AssetClass.Bond;
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertUseBondEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
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
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        Guid assetId;
        AssetClass originalClass;
        string originalName;
        decimal originalQuantity;
        AssetClass requestedClass;
        if (change == "cash-to-savings")
        {
            assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken, name: "Wallet");
            await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 300m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Cash, "Wallet", 300m, AssetClass.Savings);
        }
        else
        {
            assetId = (await client.AddSavingsAccountAsync(portfolioId, cancellationToken)).AssetId;
            (originalClass, originalName, originalQuantity, requestedClass) = (AssetClass.Savings, "Savings account", 10_000m, AssetClass.Cash);
        }

        var request = new UpdateAssetRequest { AssetClass = requestedClass, Name = "Edited through assets", Currency = "PLN" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertUseSavingsAccountEndpointsAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(originalClass, unchanged.AssetClass);
        Assert.Equal(originalName, unchanged.Name);
        Assert.Equal(originalQuantity, unchanged.Quantity);
    }
}
