using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class AddAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.Bond)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.PreciousMetal)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public async Task Add_EveryAssetClassWithManualValue_ReturnsCreated(AssetClass assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = "Test asset",
            Currency = "USD",
            ManualValue = 1000.50m,
            ManualValueDate = new DateOnly(2026, 7, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(assetClass, body.AssetClass);
        Assert.Equal("USD", body.Currency);
        // No Quantity request field (M1.5) and no InitialTransaction here — quantity stays 0.
        Assert.Equal(0m, body.Quantity);
        Assert.Equal(0, body.TransactionCount);
        Assert.Equal(1000.50m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 7, 1), body.ManualValueDate);
        Assert.Equal(AssetUri(portfolioId, body.Id), response.Headers.Location?.OriginalString);
    }

    /// <summary>M1.5 AC: "Create without a transaction → asset exists, quantity 0, zero
    /// transactions." Checks all three surfaces: the create response, TransactionCount, and
    /// ListTransactions actually reporting an empty page — not just an untouched counter.</summary>
    [Fact]
    public async Task Add_WithoutInitialTransaction_CreatesAssetWithZeroQuantityAndNoTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new { AssetClass = AssetClass.RealEstate, Name = "Flat", ManualValue = 500000m, ManualValueDate = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(0m, body!.Quantity);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(0, body.TransactionCount);

        var transactions = await client.ListTransactionsAsync(portfolioId, body.Id, cancellationToken);
        Assert.Empty(transactions.Items);
        Assert.Equal(0, transactions.TotalCount);
    }

    /// <summary>M1.5 AC: "Create with one → quantity matches a from-scratch recompute, and the
    /// transaction is listed by ListTransactions."</summary>
    [Fact]
    public async Task Add_WithInitialTransaction_QuantityMatchesRecomputeAndTransactionIsListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = 5m,
                UnitPrice = 150m,
                Fee = 2m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(5m, body.Quantity);
        Assert.Equal(1, body.TransactionCount);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, body.Id, cancellationToken);

        var transactions = await client.ListTransactionsAsync(portfolioId, body.Id, cancellationToken);
        var listed = Assert.Single(transactions.Items);
        Assert.Equal(TransactionType.Buy, listed.Type);
        Assert.Equal(5m, listed.Quantity);
        Assert.Equal(150m, listed.UnitPrice);
        Assert.Equal(2m, listed.Fee);
        Assert.Equal(new DateOnly(2026, 1, 1), listed.Date);
    }

    /// <summary>M1.5 AC: "An invalid initial transaction → 400, and no asset is created (assert the
    /// portfolio's asset count is unchanged)." A Sell as the very first transaction oversells an
    /// empty position — the same <c>TransactionQuantityCalculator</c> check RecordTransaction relies
    /// on. Proves the "no half-created asset" rule directly against the Assets table, not just the
    /// denormalized counter.</summary>
    [Fact]
    public async Task Add_WithInvalidInitialTransaction_ReturnsBadRequestAndCreatesNoAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
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

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var portfolio = (await client.GetAsync(PortfolioUri(portfolioId), cancellationToken));
        var portfolioBody = await portfolio.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal(0, portfolioBody!.AssetCount);

        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Add_WithSupportedCurrency_ReturnsCreated(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = $"Cash in {currency}",
            Currency = currency,
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(currency, body!.Currency);
    }

    [Fact]
    public async Task Add_WithUnsupportedCurrency_ReturnsBadRequestNamingAcceptedSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Swiss cash",
            Currency = "CHF",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(AddAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            problem.Errors.Values.SelectMany(messages => messages),
            message => message.Contains(SupportedCurrencies.Accepted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Negative value",
            Currency = "PLN",
            ManualValue = -1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// M1.5: <c>AddAssetRequest</c> no longer has a directly-settable <c>Quantity</c> — the
    /// equivalent "negative quantity" input now lives on the optional <c>InitialTransaction</c>, and
    /// its own <c>[Range]</c> attribute (inherited from <see cref="RecordTransactionRequest"/>) is
    /// what rejects it, recursed into by .NET 10's Minimal API validation for nested properties.
    /// </summary>
    [Fact]
    public async Task Add_WithNegativeInitialTransactionQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Negative quantity",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1),
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Buy,
                Quantity = -5m,
                UnitPrice = 10m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(
            problem.Errors,
            e => e.Key.Contains(nameof(RecordTransactionRequest.Quantity), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Add_ForNonExistentPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Orphan",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Unauthorized",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(Guid.NewGuid()), request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithValidInstrument_ReturnsCreatedAsMarketAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = instrumentId
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(instrumentId, body.InstrumentId);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
    }

    [Fact]
    public async Task Add_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithBothInstrumentIdAndManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Ambiguous",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithNeitherInstrumentIdNorManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>M1.4: the third valuation mode — neither InstrumentId nor ManualValue — is only
    /// accepted for classes <c>AssetValuationModes.SupportsCurrencyValued</c> (Cash, Deposit); for
    /// every other class "neither" stays a 400 (see <see cref="Add_WithNeitherInstrumentIdNorManualValue_ReturnsBadRequest"/>,
    /// unmodified by this change).
    /// <para>
    /// M1.5: <c>Quantity</c> is no longer a directly-settable field, and it is exactly Cash/Deposit
    /// (currency-valued, value = <c>Quantity × FxRate</c>) where that carries the most weight — this
    /// is the test proving the replacement path (an initial Deposit transaction) gives the asset the
    /// same real, non-zero quantity the old direct-set field used to.
    /// </para></summary>
    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    public async Task Add_CurrencyValuedClassWithInitialDeposit_ReturnsCreatedAsCurrencyValuedWithQuantityFromTransaction(AssetClass assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = "Plain cash",
            Currency = "EUR",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Deposit,
                Quantity = 1_000m,
                UnitPrice = 1m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(AssetValuationMode.CurrencyValued, body.ValuationMode);
        Assert.Equal(1_000m, body.Quantity);
        Assert.Equal(1, body.TransactionCount);
        Assert.Null(body.InstrumentId);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
    }

    [Fact]
    public async Task Add_WithValidInstrument_ReturnsCreatedWithMarketValuationMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetValuationMode.Market, body!.ValuationMode);
    }

    [Fact]
    public async Task Add_EveryAssetClassWithManualValue_ReturnsManualValuationMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash, // manual stays available even for a currency-valued class (M1.4 decision).
            Name = "Manual cash",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1),
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetValuationMode.Manual, body!.ValuationMode);
    }

    [Fact]
    public async Task Add_AssetToPortfolio_MakesPortfolioDeleteConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.AddAssetAsync(portfolioId, cancellationToken, name: "Blocks portfolio delete", assetClass: AssetClass.Cash, manualValue: 1m);

        var deleteResponse = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }
}
