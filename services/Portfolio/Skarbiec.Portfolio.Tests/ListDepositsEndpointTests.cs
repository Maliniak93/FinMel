using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListDepositsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsProjectionAndStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-16 12:00.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 16, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // Matures 2026-04-15 — yesterday.
        var matured = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(name: "Matured deposit"));
        // Matures 2026-05-16 — next month: 12 000.00 at 6 % for 30 days → gross 59.18, tax 11.25, net 47.93.
        var running = await client.AddDepositAsync(
            cancellationToken,
            NewDepositRequest(name: "Running deposit", principal: 12_000m, startDate: new DateOnly(2026, 4, 16), termLength: 1));

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        Assert.Equal(2, deposits.Count);

        var due = Assert.Single(deposits, d => d.AssetId == matured.AssetId);
        Assert.Equal("Matured deposit", due.Name);
        Assert.Equal(new DateOnly(2026, 4, 15), due.MaturityDate);
        Assert.Equal(147.95m, due.Projection.GrossInterest);
        Assert.Equal(28.12m, due.Projection.Tax);
        Assert.Equal(119.83m, due.Projection.NetInterest);
        Assert.Equal(10_119.83m, due.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Due, due.Status);

        var active = Assert.Single(deposits, d => d.AssetId == running.AssetId);
        Assert.Equal(new DateOnly(2026, 5, 16), active.MaturityDate);
        Assert.Equal(59.18m, active.Projection.GrossInterest);
        Assert.Equal(11.25m, active.Projection.Tax);
        Assert.Equal(47.93m, active.Projection.NetInterest);
        Assert.Equal(12_047.93m, active.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Active, active.Status);
    }

    [Fact]
    public async Task List_SettledDeposit_ReturnsSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settledDeposit = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(name: "Settled deposit"));
        var dueDeposit = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Due deposit"));
        await client.SettleDepositAsync(
            settledDeposit.AssetId,
            cancellationToken,
            NewSettleRequest(settledOn: new DateOnly(2026, 4, 16), grossInterest: 150.00m, tax: 28.50m));

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        Assert.Equal(2, deposits.Count);

        var settled = Assert.Single(deposits, d => d.AssetId == settledDeposit.AssetId);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(new DateOnly(2026, 4, 16), settled.SettledOn);
        Assert.Equal(150.00m, settled.SettledGrossInterest);
        Assert.Equal(28.50m, settled.SettledTax);
        Assert.Equal(new DateOnly(2026, 4, 15), settled.MaturityDate);

        var due = Assert.Single(deposits, d => d.AssetId == dueDeposit.AssetId);
        Assert.Equal(DepositStatus.Due, due.Status);
        Assert.Null(due.SettledOn);
        Assert.Null(due.SettledGrossInterest);
        Assert.Null(due.SettledTax);
    }

    [Fact]
    public async Task List_MaturityTodayInWarsawWhileUtcIsStillYesterday_IsDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 22, 30, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(deposit.AssetId, listed.AssetId);
        Assert.Equal(new DateOnly(2026, 4, 15), listed.MaturityDate);
        Assert.Equal(DepositStatus.Due, listed.Status);
    }

    [Fact]
    public async Task List_DayBeforeMaturityInWarsaw_IsActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-14 23:30 (CEST).
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 21, 30, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(DepositStatus.Active, listed.Status);
    }

    [Fact]
    public async Task List_DepositAndOtherAssets_ListsOnlyDeposits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.AddCashAssetAsync(cancellationToken);
        await client.AddAssetAsync(accountId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(deposit.AssetId, listed.AssetId);
    }

    [Fact]
    public async Task List_ArchivedDeposit_ListedWithFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var live = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Live deposit"));
        var archived = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Archived deposit"));
        await client.ArchiveAssetAsync(archived.AssetId, cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!;
        Assert.Equal(2, listed.Count);
        Assert.False(listed.Single(d => d.AssetId == live.AssetId).IsArchived);
        Assert.True(listed.Single(d => d.AssetId == archived.AssetId).IsArchived);
    }

    [Fact]
    public async Task List_NoDeposits_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCashAssetAsync(cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(AllDepositsUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsSourceAndReturnAccounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant);
        var toNest = await client.AddDepositAsync(
            cancellationToken,
            NewDepositRequest(name: "To Nest", sourceAccountId: mBank.AccountId, returnAccountId: nest.AccountId));
        var toSource = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(name: "To source", sourceAccountId: mBank.AccountId));

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        var listedToNest = Assert.Single(deposits, d => d.AssetId == toNest.AssetId);
        Assert.Equal(mBank.AccountId, listedToNest.SourceAccountId);
        Assert.Equal("mBank", listedToNest.SourceAccountName);
        Assert.Equal(nest.AccountId, listedToNest.ReturnAccountId);
        Assert.Equal("Nest", listedToNest.ReturnAccountName);
        var listedToSource = Assert.Single(deposits, d => d.AssetId == toSource.AssetId);
        Assert.Equal(mBank.AccountId, listedToSource.ReturnAccountId);
        Assert.Equal("mBank", listedToSource.ReturnAccountName);

        var single = await client.GetDepositAsync(toNest.AssetId, cancellationToken);
        Assert.Equal(nest.AccountId, single.ReturnAccountId);
        Assert.Equal("Nest", single.ReturnAccountName);
    }

    [Fact]
    public async Task List_ByAccountId_ReturnsDepositsItFundsOrReceives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant);
        var other = await client.CreateBankAccountAsync(cancellationToken, "Other", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var funded = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(name: "From mBank", sourceAccountId: mBank.AccountId));
        var received = await client.AddDepositAsync(
            cancellationToken,
            NewDepositRequest(name: "Into mBank", sourceAccountId: other.AccountId, returnAccountId: mBank.AccountId));
        await client.AddDepositAsync(
            cancellationToken,
            NewDepositRequest(name: "Unrelated", sourceAccountId: other.AccountId, returnAccountId: nest.AccountId));

        var response = await client.GetAsync(DepositsOfAccountUri(mBank.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        Assert.Equal(
            new[] { funded.AssetId, received.AssetId }.Order(),
            deposits.Select(d => d.AssetId).Order());
    }

    [Fact]
    public async Task List_PaidOutDeposit_ReturnsPayoutAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var paidOut = await client.CreateDepositPaidIntoBankAsync(cancellationToken);
        var settledOnly = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Settled deposit"));
        await client.SettleDepositAsync(settledOnly.AssetId, cancellationToken);

        var response = await client.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deposits = await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken);
        Assert.NotNull(deposits);
        Assert.Equal(2, deposits.Count);
        var listed = Assert.Single(deposits, d => d.AssetId == paidOut.Deposit.AssetId);
        Assert.Equal(DepositStatus.PaidOut, listed.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), listed.PaidOutOn);
        Assert.Equal("Savings account", listed.PaidOutToAccountName);
        var settled = Assert.Single(deposits, d => d.AssetId == settledOnly.AssetId);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Null(settled.PaidOutOn);
        Assert.Null(settled.PaidOutToAccountName);

        var single = await client.GetDepositAsync(paidOut.Deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, single.Status);
        Assert.Equal("Savings account", single.PaidOutToAccountName);
    }
}
