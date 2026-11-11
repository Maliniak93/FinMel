using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The asset group slices are under test, so they are called directly; the fixtures only arrange.
public sealed class AssetGroupEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Crud_ReturnsShape()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken);

        var created = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Zeta", ColorKey(0), 1_000.5m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var zeta = (await created.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
        Assert.Equal(("Zeta", ColorKey(0), 1_000.5m, 0), (zeta.Name, zeta.Color, zeta.TargetPln, zeta.AssetCount));
        await client.CreateAssetGroupAsync(cancellationToken, "Alfa", ColorKey(1));
        await client.AssignAssetsAsync(zeta.Id, cancellationToken, assetId);

        var listed = await client.GetAsync(AssetGroupsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var groups = (await listed.Content.ReadFromJsonAsync<List<AssetGroupView>>(cancellationToken))!;
        Assert.Equal(["Alfa", "Zeta"], groups.Select(g => g.Name).ToArray());
        Assert.Equal((ColorKey(1), (decimal?)null, 0), (groups[0].Color, groups[0].TargetPln, groups[0].AssetCount));
        Assert.Equal((ColorKey(0), 1_000.5m, 1), (groups[1].Color, groups[1].TargetPln, groups[1].AssetCount));

        var updated = await client.PutAsJsonAsync(AssetGroupUri(zeta.Id), NewAssetGroupBody("Beta", ColorKey(2), 2_000m), cancellationToken);

        Assert.True(updated.IsSuccessStatusCode);
        var afterUpdate = await client.ListAssetGroupsAsync(cancellationToken);
        Assert.Equal(["Alfa", "Beta"], afterUpdate.Select(g => g.Name).ToArray());
        var beta = Assert.Single(afterUpdate, g => g.Id == zeta.Id);
        Assert.Equal((ColorKey(2), 2_000m, 1), (beta.Color, beta.TargetPln, beta.AssetCount));

        var deleted = await client.DeleteAsync(AssetGroupUri(zeta.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["Alfa"], (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Name).ToArray());
    }

    [Fact]
    public async Task CreatesGroupWithContributionAndAllocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountA = await client.CreateAccountAsync(cancellationToken, "Konto A");
        var accountB = await client.CreateAccountAsync(cancellationToken, "Konto B");
        var allocation = Shares((AssetClass.Deposit, 50m), (AssetClass.BankAccount, 50m));
        ContributionBody[] contributions = [new(2_000m, accountA), new(2_000m, accountB)];

        var created = await client.PostAsJsonAsync(
            AssetGroupsUri,
            NewAssetGroupBody("Emerytura", ColorKey(0), null, RegularKind, 0m, allocation, contributions),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = (await created.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
        Assert.Equal((RegularKind, 4_000m), (view.Kind, view.MonthlyContributionPln));
        Assert.Equal(contributions, view.Contributions);
        Assert.Equal(allocation, view.ClassAllocation);
        var listed = Assert.Single(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal((RegularKind, 4_000m), (listed.Kind, listed.MonthlyContributionPln));
        Assert.Equal(contributions, listed.Contributions);
        Assert.Equal(allocation, listed.ClassAllocation);
        var updated = await client.PutAsJsonAsync(
            AssetGroupUri(view.Id), NewAssetGroupBody("Emerytura", ColorKey(0), null, RegularKind, 0m), cancellationToken);
        Assert.True(updated.IsSuccessStatusCode);
        var cleared = Assert.Single(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal(0m, cleared.MonthlyContributionPln);
        Assert.Empty(cleared.Contributions ?? []);
        Assert.Empty(cleared.ClassAllocation ?? []);
    }

    [Fact]
    public async Task UpdateReplacesContributions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountA = await client.CreateAccountAsync(cancellationToken, "Konto A");
        var accountB = await client.CreateAccountAsync(cancellationToken, "Konto B");
        var group = await client.CreateAssetGroupAsync(
            cancellationToken, "Emerytura", ColorKey(0), contributions: [new(2_000m, accountA), new(1_000m, accountB)]);
        Assert.Equal(2, group.Contributions!.Count);

        var updated = await client.PutAsJsonAsync(
            AssetGroupUri(group.Id),
            NewAssetGroupBody("Emerytura", ColorKey(0), null, RegularKind, 0m, null, [new ContributionBody(500m)]),
            cancellationToken);

        Assert.True(updated.IsSuccessStatusCode);
        var listed = Assert.Single(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal([new ContributionBody(500m)], listed.Contributions);
        Assert.Equal(500m, listed.MonthlyContributionPln);
    }

    [Fact]
    public async Task ContributionAccountMustBeOwnAndActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var foreignAccount = await stranger.CreateAccountAsync(cancellationToken, "Obce");
        var archived = await client.CreateAccountAsync(cancellationToken, "Stare");
        await client.ArchiveAccountAsync(archived, cancellationToken);
        var active = await client.CreateAccountAsync(cancellationToken, "Aktywne");
        var existing = await client.CreateAssetGroupAsync(
            cancellationToken, "Istniejąca", ColorKey(1), contributions: [new(100m, active)]);

        var foreign = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), contributions: [new(100m, foreignAccount)]), cancellationToken);
        var archivedOnCreate = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), contributions: [new(100m, archived)]), cancellationToken);
        var archivedOnUpdate = await client.PutAsJsonAsync(
            AssetGroupUri(existing.Id),
            NewAssetGroupBody("Istniejąca", ColorKey(1), contributions: [new(100m, active), new(50m, archived)]),
            cancellationToken);

        await foreign.AssertProblemAsync(HttpStatusCode.NotFound, AccountNotFoundErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, archivedOnCreate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, archivedOnUpdate.StatusCode);
        var stored = Assert.Single(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal([new ContributionBody(100m, active)], stored.Contributions);
    }

    [Fact]
    public async Task CushionWithoutTargetReturns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var regular = await client.CreateAssetGroupAsync(cancellationToken, "Zwykła", ColorKey(1));

        var create = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Poduszka", ColorKey(0), null, CushionKind), cancellationToken);
        var update = await client.PutAsJsonAsync(
            AssetGroupUri(regular.Id), NewAssetGroupBody("Zwykła", ColorKey(1), null, CushionKind), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.BadRequest, TargetRequiredForCushionErrorCode, cancellationToken);
        await update.AssertProblemAsync(HttpStatusCode.BadRequest, TargetRequiredForCushionErrorCode, cancellationToken);
        var kinds = (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Kind).ToArray();
        Assert.Equal([RegularKind], kinds);
    }

    [Fact]
    public async Task SecondCushionReturns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var cushion = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(0), 12_000m, CushionKind);
        var regular = await client.CreateAssetGroupAsync(cancellationToken, "Zwykła", ColorKey(1), 500m);

        var create = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Druga", ColorKey(2), 1_000m, CushionKind), cancellationToken);
        var turn = await client.PutAsJsonAsync(
            AssetGroupUri(regular.Id), NewAssetGroupBody("Zwykła", ColorKey(1), 500m, CushionKind), cancellationToken);
        var keepOwn = await client.PutAsJsonAsync(
            AssetGroupUri(cushion.Id), NewAssetGroupBody("Poduszka", ColorKey(0), 13_000m, CushionKind), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, CushionGroupExistsErrorCode, cancellationToken);
        await turn.AssertProblemAsync(HttpStatusCode.Conflict, CushionGroupExistsErrorCode, cancellationToken);
        Assert.True(keepOwn.IsSuccessStatusCode);
        Assert.Equal(2, (await client.ListAssetGroupsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task InvalidClassAllocationReturns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var existing = await client.CreateAssetGroupAsync(cancellationToken, "Existing", ColorKey(1));

        var sum90 = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), null, RegularKind, 0m, Shares((AssetClass.Deposit, 50m), (AssetClass.BankAccount, 40m))), cancellationToken);
        var repeated = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), null, RegularKind, 0m, Shares((AssetClass.Deposit, 50m), (AssetClass.Deposit, 50m))), cancellationToken);
        var zero = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), null, RegularKind, 0m, Shares((AssetClass.Deposit, 100m), (AssetClass.BankAccount, 0m))), cancellationToken);
        var updateSum = await client.PutAsJsonAsync(
            AssetGroupUri(existing.Id), NewAssetGroupBody("Existing", ColorKey(1), null, RegularKind, 0m, Shares((AssetClass.Etf, 70m))), cancellationToken);

        await sum90.AssertProblemAsync(HttpStatusCode.BadRequest, ClassAllocationSumErrorCode, cancellationToken);
        await repeated.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidClassAllocationErrorCode, cancellationToken);
        await zero.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidClassAllocationErrorCode, cancellationToken);
        await updateSum.AssertProblemAsync(HttpStatusCode.BadRequest, ClassAllocationSumErrorCode, cancellationToken);
        Assert.Equal(["Existing"], (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Name).ToArray());
    }

    [Theory]
    [InlineData(0, 1, InvalidContributionErrorCode)]
    [InlineData(-1, 1, InvalidContributionErrorCode)]
    [InlineData(0.001, 1, InvalidContributionErrorCode)]
    [InlineData(1, 21, TooManyContributionsErrorCode)]
    public async Task InvalidContributionReturns400(double amount, int count, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var contributions = Enumerable.Range(0, count).Select(_ => new ContributionBody((decimal)amount)).ToArray();

        var create = await client.PostAsJsonAsync(
            AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), contributions: contributions), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.BadRequest, errorCode, cancellationToken);
        Assert.Empty(await client.ListAssetGroupsAsync(cancellationToken));
    }

    [Fact]
    public async Task DuplicateName_IgnoresCase_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var poduszka = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(0));
        var other = await client.CreateAssetGroupAsync(cancellationToken, "Inna", ColorKey(1));

        var create = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("poduszka", ColorKey(2)), cancellationToken);
        var rename = await client.PutAsJsonAsync(AssetGroupUri(other.Id), NewAssetGroupBody("PODUSZKA", ColorKey(1)), cancellationToken);
        var recase = await client.PutAsJsonAsync(AssetGroupUri(poduszka.Id), NewAssetGroupBody("poduszka", ColorKey(0)), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        await rename.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        Assert.True(recase.IsSuccessStatusCode);
        var groups = await client.ListAssetGroupsAsync(cancellationToken);
        Assert.Equal(["Inna", "poduszka"], groups.Select(g => g.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Create13th_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        for (var i = 0; i < GroupLimit; i++)
        {
            await client.CreateAssetGroupAsync(cancellationToken, $"Group {i}", ColorKey(i));
        }

        var response = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Group 12", ColorKey(0)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, GroupLimitReachedErrorCode, cancellationToken);
        Assert.Equal(GroupLimit, (await client.ListAssetGroupsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task InvalidColorOrTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var existing = await client.CreateAssetGroupAsync(cancellationToken, "Existing", ColorKey(1));

        var unknownColor = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", "no-such-colour"), cancellationToken);
        var zeroTarget = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), 0m), cancellationToken);
        var negativeTarget = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), -5m), cancellationToken);
        var threeDecimals = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), 10.123m), cancellationToken);
        var updateUnknownColor = await client.PutAsJsonAsync(AssetGroupUri(existing.Id), NewAssetGroupBody("Existing", "no-such-colour"), cancellationToken);
        var updateZeroTarget = await client.PutAsJsonAsync(AssetGroupUri(existing.Id), NewAssetGroupBody("Existing", ColorKey(1), 0m), cancellationToken);

        await unknownColor.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidColorErrorCode, cancellationToken);
        await zeroTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await negativeTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await threeDecimals.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await updateUnknownColor.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidColorErrorCode, cancellationToken);
        await updateZeroTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        Assert.Equal(["Existing"], (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Name).ToArray());
    }

    [Fact]
    public async Task ColorTaken_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAssetGroupAsync(cancellationToken, "Pierwsza", ColorKey(0));
        var second = await client.CreateAssetGroupAsync(cancellationToken, "Druga", ColorKey(1));

        var create = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Trzecia", ColorKey(0)), cancellationToken);
        var recolour = await client.PutAsJsonAsync(AssetGroupUri(second.Id), NewAssetGroupBody("Druga", ColorKey(0)), cancellationToken);
        var keepOwn = await client.PutAsJsonAsync(AssetGroupUri(second.Id), NewAssetGroupBody("Druga", ColorKey(1), 500m), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, GroupColorTakenErrorCode, cancellationToken);
        await recolour.AssertProblemAsync(HttpStatusCode.Conflict, GroupColorTakenErrorCode, cancellationToken);
        Assert.True(keepOwn.IsSuccessStatusCode);
    }
}
