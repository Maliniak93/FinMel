using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioAssertions;

namespace Skarbiec.Portfolio.Tests;

// The balance PUT and DELETE are posted directly; the SetBucketBalanceAsync and DeleteBucketBalanceAsync helpers only arrange.
public sealed class BucketBalanceEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static AllocationBucketView Bucket(AllocationView allocation, string name) =>
        allocation.Buckets.Single(b => b.Name == name);

    [Fact]
    public async Task CushionAdjustment_RefillsFromMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var cushionId = Bucket(await client.GetAllocationAsync("2026-10", cancellationToken), "Poduszka").BucketId;

        var set = await client.PutAsJsonAsync(BucketBalanceUri(cushionId, "2026-12"), new { amount = 20_000m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        var november = await client.GetAllocationAsync("2026-11", cancellationToken);
        var december = await client.GetAllocationAsync("2026-12", cancellationToken);
        var january = await client.GetAllocationAsync("2027-01", cancellationToken);
        Assert.Equal(0m, Bucket(november, "Poduszka").AllocatedPln);
        Assert.Equal(11_743.27m, Bucket(november, "Inwestycje").AllocatedPln);
        Assert.Equal(5_200m, Bucket(december, "Poduszka").AllocatedPln);
        Assert.Equal(6_543.27m, Bucket(december, "Inwestycje").AllocatedPln);
        Assert.Equal(25_200m, Bucket(january, "Poduszka").Cushion!.BalanceStartPln);
    }

    [Fact]
    public async Task CounterAdjustment_ReopensRoom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ike, ikze) = await client.CreateTaxLimitSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, TaxLimitPlan(ike, ikze));
        var ikeId = Bucket(await client.GetAllocationAsync("2026-10", cancellationToken), "IKE").BucketId;

        var set = await client.PutAsJsonAsync(BucketBalanceUri(ikeId, "2026-11"), new { amount = 20_000m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        var october = await client.GetAllocationAsync("2026-10", cancellationToken);
        var november = await client.GetAllocationAsync("2026-11", cancellationToken);
        Assert.Equal(2_260m, Bucket(october, "IKE").AllocatedPln);
        Assert.Equal(5_871m, Bucket(november, "IKE").AllocatedPln);
        Assert.Equal(20_000m, Bucket(november, "IKE").Limit!.ContributedStartPln);
        Assert.Equal(25_871m, Bucket(november, "IKE").Limit!.ContributedEndPln);
        Assert.Equal(0m, Bucket(november, "IKZE").AllocatedPln);
        Assert.Equal(5_872.27m, Bucket(november, "Inwestycje").AllocatedPln);
    }

    [Fact]
    public async Task SetReplaces_DeleteFallsBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (ike, ikze) = await client.CreateTaxLimitSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, TaxLimitPlan(ike, ikze));
        var ikeId = Bucket(await client.GetAllocationAsync("2026-10", cancellationToken), "IKE").BucketId;
        await client.SetBucketBalanceAsync(ikeId, "2026-11", 20_000m, cancellationToken);

        var replaced = await client.PutAsJsonAsync(BucketBalanceUri(ikeId, "2026-11"), new { amount = 28_260m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        Assert.Equal(0m, Bucket(await client.GetAllocationAsync("2026-11", cancellationToken), "IKE").AllocatedPln);
        await using (var dbContext = CreateDbContext(userId))
        {
            Assert.Equal(
                1,
                await dbContext.AllocationBalanceOverrides.CountAsync(
                    o => o.BucketId == ikeId && o.Month == new DateOnly(2026, 11, 1), cancellationToken));
        }

        var deleted = await client.DeleteAsync(BucketBalanceUri(ikeId, "2026-11"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterDelete = Bucket(await client.GetAllocationAsync("2026-11", cancellationToken), "IKE");
        Assert.Equal(0m, afterDelete.AllocatedPln);
        Assert.Equal(28_260m, afterDelete.Limit!.ContributedStartPln);

        var startingValueDeleted = await client.DeleteAsync(BucketBalanceUri(ikeId, "2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, startingValueDeleted.StatusCode);
        Assert.Equal(3_271m, Bucket(await client.GetAllocationAsync("2026-10", cancellationToken), "IKE").AllocatedPln);

        var missing = await client.DeleteAsync(BucketBalanceUri(ikeId, "2026-11"), cancellationToken);

        await missing.AssertProblemAsync(HttpStatusCode.NotFound, BalanceAdjustmentNotFoundErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Validation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var plan = await client.GetAllocationAsync("2026-10", cancellationToken);
        var cushionId = Bucket(plan, "Poduszka").BucketId;
        var savingsId = Bucket(plan, "Wakacje").BucketId;
        var investmentsId = Bucket(plan, "Inwestycje").BucketId;

        var onInvestments = await client.PutAsJsonAsync(
            BucketBalanceUri(investmentsId, "2026-12"), new { amount = 1_000m }, cancellationToken);
        var onSavings = await client.PutAsJsonAsync(
            BucketBalanceUri(savingsId, "2026-12"), new { amount = 1_000m }, cancellationToken);
        var negativeAmount = await client.PutAsJsonAsync(
            BucketBalanceUri(cushionId, "2026-12"), new { amount = -1m }, cancellationToken);
        var monthBeforeRange = await client.PutAsJsonAsync(
            BucketBalanceUri(cushionId, "2010-12"), new { amount = 1_000m }, cancellationToken);
        var unknownBucket = await client.PutAsJsonAsync(
            BucketBalanceUri(Guid.NewGuid(), "2026-12"), new { amount = 1_000m }, cancellationToken);

        await onInvestments.AssertProblemAsync(HttpStatusCode.BadRequest, BucketHasNoBalanceErrorCode, cancellationToken);
        await onSavings.AssertProblemAsync(HttpStatusCode.BadRequest, BucketHasNoBalanceErrorCode, cancellationToken);
        await negativeAmount.AssertFieldValidationErrorAsync("amount", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, monthBeforeRange.StatusCode);
        await unknownBucket.AssertProblemAsync(HttpStatusCode.NotFound, AllocationBucketNotFoundErrorCode, cancellationToken);
    }

    [Fact]
    public async Task MonthView_ListsAdjustments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var plan = await client.GetAllocationAsync("2026-10", cancellationToken);
        var cushionId = Bucket(plan, "Poduszka").BucketId;
        await client.SetBucketBalanceAsync(cushionId, "2026-12", 20_000m, cancellationToken);

        foreach (var month in new[] { "2026-10", "2026-11", "2026-12" })
        {
            var allocation = await client.GetAllocationAsync(month, cancellationToken);

            var adjustments = Bucket(allocation, "Poduszka").Adjustments!.Select(a => (a.Month, a.AmountPln)).ToList();
            Assert.Equal(new[] { ("2026-10", 20_000m), ("2026-12", 20_000m) }, adjustments);
            var investmentsAdjustments = Bucket(allocation, "Inwestycje").Adjustments;
            Assert.True(investmentsAdjustments is null or { Count: 0 });
        }
    }
}
