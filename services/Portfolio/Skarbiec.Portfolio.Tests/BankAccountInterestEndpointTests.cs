using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

namespace Skarbiec.Portfolio.Tests;

// Preview, settle and undo are under test, so they are called directly; BankAccountsApi helpers only arrange.
public sealed class BankAccountInterestEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_AugustOfSavingsVariant_ReturnsGrossBelkaTaxAndNet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await client.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m, balance: 10_000m, depositedOn: AugustOpening);

        var response = await client.GetAsync(InterestPreviewUri(nest.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        Assert.Equal("2026-08-01", preview.GetProperty("periodStart").GetString());
        Assert.Equal("2026-08-31", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(42.47m, preview.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(8.07m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(34.40m, preview.GetProperty("netInterest").GetDecimal());
    }

    [Fact]
    public async Task Settle_August_CreditsNetAndReportsInterestCreditedNet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await client.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m, balance: 10_000m, depositedOn: AugustOpening);

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(nest.AccountId), NewSettleInterestBody(AugustEnd, 42.47m, 8.07m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await client.GetAccountJsonAsync(nest.AccountId, cancellationToken);
        Assert.Equal(10_034.40m, account.GetProperty("cashBalance").GetDecimal());
        Assert.Equal(34.40m, account.Bank().GetProperty("interestCreditedNet").GetDecimal());
        Assert.Equal("2026-08-31", account.Bank().GetProperty("lastSettlement").GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Undo_LastSettlement_RestoresBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await client.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m, balance: 10_000m, depositedOn: AugustOpening);
        await client.SettlePreviewedInterestAsync(nest.AccountId, cancellationToken);
        var settlementId = (await client.GetAccountJsonAsync(nest.AccountId, cancellationToken))
            .Bank().GetProperty("lastSettlement").GetProperty("settlementId").GetGuid();

        var response = await client.DeleteAsync(InterestSettlementUri(nest.AccountId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await client.GetAccountJsonAsync(nest.AccountId, cancellationToken);
        Assert.Equal(10_000m, account.GetProperty("cashBalance").GetDecimal());
        Assert.Equal(0m, account.Bank().GetProperty("interestCreditedNet").GetDecimal());
    }

    [Fact]
    public async Task Preview_AfterRateChange_UsesNewRateForEveryDayOfNextPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await client.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m, balance: 10_000m, depositedOn: AugustOpening);
        await client.SettlePreviewedInterestAsync(nest.AccountId, cancellationToken);
        var update = await client.PutAsJsonAsync(
            AccountUri(nest.AccountId),
            NewUpdateBankBody("Nest Oszczędnościowe", SavingsVariant, "Nest Bank", annualInterestRatePercent: 4m),
            cancellationToken);
        update.EnsureSuccessStatusCode();

        var preview = await client.GetInterestPreviewAsync(nest.AccountId, cancellationToken);

        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(4m, preview.GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.Equal(32.99m, preview.GetProperty("grossInterest").GetDecimal());
    }

    [Fact]
    public async Task NonBank_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var brokerageId = await client.CreateAccountAsync(cancellationToken);

        var response = await client.GetAsync(InterestPreviewUri(brokerageId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InterestNotSupportedErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Preview_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Old", SavingsVariant, annualInterestRatePercent: 5m);
        await client.ArchiveAccountAsync(nest.AccountId, cancellationToken);

        var response = await client.GetAsync(InterestPreviewUri(nest.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "Conflict.AccountArchived", cancellationToken);
    }
}
