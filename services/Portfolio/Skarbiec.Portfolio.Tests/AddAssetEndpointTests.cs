using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    /// transaction is listed by ListTransactions." transactions-pln-value-and-fee-removal AC8: the
    /// listed transaction carries the asset's currency and its PLN value at the transaction-date
    /// rate (5 × 150 USD × 4.00), and no fee.</summary>
    [Fact]
    public async Task Add_WithInitialTransaction_QuantityMatchesRecomputeAndTransactionIsListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("USD", 4.00m);
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

        var listResponse = await client.GetAsync(TransactionsUri(portfolioId, body.Id), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listedJson = Assert.Single((await listResponse.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray());
        listedJson.AssertCarriesNoFee();
        var listed = listedJson.Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        Assert.Equal(TransactionType.Buy, listed.Type);
        Assert.Equal(5m, listed.Quantity);
        Assert.Equal(150m, listed.UnitPrice);
        Assert.Equal("USD", listed.Currency);
        Assert.Equal(3000.00m, listed.ValuePln);
        Assert.Equal(new DateOnly(2026, 1, 1), listed.Date);
        Assert.Equal(("USD", new DateOnly(2026, 1, 1)), Assert.Single(Factory.FxRateLookupClient.Calls));
    }

    /// <summary>transactions-pln-value-and-fee-removal: the initial transaction resolves its PLN rate
    /// like RecordTransaction does — MarketData being down is a 503 and no half-created asset is left
    /// behind.</summary>
    [Fact]
    public async Task Add_WithInitialTransactionWhenFxUnavailable_ReturnsServiceUnavailableAndCreatesNoAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Euro account",
            Currency = "EUR",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Deposit,
                Quantity = 500m,
                UnitPrice = 1m,
                Date = new DateOnly(2026, 3, 4)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(Factory.FxRateLookupClient.Calls);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    /// <summary>transactions-pln-value-and-fee-removal: a PLN asset's initial transaction is valued
    /// at rate 1 without asking MarketData.</summary>
    [Fact]
    public async Task Add_PlnAssetWithInitialTransaction_ValuePlnEqualsAmountWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Checking account",
            Currency = "PLN",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Deposit,
                Quantity = 1_000m,
                UnitPrice = 1m,
                Date = new DateOnly(2026, 3, 4)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        var listed = Assert.Single((await client.ListTransactionsAsync(portfolioId, body!.Id, cancellationToken)).Items);
        Assert.Equal("PLN", listed.Currency);
        Assert.Equal(1000.00m, listed.ValuePln);
        Assert.Empty(Factory.FxRateLookupClient.Calls);
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

    /// <summary>cash-transaction-types AC-3: a cash-like class's opening transaction must be a
    /// Deposit/Withdraw. Any other type is a 400 <c>Validation.TransactionTypeNotAllowed</c>, checked
    /// before the FX lookup, and no asset (nor transaction) is created.</summary>
    [Theory]
    [InlineData(AssetClass.Cash, TransactionType.Buy)]
    [InlineData(AssetClass.Deposit, TransactionType.Interest)]
    public async Task Add_CashLikeClassWithDisallowedInitialType_ReturnsBadRequestAndCreatesNoAsset(
        AssetClass assetClass, TransactionType initialType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = "Euro savings",
            Currency = "EUR",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = initialType,
                Quantity = 500m,
                UnitPrice = 1m,
                Date = new DateOnly(2026, 3, 4)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        Assert.Empty(Factory.FxRateLookupClient.Calls);
        var portfolio = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        var portfolioBody = await portfolio.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal(0, portfolioBody!.AssetCount);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
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

    /// <summary>archived-portfolio-out-of-net-worth AC6: an archived portfolio is read-only — adding
    /// an asset is a 409 <c>Conflict.PortfolioArchived</c> and no asset row is written.</summary>
    [Fact]
    public async Task AddAsset_ToArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Cash in an archived portfolio",
            Currency = "PLN",
            InitialTransaction = new RecordTransactionRequest
            {
                Type = TransactionType.Deposit,
                Quantity = 1_000m,
                UnitPrice = 1m,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    /// <summary>archived-portfolio-out-of-net-worth AC9: tenancy wins over the archived check — a
    /// stranger writing into someone else's archived portfolio gets 404, never a 409 that would
    /// leak the portfolio's existence (and its archived state).</summary>
    [Fact]
    public async Task AddAsset_ToStrangersArchivedPortfolio_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        await owner.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Sneaky cash",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await stranger.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(0, await dbContext.Assets.IgnoreQueryFilters().CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
    }
}
