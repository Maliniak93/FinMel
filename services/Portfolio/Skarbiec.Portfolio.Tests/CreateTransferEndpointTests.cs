using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class CreateTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_CashToSavings_MovesAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.TransferId);

        Assert.Equal(3_000m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await client.GetAssetAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken)).Quantity);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg => Assert.Equal(body.TransferId, leg.TransferId));
        Assert.All(legs, leg => Assert.Equal(DefaultTransferDate, leg.Date));
        Assert.All(legs, leg => Assert.Equal(2_000m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(1m, leg.UnitPriceAmount));
        Assert.Contains(legs, l => l.AssetId == setup.CashAssetId && l.Type == TransactionType.Withdraw);
        Assert.Contains(legs, l => l.AssetId == setup.SavingsAssetId && l.Type == TransactionType.Deposit);

        var cashLeg = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        Assert.NotNull(cashLeg.Transfer);
        Assert.Equal(setup.SavingsAssetId, cashLeg.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, cashLeg.Transfer.Direction);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken);
    }

    [Fact]
    public async Task Create_SavingsToCash_MovesAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));

        var response = await client.PostAsJsonAsync(
            TransfersUri,
            NewTransferRequest(setup.SavingsAssetId, setup.CashAssetId, 500m, date: new DateOnly(2026, 1, 25)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1_500m, (await client.GetAssetAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken)).Quantity);
        Assert.Equal(3_500m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("cash-to-deposit", true)]
    [InlineData("cash-to-stock", true)]
    [InlineData("eur-cash-to-pln-savings", true)]
    [InlineData("same-asset", true)]
    [InlineData("unknown-asset", true)]
    [InlineData("zero-amount", false)]
    [InlineData("tomorrow", false)]
    public async Task Create_InvalidInput_ReturnsBadRequest(string invalidCase, bool counterpartError)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var request = invalidCase switch
        {
            "cash-to-deposit" => NewTransferRequest(
                setup.CashAssetId,
                (await client.AddDepositAsync(setup.SavingsPortfolioId, cancellationToken, NewDepositRequest(name: "Other deposit"))).AssetId),
            "cash-to-stock" => NewTransferRequest(
                setup.CashAssetId,
                await client.AddAssetAsync(setup.CashPortfolioId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock)),
            "eur-cash-to-pln-savings" => NewTransferRequest(
                await client.AddCashAssetWithBalanceAsync(setup.CashPortfolioId, cancellationToken, currency: "EUR", name: "EUR cash"),
                setup.SavingsAssetId,
                amount: 100m),
            "same-asset" => NewTransferRequest(setup.CashAssetId, setup.CashAssetId),
            "unknown-asset" => NewTransferRequest(setup.CashAssetId, Guid.NewGuid()),
            "zero-amount" => NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, amount: 0m),
            "tomorrow" => NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, date: SavingsToday.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(TransfersUri, request, cancellationToken);

        if (counterpartError)
        {
            await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task Create_ExceedsBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 5_001m), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task Create_SourceEmptyOnTransferDate_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken, toppedUpOn: new DateOnly(2026, 1, 25));
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 100m, DefaultTransferDate), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Create_ArchivedPortfolio_ReturnsConflict(string archivedSide)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        await client.ArchivePortfolioAsync(archivedSide == "source" ? setup.CashPortfolioId : setup.SavingsPortfolioId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
