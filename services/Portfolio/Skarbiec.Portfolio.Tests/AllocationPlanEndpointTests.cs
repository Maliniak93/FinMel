using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AllocationPlanEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static object[] SecondVersion(AllocationView first, decimal wyjazdAmount)
    {
        Guid IdOf(string name) => first.Buckets.Single(b => b.Name == name).BucketId;

        return
        [
            AllocationRow("Wyjazd", SavingsKind, FixedShare, wyjazdAmount, bucketId: IdOf("Wakacje")),
            AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m, bucketId: IdOf("Akcje")),
            AllocationRow("Lokata", SavingsKind, PercentShare, 20m, bucketId: IdOf("Lokata")),
            AllocationRow("ETF", InvestmentsKind, PercentShare, 30m, isRemainder: true, bucketId: IdOf("ETF"))
        ];
    }

    [Fact]
    public async Task VersionsFromMonth_LaterUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());
        var first = await client.GetAllocationAsync("2026-10", cancellationToken);
        var wakacjeId = first.Buckets.Single(b => b.Name == "Wakacje").BucketId;

        var saved = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2027-01"), NewAllocationPlanBody(SecondVersion(first, 800m)), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var december = await client.GetAllocationAsync("2026-12", cancellationToken);
        var january = await client.GetAllocationAsync("2027-01", cancellationToken);
        var september = await client.GetAllocationAsync("2026-09", cancellationToken);
        Assert.Equal("Wakacje", december.Buckets[0].Name);
        Assert.Equal(500m, december.Buckets[0].AllocatedPln);
        Assert.Equal("Wyjazd", january.Buckets[0].Name);
        Assert.Equal(800m, january.Buckets[0].AllocatedPln);
        Assert.Equal(wakacjeId, january.Buckets[0].BucketId);
        Assert.Equal("2027-01", (await client.GetAllocationAsync("2026-10", cancellationToken)).NextChange);
        Assert.Equal("2026-10", december.EffectiveFrom);
        Assert.Equal("2027-01", january.EffectiveFrom);
        Assert.Empty(september.Buckets);
        Assert.Null(september.EffectiveFrom);
        Assert.Equal("2026-10", september.NextChange);

        await client.SaveAllocationPlanAsync("2027-01", cancellationToken, SecondVersion(first, 900m));

        Assert.Equal(900m, (await client.GetAllocationAsync("2027-01", cancellationToken)).Buckets[0].AllocatedPln);
        Assert.Equal(500m, (await client.GetAllocationAsync("2026-12", cancellationToken)).Buckets[0].AllocatedPln);
        Assert.Equal(2, await CountAllocationVersionsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task DeleteVersion_FallsBackAndRemovesOrphans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());
        var first = await client.GetAllocationAsync("2026-10", cancellationToken);
        await client.SaveAllocationPlanAsync("2027-01", cancellationToken, SecondVersion(first, 800m));

        var deletedLater = await client.DeleteAsync(AllocationPlanVersionUri("2027-01"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletedLater.StatusCode);
        var january = await client.GetAllocationAsync("2027-01", cancellationToken);
        Assert.Equal("Wakacje", january.Buckets[0].Name);
        Assert.Equal(500m, january.Buckets[0].AllocatedPln);
        Assert.Null((await client.GetAllocationAsync("2026-10", cancellationToken)).NextChange);
        Assert.Equal(4, await CountAllocationBucketsAsync(userId, cancellationToken));

        var deletedFirst = await client.DeleteAsync(AllocationPlanVersionUri("2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletedFirst.StatusCode);
        Assert.Empty((await client.GetAllocationAsync("2026-10", cancellationToken)).Buckets);
        Assert.Empty((await client.GetAllocationAsync("2027-01", cancellationToken)).Buckets);
        Assert.Equal(0, await CountAllocationBucketsAsync(userId, cancellationToken));
        Assert.Equal(0, await CountAllocationVersionsAsync(userId, cancellationToken));

        var missing = await client.DeleteAsync(AllocationPlanVersionUri("2026-10"), cancellationToken);

        await missing.AssertProblemAsync(HttpStatusCode.NotFound, AllocationPlanVersionNotFoundErrorCode, cancellationToken);
    }

    [Fact]
    public async Task InvalidPlans_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());
        var before = await client.GetAllocationAsync("2026-10", cancellationToken);
        var akcjeId = before.Buckets.Single(b => b.Name == "Akcje").BucketId;
        var remainder = AllocationRow("ETF", InvestmentsKind, PercentShare, 0m, isRemainder: true);

        var invalid = new (string Case, string Month, object[] Buckets)[]
        {
            ("month before 2011-01", "2010-12", [remainder]),
            ("empty name", "2026-10", [AllocationRow("", SavingsKind, FixedShare, 100m), remainder]),
            (
                "duplicate names ignoring case",
                "2026-10",
                [AllocationRow("Fundusz", SavingsKind, FixedShare, 100m), AllocationRow("fundusz", SavingsKind, FixedShare, 200m), remainder]
            ),
            ("fixed share 0", "2026-10", [AllocationRow("Wakacje", SavingsKind, FixedShare, 0m), remainder]),
            ("percent 0 on a non-remainder", "2026-10", [AllocationRow("Akcje", InvestmentsKind, PercentShare, 0m), remainder]),
            (
                "no remainder",
                "2026-10",
                [AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m)]
            ),
            (
                "two remainders",
                "2026-10",
                [AllocationRow("Akcje", InvestmentsKind, PercentShare, 10m, isRemainder: true), remainder]
            ),
            (
                "bucketId with another kind",
                "2026-10",
                [AllocationRow("Akcje", SavingsKind, PercentShare, 50m, bucketId: akcjeId), remainder]
            )
        };
        foreach (var (name, month, buckets) in invalid)
        {
            var response = await client.PutAsJsonAsync(AllocationPlanVersionUri(month), NewAllocationPlanBody(buckets), cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
        }

        var overHundred = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("A", InvestmentsKind, PercentShare, 60m),
                AllocationRow("B", InvestmentsKind, PercentShare, 40.01m),
                remainder),
            cancellationToken);

        await overHundred.AssertProblemAsync(HttpStatusCode.BadRequest, PercentSumOver100ErrorCode, cancellationToken);
        var after = await client.GetAllocationAsync("2026-10", cancellationToken);
        Assert.Equal(before.Buckets.Select(b => (b.BucketId, b.Name, b.AllocatedPln)), after.Buckets.Select(b => (b.BucketId, b.Name, b.AllocatedPln)));
        Assert.Equal(1, await CountAllocationVersionsAsync(userId, cancellationToken));
        Assert.Equal(4, await CountAllocationBucketsAsync(userId, cancellationToken));

        var empty = await client.PutAsJsonAsync(AllocationPlanVersionUri("2026-12"), NewAllocationPlanBody(), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        Assert.Empty((await client.GetAllocationAsync("2026-12", cancellationToken)).Buckets);
    }

    [Fact]
    public async Task InvalidCushion_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var cushionId = (await client.GetAllocationAsync("2026-10", cancellationToken)).Buckets[0].BucketId;
        var remainder = AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 100m, isRemainder: true);

        var invalid = new (string Case, object[] Buckets)[]
        {
            ("two cushions", [CushionRow(6), CushionRow(3, name: "Druga"), remainder]),
            ("cushion with a share", [CushionRow(6, shareType: PercentShare, share: 10m), remainder]),
            ("cushion as remainder", [CushionRow(6, isRemainder: true)]),
            ("targetMonths 0", [CushionRow(0), remainder]),
            ("targetMonths 25", [CushionRow(25), remainder]),
            ("targetMonths missing", [CushionRow(null), remainder]),
            ("targetMonths on another kind", [AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, targetMonths: 6), remainder]),
            ("negative starting balance", [CushionRow(6, -1m), remainder]),
            ("starting balance on another kind", [AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, startingBalance: 100m), remainder]),
            ("starting balance on an existing cushion", [CushionRow(6, 100m, cushionId), remainder])
        };
        foreach (var (name, buckets) in invalid)
        {
            var response = await client.PutAsJsonAsync(AllocationPlanVersionUri("2026-10"), NewAllocationPlanBody(buckets), cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
        }

        var unchanged = await client.GetAllocationAsync("2026-10", cancellationToken);
        Assert.Equal(["Poduszka", "Wakacje", "Inwestycje"], unchanged.Buckets.Select(b => b.Name));
        Assert.Equal(1, await CountAllocationVersionsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task InvalidLimitedBuckets_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateAllocationSetupAsync(cancellationToken);
        var ike = await client.CreateAccountAsync(cancellationToken, "IKE Dominik", AccountType.Brokerage, TaxWrapper.Ike);
        var regular = await client.CreateAccountAsync(cancellationToken, "XTB", AccountType.Brokerage, TaxWrapper.Regular);
        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("IKE", IkeKind, PercentShare, 50m, accountId: ike, startingBalance: 1_000m),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 50m, isRemainder: true));
        var ikeId = (await client.GetAllocationAsync("2026-10", cancellationToken)).Buckets[0].BucketId;
        var remainder = AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 50m, isRemainder: true);

        var wrapperCases = new (string Case, object[] Buckets)[]
        {
            ("IKE on a Regular account", [AllocationRow("IKE", IkeKind, PercentShare, 50m, accountId: regular), remainder]),
            ("IKZE JDG on an IKE account", [AllocationRow("JDG", IkzeJdgKind, PercentShare, 50m, accountId: ike), remainder])
        };
        foreach (var (_, buckets) in wrapperCases)
        {
            var response = await client.PutAsJsonAsync(AllocationPlanVersionUri("2026-10"), NewAllocationPlanBody(buckets), cancellationToken);

            await response.AssertProblemAsync(HttpStatusCode.BadRequest, BucketAccountWrapperErrorCode, cancellationToken);
        }

        var invalid = new (string Case, object[] Buckets)[]
        {
            ("IKE as remainder", [AllocationRow("IKE", IkeKind, PercentShare, 50m, isRemainder: true)]),
            ("negative contributed", [AllocationRow("IKE", IkeKind, PercentShare, 50m, startingBalance: -1m), remainder]),
            ("contributed on an existing bucket", [AllocationRow("IKE", IkeKind, PercentShare, 50m, bucketId: ikeId, startingBalance: 500m), remainder])
        };
        foreach (var (name, buckets) in invalid)
        {
            var response = await client.PutAsJsonAsync(AllocationPlanVersionUri("2026-10"), NewAllocationPlanBody(buckets), cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
        }

        Assert.Equal(1, await CountAllocationVersionsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task ArchivedAccount_OnlyWhenChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        var savings = await client.CreateBankAccountAsync(cancellationToken, "Konto oszczędnościowe", SavingsVariant);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan(savings.AccountId));
        var saved = await client.GetAllocationAsync("2026-10", cancellationToken);
        Assert.Equal(savings.AccountId, saved.Buckets.Single(b => b.Name == "Wakacje").AccountId);
        await client.ArchiveAccountAsync(savings.AccountId, cancellationToken);
        Guid IdOf(string name) => saved.Buckets.Single(b => b.Name == name).BucketId;

        var unchanged = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, bucketId: IdOf("Wakacje"), accountId: savings.AccountId),
                AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m, bucketId: IdOf("Akcje")),
                AllocationRow("Lokata", SavingsKind, PercentShare, 20m, bucketId: IdOf("Lokata")),
                AllocationRow("ETF", InvestmentsKind, PercentShare, 30m, isRemainder: true, bucketId: IdOf("ETF"))),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);

        var changed = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, bucketId: IdOf("Wakacje"), accountId: savings.AccountId),
                AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m, bucketId: IdOf("Akcje")),
                AllocationRow("Lokata", SavingsKind, PercentShare, 20m, bucketId: IdOf("Lokata")),
                AllocationRow("ETF", InvestmentsKind, PercentShare, 30m, isRemainder: true, bucketId: IdOf("ETF"), accountId: savings.AccountId)),
            cancellationToken);

        await changed.AssertProblemAsync(HttpStatusCode.Conflict, "Conflict.AccountArchived", cancellationToken);

        var unknown = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            NewAllocationPlanBody(
                AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, bucketId: IdOf("Wakacje"), accountId: Guid.NewGuid()),
                AllocationRow("ETF", InvestmentsKind, PercentShare, 100m, isRemainder: true, bucketId: IdOf("ETF"))),
            cancellationToken);

        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "NotFound.Account", cancellationToken);
    }
}
