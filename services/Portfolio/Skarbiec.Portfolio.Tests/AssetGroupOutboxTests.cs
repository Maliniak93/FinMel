using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;
using Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.DeleteAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetGroupOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task Assign_WritesChangedPositionPerAssetWithGroupIdInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (first, second, groupId) = await ArrangeAsync(scope.ServiceProvider, cancellationToken);
        var firstVersion = (await dbContext.LastPublishedAsync(first, cancellationToken)).Version;
        var published = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        SaveChanges.Reset();

        var assigned = await scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>().HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first, second], GroupId = groupId }, cancellationToken);

        Assert.True(assigned.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        Assert.Equal(new[] { first, second }.Order().ToArray(), events.Select(e => e.AssetId).Order().ToArray());
        Assert.All(events, e => Assert.Equal(groupId, e.GroupId));
        Assert.Equal(firstVersion + 1, events.Single(e => e.AssetId == first).Version);
    }

    [Fact]
    public async Task Clear_WritesChangedPositionPerAssetWithNullGroupId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (first, second, groupId) = await ArrangeAsync(scope.ServiceProvider, cancellationToken);
        var handler = scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>();
        await handler.HandleAsync(new AssignAssetsToGroupRequest { AssetIds = [first, second], GroupId = groupId }, cancellationToken);
        var published = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;

        var cleared = await handler.HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first, second], GroupId = null }, cancellationToken);

        Assert.True(cleared.IsSuccess);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Null(e.GroupId));
    }

    [Fact]
    public async Task Assign_SameGroupAgain_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (first, second, groupId) = await ArrangeAsync(scope.ServiceProvider, cancellationToken);
        var handler = scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>();
        await handler.HandleAsync(new AssignAssetsToGroupRequest { AssetIds = [first], GroupId = groupId }, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var again = await handler.HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first], GroupId = groupId }, cancellationToken);
        var mixed = await handler.HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first, second], GroupId = groupId }, cancellationToken);

        Assert.True(again.IsSuccess);
        Assert.True(mixed.IsSuccess);
        Assert.Equal(outboxRowsBefore + 1, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(1, events.Count(e => e.AssetId == first && e.GroupId == groupId));
        Assert.Equal(1, events.Count(e => e.AssetId == second && e.GroupId == groupId));
    }

    [Fact]
    public async Task Delete_ClearsAssetsAndWritesChangedPositionsInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (first, second, groupId) = await ArrangeAsync(scope.ServiceProvider, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>().HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first, second], GroupId = groupId }, cancellationToken);
        var published = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        SaveChanges.Reset();

        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteAssetGroupHandler>().HandleAsync(groupId, cancellationToken);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Null(e.GroupId));
        Assert.Equal(new[] { first, second }.Order().ToArray(), events.Select(e => e.AssetId).Order().ToArray());
        Assert.False(await dbContext.Set<AssetGroup>().AnyAsync(g => g.Id == groupId, cancellationToken));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.GroupId != null, cancellationToken));
    }

    [Fact]
    public async Task Update_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (first, _, groupId) = await ArrangeAsync(scope.ServiceProvider, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>().HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [first], GroupId = groupId }, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var updated = await scope.ServiceProvider.GetRequiredService<UpdateAssetGroupHandler>().HandleAsync(
            groupId, new UpdateAssetGroupRequest { Name = "Renamed", Color = AssetGroupColors.Keys[5], TargetPln = 1_500m }, cancellationToken);

        Assert.True(updated.IsSuccess);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
        var group = await dbContext.Set<AssetGroup>().AsNoTracking().SingleAsync(g => g.Id == groupId, cancellationToken);
        Assert.Equal(("Renamed", AssetGroupColors.Keys[5], 1_500m), (group.Name, group.Color, group.TargetPln));
    }

    [Fact]
    public async Task RecordTransaction_OnGroupedAsset_PublishesGroupId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Grouped", cancellationToken);
        var assetId = await AddAssetWithBuyAsync(scope.ServiceProvider, accountId, "Held", cancellationToken);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAssetGroupHandler>().HandleAsync(
            new CreateAssetGroupRequest { Name = "Poduszka", Color = AssetGroupColors.Keys[0] }, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AssignAssetsToGroupHandler>().HandleAsync(
            new AssignAssetsToGroupRequest { AssetIds = [assetId], GroupId = created.Value.Id }, cancellationToken);

        await RecordTradeAsync(scope.ServiceProvider, assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 3), cancellationToken);

        Assert.Equal(created.Value.Id, (await dbContext.LastPublishedAsync(assetId, cancellationToken)).GroupId);
    }

    private static async Task<(Guid First, Guid Second, Guid GroupId)> ArrangeAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var accountId = await CreateAccountAsync(services, "Grouped", cancellationToken);
        var first = await AddStockAsync(services, accountId, "First", cancellationToken);
        var second = await AddStockAsync(services, accountId, "Second", cancellationToken);
        var created = await services.GetRequiredService<CreateAssetGroupHandler>().HandleAsync(
            new CreateAssetGroupRequest { Name = "Poduszka", Color = AssetGroupColors.Keys[0] }, cancellationToken);
        Assert.True(created.IsSuccess);

        return (first, second, created.Value.Id);
    }
}
