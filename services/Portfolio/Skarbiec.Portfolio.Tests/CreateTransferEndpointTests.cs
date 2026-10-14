using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class CreateTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_CurrentToSavings_MovesAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 2_000m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.TransferId);

        Assert.Equal(3_000m, (await client.GetAssetAsync(setup.Current.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await client.GetAssetAsync(setup.Savings.MoneyAssetId, cancellationToken)).Quantity);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg => Assert.Equal(body.TransferId, leg.TransferId));
        Assert.All(legs, leg => Assert.Equal(DefaultTransferDate, leg.Date));
        Assert.All(legs, leg => Assert.Equal(2_000m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(1m, leg.UnitPriceAmount));
        Assert.Contains(legs, l => l.AssetId == setup.Current.MoneyAssetId && l.Type == TransactionType.Withdraw);
        Assert.Contains(legs, l => l.AssetId == setup.Savings.MoneyAssetId && l.Type == TransactionType.Deposit);

        var currentLeg = await client.GetCashWithdrawAsync(setup.Current.MoneyAssetId, cancellationToken);
        Assert.NotNull(currentLeg.Transfer);
        Assert.Equal(setup.Savings.MoneyAssetId, currentLeg.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, currentLeg.Transfer.Direction);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.Current.MoneyAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.Savings.MoneyAssetId, cancellationToken);
    }

    [Fact]
    public async Task Create_SavingsToCurrent_MovesAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);
        await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 2_000m));

        var response = await client.PostAsJsonAsync(
            TransfersUri,
            NewTransferRequest(setup.Savings.MoneyAssetId, setup.Current.MoneyAssetId, 500m, date: new DateOnly(2026, 1, 25)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1_500m, (await client.GetAssetAsync(setup.Savings.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Equal(3_500m, (await client.GetAssetAsync(setup.Current.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("cash-to-metal")]
    [InlineData("metal-to-cash")]
    public async Task MetalRoute_ReturnsBadRequest(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken, pieces: 3m);
        var request = route == "cash-to-metal"
            ? NewTransferRequest(setup.CashAssetId, setup.MetalAssetId, date: MetalCashDate)
            : NewTransferRequest(setup.MetalAssetId, setup.CashAssetId, date: MetalCashDate);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(TransfersUri, request, cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Theory]
    [InlineData("cash-to-deposit", true)]
    [InlineData("cash-to-stock", true)]
    [InlineData("free-cash-to-bank", true)]
    [InlineData("eur-bank-to-pln-bank", false)]
    [InlineData("same-asset", true)]
    [InlineData("unknown-asset", true)]
    [InlineData("zero-amount", false)]
    [InlineData("tomorrow", false)]
    public async Task Create_InvalidInput_ReturnsBadRequest(string invalidCase, bool counterpartError)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);
        var request = invalidCase switch
        {
            "cash-to-deposit" => NewTransferRequest(
                setup.Current.MoneyAssetId,
                (await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Other deposit"))).AssetId),
            "cash-to-stock" => NewTransferRequest(
                setup.Current.MoneyAssetId,
                await client.AddAssetAsync(await client.CreateAccountAsync(cancellationToken), cancellationToken, name: "Shares", assetClass: AssetClass.Stock)),
            "eur-bank-to-pln-bank" => NewTransferRequest(
                (await client.CreateBankAccountAsync(cancellationToken, "EUR account", currency: "EUR", balance: 200m, depositedOn: DefaultTopUpDate)).MoneyAssetId,
                setup.Savings.MoneyAssetId,
                amount: 100m),
            "free-cash-to-bank" => NewTransferRequest(
                await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Wallet"), setup.Savings.MoneyAssetId, amount: 100m),
            "same-asset" => NewTransferRequest(setup.Current.MoneyAssetId, setup.Current.MoneyAssetId),
            "unknown-asset" => NewTransferRequest(setup.Current.MoneyAssetId, Guid.NewGuid()),
            "zero-amount" => NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, amount: 0m),
            "tomorrow" => NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, date: FixedToday.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        var transferLegsBefore = await dbContext.Transactions.CountAsync(t => t.TransferId != null, cancellationToken);

        var response = await client.PostAsJsonAsync(TransfersUri, request, cancellationToken);

        if (counterpartError)
        {
            await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        }
        else if (invalidCase == "eur-bank-to-pln-bank")
        {
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(transferLegsBefore, await dbContext.Transactions.CountAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task Create_ExceedsBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 5_001m), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(setup.Current.MoneyAssetId, cancellationToken);
    }

    [Fact]
    public async Task Create_SourceEmptyOnTransferDate_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken, toppedUpOn: new DateOnly(2026, 1, 25));
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 100m, DefaultTransferDate), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Create_ArchivedAccount_ReturnsConflict(string archivedSide)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken, currentBalance: 0m);
        await client.ArchiveAccountAsync(archivedSide == "source" ? setup.Current.AccountId : setup.Savings.AccountId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId), cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
