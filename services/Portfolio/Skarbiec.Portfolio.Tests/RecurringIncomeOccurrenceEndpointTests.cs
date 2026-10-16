using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The settle, skip and undo slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class RecurringIncomeOccurrenceEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset EndOfOctoberUtc = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset MidNovemberUtc = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Settle_FreezesPlanAndShowsDifference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EndOfOctoberUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var income = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);

        var response = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(account.AccountId, 9_143.27m, new DateOnly(2026, 10, 30)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var occurrence = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.Equal("Settled", occurrence.Status);
        Assert.Equal(9_200m, occurrence.Amount);
        Assert.Equal(new DateOnly(2026, 10, 30), occurrence.Date);
        Assert.Equal(9_143.27m, occurrence.ActualAmount);
        Assert.Equal(new DateOnly(2026, 10, 30), occurrence.ActualDate);
        Assert.Equal(-56.73m, occurrence.Difference);
        Assert.Empty((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);
    }

    [Fact]
    public async Task Settle_BeforePlannedDate_StaysInPlanMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EndOfOctoberUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m, dayOfMonth: 2);

        var tomorrow = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(account.AccountId, 500m, new DateOnly(2026, 11, 1)),
            cancellationToken);
        var early = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(account.AccountId, 500m, new DateOnly(2026, 10, 31)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, tomorrow.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, early.StatusCode);
        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrence("2026-11");
        Assert.Equal("Settled", november.Status);
        Assert.Equal(new DateOnly(2026, 11, 2), november.Date);
        Assert.Equal(new DateOnly(2026, 10, 31), november.ActualDate);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        Assert.DoesNotContain(october.Occurrences, o => o.NominalMonth == "2026-11");
    }

    [Fact]
    public async Task Settle_ForeignCurrency_FreezesRateOfActualDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MidNovemberUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        var income = await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 10);
        var settleDate = new DateOnly(2026, 11, 10);
        Factory.FxRateLookupClient.WithRate("EUR", settleDate, 4.30m);

        Factory.FxRateLookupClient.WithNotFound("EUR");
        var missing = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(eur.AccountId, 980m, new DateOnly(2026, 11, 12)),
            cancellationToken);
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        var unavailable = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(eur.AccountId, 980m, new DateOnly(2026, 11, 12)),
            cancellationToken);

        await missing.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.FxRateMissingErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrence("2026-11").Status);

        await client.SettleOccurrenceAsync(income, "2026-11", eur.AccountId, 980m, settleDate, cancellationToken);

        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes;
        var settled = november.Occurrence("2026-11");
        Assert.Equal("Settled", settled.Status);
        Assert.Equal(4_300m, settled.AmountPln);
        Assert.Equal(4_214m, settled.ActualAmountPln);
        Assert.Equal(-20m, settled.Difference);
        Assert.False(settled.Estimated);
        var pending = (await client.GetPlanningMonthAsync("2026-12", cancellationToken)).Incomes.Occurrence("2026-12");
        Assert.Equal("Pending", pending.Status);
        Assert.Equal(4_250m, pending.AmountPln);
        Assert.True(pending.Estimated);
    }

    [Fact]
    public async Task EditAndUndo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var first = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var second = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var income = await client.CreateRecurringIncomeAsync(first.AccountId, cancellationToken, "Pensja", 1_000m, dayOfMonth: 10);
        await client.SettleOccurrenceAsync(income, "2026-10", first.AccountId, 990m, new DateOnly(2026, 10, 10), cancellationToken);

        var edit = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(second.AccountId, 950m, new DateOnly(2026, 10, 15)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var edited = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.Equal("Settled", edited.Status);
        Assert.Equal(950m, edited.ActualAmount);
        Assert.Equal(new DateOnly(2026, 10, 15), edited.ActualDate);
        Assert.Equal(second.AccountId, edited.AccountId);
        Assert.Equal(1_000m, edited.Amount);
        Assert.Equal(new DateOnly(2026, 10, 10), edited.Date);
        Assert.Equal(first.AccountId, edited.PlannedAccountId);
        Assert.Equal(-50m, edited.Difference);

        await client.UpdateRecurringIncomeAsync(income, first.AccountId, "2026-10", cancellationToken, "Pensja", 1_200m);
        var undo = await client.DeleteAsync(OccurrenceUri(income, "2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        var pending = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.Equal("Pending", pending.Status);
        Assert.Equal(1_200m, pending.Amount);
        Assert.Null(pending.ActualAmount);
        Assert.Null(pending.ActualDate);
        Assert.Null(pending.Difference);
        var again = await client.DeleteAsync(OccurrenceUri(income, "2026-10"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Skip_AndUndo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "800+", 800m, dayOfMonth: 10);

        var skip = await client.PutAsync(SkipOccurrenceUri(income, "2026-10"), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, skip.StatusCode);
        var skippedMonth = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        var skipped = skippedMonth.Occurrence("2026-10");
        Assert.Equal("Skipped", skipped.Status);
        Assert.Equal(-800m, skipped.Difference);
        Assert.Equal(800m, skippedMonth.TotalPln);

        var skipAgain = await client.PutAsync(SkipOccurrenceUri(income, "2026-10"), content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, skipAgain.StatusCode);

        await client.SettleOccurrenceAsync(income, "2026-10", account.AccountId, 800m, new DateOnly(2026, 10, 10), cancellationToken);
        Assert.Equal("Settled", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10").Status);

        var skipSettled = await client.PutAsync(SkipOccurrenceUri(income, "2026-10"), content: null, cancellationToken);
        await skipSettled.AssertProblemAsync(HttpStatusCode.Conflict, OccurrenceSettledErrorCode, cancellationToken);

        await client.UndoOccurrenceAsync(income, "2026-10", cancellationToken);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10").Status);
    }

    [Fact]
    public async Task StoredOccurrence_SurvivesDefinitionEdits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EndOfOctoberUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mbank = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var income = await client.CreateRecurringIncomeAsync(
            mbank.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);
        await client.SettleOccurrenceAsync(income, "2026-10", mbank.AccountId, 9_200m, new DateOnly(2026, 10, 30), cancellationToken);
        await client.SkipOccurrenceAsync(income, "2026-09", cancellationToken);

        await client.UpdateRecurringIncomeAsync(
            income, company.AccountId, "2026-09", cancellationToken, "Wypłata Dominik", 9_800m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);

        var september = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09");
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.All([september, october], o =>
        {
            Assert.Equal(9_200m, o.Amount);
            Assert.Equal(mbank.AccountId, o.PlannedAccountId);
            Assert.Equal("mBank ROR", o.AccountName);
        });
        Assert.Equal("Skipped", september.Status);
        Assert.Equal("Settled", october.Status);
        Assert.Equal(9_800m, (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrence("2026-11").Amount);

        await client.EndRecurringIncomeAsync(income, new DateOnly(2026, 8, 31), cancellationToken);

        var afterEndSeptember = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09");
        var afterEndOctober = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.True(afterEndSeptember.OutOfRule);
        Assert.True(afterEndOctober.OutOfRule);
        Assert.Equal(9_200m, afterEndOctober.Amount);
        Assert.Empty((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrences);
    }

    [Fact]
    public async Task StoredKey_SuppressesComputedOccurrence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Zwrot kosztów", 300m, new DateOnly(2026, 1, 1),
            dayOfMonth: 1, shift: BusinessDayShift.PreviousBusinessDay);
        await client.SettleOccurrenceAsync(income, "2026-08", account.AccountId, 300m, new DateOnly(2026, 7, 31), cancellationToken);

        await client.UpdateRecurringIncomeAsync(
            income, account.AccountId, "2026-07", cancellationToken, "Zwrot kosztów", 300m, new DateOnly(2026, 1, 1),
            dayOfMonth: 1, shift: BusinessDayShift.None);

        var july = (await client.GetPlanningMonthAsync("2026-07", cancellationToken)).Incomes;
        var august = (await client.GetPlanningMonthAsync("2026-08", cancellationToken)).Incomes;
        var stored = july.Occurrence("2026-08");
        Assert.Equal("Settled", stored.Status);
        Assert.Equal(new DateOnly(2026, 7, 31), stored.Date);
        Assert.DoesNotContain(august.Occurrences, o => o.NominalMonth == "2026-08");
    }

    [Fact]
    public async Task Settle_InvalidRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var pln = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(
            pln.AccountId, cancellationToken, "Pensja", 500m, new DateOnly(2026, 1, 1), dayOfMonth: 10);
        var date = new DateOnly(2026, 10, 10);

        var currency = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"), SettleOccurrenceBody(eur.AccountId, 500m, date), cancellationToken);
        var archivedAccount = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"), SettleOccurrenceBody(archived.AccountId, 500m, date), cancellationToken);
        var zero = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"), SettleOccurrenceBody(pln.AccountId, 0m, date), cancellationToken);
        var tooOld = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(pln.AccountId, 500m, new DateOnly(2010, 12, 31)),
            cancellationToken);
        var notGenerated = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2025-12"), SettleOccurrenceBody(pln.AccountId, 500m, date), cancellationToken);
        var badMonth = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-13"), SettleOccurrenceBody(pln.AccountId, 500m, date), cancellationToken);

        await currency.AssertProblemAsync(HttpStatusCode.BadRequest, CurrencyMismatchErrorCode, cancellationToken);
        await archivedAccount.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooOld.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notGenerated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.Equal("Pending", october.Status);
        Assert.Null(october.ActualAmount);
    }
}
