using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GetMonth_AsUserB_SeesOnlyOwnGroupsValuesAndMinimum()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = Factory.CreateAuthenticatedClient(userA);
        using var clientB = Factory.CreateAuthenticatedClient(userB);
        await clientA.CreateTransferPlanSetupAsync(cancellationToken);
        await clientB.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userA, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await SeedReplicasAsync(
            userB,
            cancellationToken,
            [new TargetAllocationReplicaGroup("Wszystko", [AssetClass.Stock, AssetClass.Etf, AssetClass.Bond, AssetClass.Deposit, AssetClass.PreciousMetal], 100m, 5m)],
            [new ValuationReplicaClassValue(AssetClass.Stock, 50_000m)],
            new DateOnly(2026, 10, 10));
        await clientA.SavePlanningSettingsAsync(cancellationToken, minimumTransferPln: 250);

        var planA = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);
        var planB = await clientB.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Equal(["Akcje/ETF", "Obligacje", "Kruszce"], planA.Groups.Select(g => g.Name));
        Assert.Equal(250, planA.MinimumTransferPln);
        Assert.Equal(TransferValuationDate, planA.ValuationDate);
        Assert.Equal(["Wszystko"], planB.Groups.Select(g => g.Name));
        Assert.Equal(100, planB.MinimumTransferPln);
        Assert.Equal(new DateOnly(2026, 10, 10), planB.ValuationDate);
        Assert.Equal(100m, planB.Groups.Single().CurrentPercent);
    }

    [Fact]
    public async Task TopUps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = Factory.CreateAuthenticatedClient(userA);
        using var clientB = Factory.CreateAuthenticatedClient(userB);
        var setupA = await clientA.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userA, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await SeedReplicasAsync(userB, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await clientA.AcceptTransferPlanAsync("2026-10", cancellationToken);
        await clientA.SettleBenefitAsync(setupA, 1_000m, cancellationToken);
        var before = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);

        var topUp = await clientB.PostAsync(TransferPlanTopUpAcceptUri("2026-10"), content: null, cancellationToken);

        await topUp.AssertProblemAsync(HttpStatusCode.Conflict, TransferPlanNoTopUpErrorCode, cancellationToken);
        var after = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);
        Assert.Equal(before.Rows.Select(r => (r.Id, r.BatchNumber, r.Checked)), after.Rows.Select(r => (r.Id, r.BatchNumber, r.Checked)));
        Assert.Equal(200m, after.TopUp!.Rows.Sum(r => r.AmountPln));
    }

    [Fact]
    public async Task AcceptedPlans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = Factory.CreateAuthenticatedClient(userA);
        using var clientB = Factory.CreateAuthenticatedClient(userB);
        await clientA.CreateTransferPlanSetupAsync(cancellationToken);
        await clientB.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userA, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await SeedReplicasAsync(userB, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await clientA.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var rowsA = (await clientA.GetTransferPlanAsync("2026-10", cancellationToken)).Rows;
        await clientA.CheckTransferPlanRowAsync(rowsA[0].Id!.Value, true, cancellationToken);
        var before = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);

        var undo = await clientB.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        var unskip = await clientB.DeleteAsync(TransferPlanActionUri("2026-10", "skip"), cancellationToken);
        var check = await clientB.PutAsJsonAsync(TransferPlanRowCheckedUri(rowsA[1].Id!.Value), new { @checked = true }, cancellationToken);
        var planBBefore = await clientB.GetTransferPlanAsync("2026-10", cancellationToken);
        var accept = await clientB.PostAsync(TransferPlanActionUri("2026-10", "accept"), content: null, cancellationToken);
        var planB = await clientB.GetTransferPlanAsync("2026-10", cancellationToken);
        var undoOwn = await clientB.PostAsync(TransferPlanActionUri("2026-10", "undo"), content: null, cancellationToken);
        var skipOwn = await clientB.PostAsync(TransferPlanActionUri("2026-10", "skip"), content: null, cancellationToken);

        await undo.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanNotFoundErrorCode, cancellationToken);
        await unskip.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanNotFoundErrorCode, cancellationToken);
        await check.AssertProblemAsync(HttpStatusCode.NotFound, TransferPlanRowNotFoundErrorCode, cancellationToken);
        Assert.True(planBBefore.Status.Is(TransferPlanStatus.Preview));
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        Assert.True(planB.Status.Is(TransferPlanStatus.Accepted));
        Assert.DoesNotContain(planB.Rows, r => rowsA.Any(a => a.Id == r.Id));
        Assert.Equal(HttpStatusCode.NoContent, undoOwn.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, skipOwn.StatusCode);

        var after = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);
        Assert.True(after.Status.Is(TransferPlanStatus.Accepted));
        Assert.Equal(before.Rows.Select(r => (r.Id, r.Checked)), after.Rows.Select(r => (r.Id, r.Checked)));
    }
}
