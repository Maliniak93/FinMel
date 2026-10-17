using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The endpoint under test is called directly; the Api helpers only arrange state.
public sealed class TransferPlanAcceptanceTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Accept_FreezesRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (ike, _) = await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var preview = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var accepted = await client.PostAsync(TransferPlanActionUri("2026-10", "accept"), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var renamed = await client.PutAsJsonAsync(AccountUri(ike), new { name = "IKE" }, cancellationToken);
        renamed.EnsureSuccessStatusCode();
        await SeedReplicasAsync(
            userId,
            cancellationToken,
            [
                new("Akcje", [AssetClass.Stock, AssetClass.Etf], 70m, 5m),
                new("Metale", [AssetClass.PreciousMetal], 30m, 5m)
            ],
            TransferValues,
            new DateOnly(2026, 10, 16));
        await client.SaveTransferPlanSettingsAsync(2_000, cancellationToken);

        var plan = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(plan.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal(TransferValuationDate, plan.ValuationDate);
        Assert.Equal(
            preview.Rows.Select(r => (r.BucketName, r.AccountName, r.GroupName, r.AmountPln)),
            plan.Rows.Select(r => (r.BucketName, r.AccountName, r.GroupName, r.AmountPln)));
        Assert.Equal(
            [
                ("IKE", "IKE mBank", "Akcje/ETF", 2_260m),
                ("IKZE", "IKZE Obligacje", "Obligacje", 2_304m),
                ("Inwestycje", null, "Obligacje", 1_300.27m),
                ("Inwestycje", null, "Kruszce", 679m)
            ],
            plan.Rows.Select(r => (r.BucketName, r.AccountName, r.GroupName, r.AmountPln)));
        Assert.All(plan.Rows, r =>
        {
            Assert.NotNull(r.Id);
            Assert.Equal(1, r.BatchNumber);
            Assert.False(r.Checked);
        });
    }

    [Fact]
    public async Task Accept_Rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await SetUpTransferPlanFromAsync(client, userId, "2026-09", cancellationToken);
        await client.SkipTransferPlanAsync("2026-09", cancellationToken);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var rowsBefore = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Select(r => r.Id).ToList();

        var future = await client.PostAsync(TransferPlanActionUri("2026-11", "accept"), content: null, cancellationToken);
        var none = await client.PostAsync(TransferPlanActionUri("2026-08", "accept"), content: null, cancellationToken);
        var accepted = await client.PostAsync(TransferPlanActionUri("2026-10", "accept"), content: null, cancellationToken);
        var skipped = await client.PostAsync(TransferPlanActionUri("2026-09", "accept"), content: null, cancellationToken);
        var badMonth = await client.PostAsync(TransferPlanActionUri("2026-13", "accept"), content: null, cancellationToken);

        await future.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
        await none.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
        await accepted.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
        await skipped.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
        await badMonth.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidMonthErrorCode, cancellationToken);
        Assert.True((await client.GetTransferPlanAsync("2026-11", cancellationToken)).Status.Is(TransferPlanStatus.None));
        Assert.True((await client.GetTransferPlanAsync("2026-08", cancellationToken)).Status.Is(TransferPlanStatus.None));
        Assert.True((await client.GetTransferPlanAsync("2026-09", cancellationToken)).Status.Is(TransferPlanStatus.Skipped));
        Assert.Equal(rowsBefore, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task Check_CompletesAndReopens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var rows = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows;
        Assert.Equal(4, rows.Count);

        var first = await client.PutAsJsonAsync(TransferPlanRowCheckedUri(rows[0].Id!.Value), new { @checked = true }, cancellationToken);
        foreach (var row in rows.Skip(1))
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        var completed = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        var strangers = await stranger.PutAsJsonAsync(TransferPlanRowCheckedUri(rows[0].Id!.Value), new { @checked = false }, cancellationToken);
        var unknown = await client.PutAsJsonAsync(TransferPlanRowCheckedUri(Guid.NewGuid()), new { @checked = true }, cancellationToken);
        await client.CheckTransferPlanRowAsync(rows[2].Id!.Value, false, cancellationToken);
        var reopened = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.True(completed.Status.Is(TransferPlanStatus.Completed));
        Assert.All(completed.Rows, r => Assert.True(r.Checked));
        await strangers.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanRowNotFoundErrorCode, cancellationToken);
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanRowNotFoundErrorCode, cancellationToken);
        Assert.True(reopened.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal([true, true, false, true], reopened.Rows.Select(r => r.Checked == true));
    }

    [Fact]
    public async Task Undo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var nothingAccepted = await client.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var acceptedPlan = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var undone = await client.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        var previewAgain = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var row = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows[1];
        await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        var blocked = await client.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        var stillAccepted = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        await nothingAccepted.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanNotFoundErrorCode, cancellationToken);
        Assert.True(acceptedPlan.CanUndo);
        Assert.Equal(HttpStatusCode.NoContent, undone.StatusCode);
        Assert.True(previewAgain.Status.Is(TransferPlanStatus.Preview));
        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanRowCheckedErrorCode, cancellationToken);
        Assert.True(stillAccepted.Status.Is(TransferPlanStatus.Accepted));
        Assert.False(stillAccepted.CanUndo);
    }

    [Fact]
    public async Task SkipAndUnskip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var notSkipped = await client.DeleteAsync(TransferPlanActionUri("2026-10", "skip"), cancellationToken);

        var skipped = await client.PostAsync(TransferPlanActionUri("2026-10", "skip"), content: null, cancellationToken);
        var skippedPlan = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        var acceptSkipped = await client.PostAsync(TransferPlanActionUri("2026-10", "accept"), content: null, cancellationToken);
        var unskipped = await client.DeleteAsync(TransferPlanActionUri("2026-10", "skip"), cancellationToken);
        var previewAgain = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var skipAccepted = await client.PostAsync(TransferPlanActionUri("2026-10", "skip"), content: null, cancellationToken);

        await notSkipped.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanNotFoundErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, skipped.StatusCode);
        Assert.True(skippedPlan.Status.Is(TransferPlanStatus.Skipped));
        Assert.Empty(skippedPlan.Rows);
        await acceptSkipped.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, unskipped.StatusCode);
        Assert.True(previewAgain.Status.Is(TransferPlanStatus.Preview));
        Assert.Equal(4, previewAgain.Rows.Count);
        await skipAccepted.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNotPreviewErrorCode, cancellationToken);
    }
}
