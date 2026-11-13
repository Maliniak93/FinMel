using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetGroupRepublishServiceTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task PublishesFlaggedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid groupId;
        Guid accountId;
        Guid flaggedOne;
        Guid flaggedTwo;
        Guid untouched;

        await using (var scope = Provider.CreateAsyncScope())
        {
            groupId = await CreateGroupAsync(scope.ServiceProvider, "Poduszka", cancellationToken);
            accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
            flaggedOne = await AddStockAsync(scope.ServiceProvider, accountId, "One", cancellationToken);
            flaggedTwo = await AddStockAsync(scope.ServiceProvider, accountId, "Two", cancellationToken);
            untouched = await AddStockAsync(scope.ServiceProvider, accountId, "Untouched", cancellationToken);

            var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            foreach (var asset in await dbContext.Assets.Where(a => a.Id == flaggedOne || a.Id == flaggedTwo).ToListAsync(cancellationToken))
            {
                asset.GroupId = groupId;
                asset.GroupRepublishPending = true;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var versionsBefore = await ReadVersionsAsync(cancellationToken, flaggedOne, flaggedTwo, untouched);
        var eventsBefore = await CountPositionEventsAsync(cancellationToken);

        await RunServiceAsync(cancellationToken);

        var events = await ReadEventsAsync(cancellationToken);
        var republished = events.Skip(eventsBefore).ToList();
        Assert.Equal(2, republished.Count);
        foreach (var assetId in new[] { flaggedOne, flaggedTwo })
        {
            var published = Assert.Single(republished, e => e.AssetId == assetId);
            Assert.Equal(groupId, published.GroupId);
            Assert.Equal(versionsBefore[assetId] + 1, published.Version);
        }

        await using (var verify = Provider.CreateAsyncScope())
        {
            var dbContext = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            Assert.Empty(await dbContext.Assets.Where(a => a.GroupRepublishPending).ToListAsync(cancellationToken));
        }

        await RunServiceAsync(cancellationToken);

        Assert.Equal(eventsBefore + 2, (await ReadEventsAsync(cancellationToken)).Count);
        Assert.Equal(versionsBefore[untouched], (await ReadVersionsAsync(cancellationToken, untouched))[untouched]);
    }

    private async Task RunServiceAsync(CancellationToken cancellationToken)
    {
        var service = ActivatorUtilities.CreateInstance<AssetGroupRepublishService>(Provider);
        await ((IHostedService)service).StartAsync(cancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private async Task<IReadOnlyList<AssetPositionChanged>> ReadEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
    }

    private async Task<Dictionary<Guid, long>> ReadVersionsAsync(CancellationToken cancellationToken, params Guid[] assetIds)
    {
        await using var scope = Provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Assets
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Version, cancellationToken);
    }
}
