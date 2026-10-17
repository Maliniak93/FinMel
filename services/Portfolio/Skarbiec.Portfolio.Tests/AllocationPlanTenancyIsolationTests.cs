using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AllocationPlanTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Stranger_CannotUseDeleteOrReadOwnersPlan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var account = await owner.CreateAllocationSetupAsync(cancellationToken);
        await owner.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan(account.AccountId));
        var plan = await owner.GetAllocationAsync("2026-10", cancellationToken);
        var akcjeId = plan.Buckets.Single(b => b.Name == "Akcje").BucketId;
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var withBucket = await stranger.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m, bucketId: akcjeId),
                AllocationRow("ETF", InvestmentsKind, PercentShare, 50m, isRemainder: true)),
            cancellationToken);
        var withAccount = await stranger.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, accountId: account.AccountId),
                AllocationRow("ETF", InvestmentsKind, PercentShare, 100m, isRemainder: true)),
            cancellationToken);
        var deleted = await stranger.DeleteAsync(AllocationPlanVersionUri("2026-10"), cancellationToken);

        await withBucket.AssertProblemAsync(HttpStatusCode.NotFound, AllocationBucketNotFoundErrorCode, cancellationToken);
        await withAccount.AssertProblemAsync(HttpStatusCode.NotFound, "NotFound.Account", cancellationToken);
        await deleted.AssertProblemAsync(HttpStatusCode.NotFound, AllocationPlanVersionNotFoundErrorCode, cancellationToken);
        var strangerView = await stranger.GetAllocationAsync("2026-10", cancellationToken);
        Assert.Empty(strangerView.Buckets);
        Assert.Null(strangerView.EffectiveFrom);
        var after = await owner.GetAllocationAsync("2026-10", cancellationToken);
        Assert.Equal(plan.Buckets.Select(b => (b.BucketId, b.Name, b.AllocatedPln)), after.Buckets.Select(b => (b.BucketId, b.Name, b.AllocatedPln)));
        Assert.Equal(1, await CountAllocationVersionsAsync(ownerId, cancellationToken));
        Assert.Equal(4, await CountAllocationBucketsAsync(ownerId, cancellationToken));
    }

    [Fact]
    public async Task Stranger_CannotChooseOwnersAccountAsReserve()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var account = await owner.CreateAllocationSetupAsync(cancellationToken);
        await owner.CreateOcToyotaAsync(cancellationToken);
        await owner.SaveAllocationPlanWithReserveAsync("2026-10", account.AccountId, cancellationToken);
        var ownersReserve = (await owner.GetAllocationAsync("2026-10", cancellationToken)).Reserve;
        Assert.Equal(account.AccountId, ownersReserve?.AccountId);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var chosen = await stranger.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            new { buckets = Array.Empty<object>(), reserveAccountId = account.AccountId },
            cancellationToken);

        await chosen.AssertProblemAsync(HttpStatusCode.NotFound, "NotFound.Account", cancellationToken);
        Assert.Null((await stranger.GetAllocationAsync("2026-10", cancellationToken)).Reserve);
        Assert.Equal(1, await CountAllocationVersionsAsync(ownerId, cancellationToken));
    }

    [Fact]
    public async Task SetAndDeleteBucketBalance_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        await owner.CreateCushionSetupAsync(cancellationToken);
        await owner.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var plan = await owner.GetAllocationAsync("2026-10", cancellationToken);
        var cushionId = plan.Buckets.Single(b => b.Name == "Poduszka").BucketId;
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var set = await stranger.PutAsJsonAsync(
            BucketBalanceUri(cushionId, "2026-12"), new { amount = 1_000m }, cancellationToken);
        var deleted = await stranger.DeleteAsync(BucketBalanceUri(cushionId, "2026-10"), cancellationToken);

        await set.AssertProblemAsync(HttpStatusCode.NotFound, AllocationBucketNotFoundErrorCode, cancellationToken);
        await deleted.AssertProblemAsync(HttpStatusCode.NotFound, AllocationBucketNotFoundErrorCode, cancellationToken);
        var before = plan.Buckets.Select(b => (b.BucketId, b.Name, b.AllocatedPln));
        var after = (await owner.GetAllocationAsync("2026-10", cancellationToken)).Buckets
            .Select(b => (b.BucketId, b.Name, b.AllocatedPln));
        Assert.Equal(before, after);
        Assert.Equal(1, await CountAllocationBalanceOverridesAsync(ownerId, cancellationToken));
    }
}
