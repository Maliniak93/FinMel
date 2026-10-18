using System.Net;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The top-up accept endpoint is called directly where it is under test; the Api helpers only arrange state.
public sealed class TransferPlanTopUpTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Increase_ShowsTopUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var unchanged = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        await client.SettleBenefitAsync(setup, 1_000m, cancellationToken);
        var withMinimum = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.SavePlanningSettingsAsync(cancellationToken, minimumTransferPln: 0);
        var withoutMinimum = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Empty(unchanged.Differences!);
        Assert.Null(unchanged.TopUp);
        var difference = Assert.Single(withMinimum.Differences!);
        Assert.Equal(("Inwestycje", 1_979.27m, 2_179.27m, 200m), (difference.BucketName, difference.AcceptedPln, difference.CurrentPln, difference.DifferencePln));
        Assert.True(withMinimum.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal(
            [("Inwestycje", null, "Obligacje", 200m)],
            withMinimum.TopUp!.Rows.Select(r => (r.BucketName, r.AccountName, r.GroupName, r.AmountPln)));
        Assert.Equal(
            [("Inwestycje", "Obligacje", 132m), ("Inwestycje", "Kruszce", 68m)],
            withoutMinimum.TopUp!.Rows.Select(r => (r.BucketName, r.GroupName, r.AmountPln)));
        Assert.Equal(4, withMinimum.Rows.Count);
    }

    [Fact]
    public async Task AcceptTopUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        await client.SettleBenefitAsync(setup, 1_000m, cancellationToken);
        foreach (var row in (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows)
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        var completedBefore = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        var accepted = await client.PostAsync(TransferPlanTopUpAcceptUri("2026-10"), content: null, cancellationToken);
        var afterAccept = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.SettleBenefitAsync(setup, 1_050m, cancellationToken);
        var afterRise = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(completedBefore.Status.Is(TransferPlanStatus.Completed));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.True(afterAccept.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal(5, afterAccept.Rows.Count);
        var batchTwo = Assert.Single(afterAccept.Rows, r => r.BatchNumber == 2);
        Assert.Equal(("Inwestycje", "Obligacje", 200m, false), (batchTwo.BucketName, batchTwo.GroupName, batchTwo.AmountPln, batchTwo.Checked));
        Assert.All(afterAccept.Rows.Where(r => r.BatchNumber == 1), r => Assert.True(r.Checked));
        Assert.Null(afterAccept.TopUp);
        Assert.Empty(afterAccept.Differences!);
        Assert.Equal(50m, Assert.Single(afterRise.Differences!).DifferencePln);
        Assert.Equal(50m, afterRise.TopUp!.Rows.Sum(r => r.AmountPln));
    }

    [Fact]
    public async Task DecreaseAndSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.SettleBenefitAsync(setup, 1_000m, cancellationToken);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var rowsBefore = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Select(r => r.Id).ToList();

        await client.UndoBenefitSettlementAsync(setup, cancellationToken);
        var decreased = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        var rejected = await client.PostAsync(TransferPlanTopUpAcceptUri("2026-10"), content: null, cancellationToken);

        var difference = Assert.Single(decreased.Differences!);
        Assert.Equal(("Inwestycje", 2_179.27m, 1_979.27m, -200m), (difference.BucketName, difference.AcceptedPln, difference.CurrentPln, difference.DifferencePln));
        Assert.Null(decreased.TopUp);
        await rejected.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNoTopUpErrorCode, cancellationToken);
        Assert.Equal(rowsBefore, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Select(r => r.Id));

        var skippedUser = Guid.NewGuid();
        using var skippedClient = Factory.CreateAuthenticatedClient(skippedUser);
        var skippedSetup = await skippedClient.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(skippedUser, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await skippedClient.SkipTransferPlanAsync("2026-10", cancellationToken);
        await skippedClient.SettleBenefitAsync(skippedSetup, 1_000m, cancellationToken);

        var skipped = await skippedClient.GetTransferPlanAsync("2026-10", cancellationToken);
        var skippedAccept = await skippedClient.PostAsync(TransferPlanTopUpAcceptUri("2026-10"), content: null, cancellationToken);

        Assert.True(skipped.Status.Is(TransferPlanStatus.Skipped));
        Assert.Empty(skipped.Differences!);
        Assert.Null(skipped.TopUp);
        await skippedAccept.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNoTopUpErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Undo_RemovesLatestBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var planRows = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows;
        await client.CheckTransferPlanRowAsync(planRows[0].Id!.Value, true, cancellationToken);
        await client.SettleBenefitAsync(setup, 1_000m, cancellationToken);
        await client.AcceptTransferPlanTopUpAsync("2026-10", cancellationToken);
        var withTopUp = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var undone = await client.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        var afterUndo = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.AcceptTransferPlanTopUpAsync("2026-10", cancellationToken);
        var topUpRow = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Single(r => r.BatchNumber == 2);
        await client.CheckTransferPlanRowAsync(topUpRow.Id!.Value, true, cancellationToken);
        var blocked = await client.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);

        Assert.True(withTopUp.CanUndo);
        Assert.Equal(HttpStatusCode.NoContent, undone.StatusCode);
        Assert.Equal(planRows.Select(r => r.Id), afterUndo.Rows.Select(r => r.Id));
        Assert.True(afterUndo.Rows[0].Checked);
        Assert.True(afterUndo.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal(200m, afterUndo.TopUp!.Rows.Sum(r => r.AmountPln));
        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanRowCheckedErrorCode, cancellationToken);
        Assert.Equal(2, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Max(r => r.BatchNumber));
    }
}
