using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.RemoveWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class WatchlistOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task Add_WritesWatchlistItemAdded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var instrumentId = Guid.NewGuid();

        var result = await scope.ServiceProvider.GetRequiredService<AddWatchlistItemHandler>()
            .HandleAsync(new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(await dbContext.ReadPublishedAsync<WatchlistItemAdded>(cancellationToken));
        Assert.Multiple(
            () => Assert.Equal(result.Value.Id, evt.WatchlistItemId),
            () => Assert.Equal(UserId, evt.UserId),
            () => Assert.Equal(instrumentId, evt.InstrumentId));
        Assert.Empty(await dbContext.ReadPublishedAsync<WatchlistItemRemoved>(cancellationToken));
    }

    [Fact]
    public async Task Update_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var added = await scope.ServiceProvider.GetRequiredService<AddWatchlistItemHandler>()
            .HandleAsync(new AddWatchlistItemRequest { InstrumentId = Guid.NewGuid() }, cancellationToken);
        Assert.True(added.IsSuccess);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var updated = await scope.ServiceProvider.GetRequiredService<UpdateWatchlistItemHandler>()
            .HandleAsync(added.Value.Id, new UpdateWatchlistItemRequest { Note = "later", TargetPrice = 12.5m }, cancellationToken);

        Assert.True(updated.IsSuccess);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_WritesWatchlistItemRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var instrumentId = Guid.NewGuid();
        var added = await scope.ServiceProvider.GetRequiredService<AddWatchlistItemHandler>()
            .HandleAsync(new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);
        Assert.True(added.IsSuccess);

        var removed = await scope.ServiceProvider.GetRequiredService<RemoveWatchlistItemHandler>()
            .HandleAsync(added.Value.Id, cancellationToken);

        Assert.True(removed.IsSuccess);
        var evt = Assert.Single(await dbContext.ReadPublishedAsync<WatchlistItemRemoved>(cancellationToken));
        Assert.Multiple(
            () => Assert.Equal(added.Value.Id, evt.WatchlistItemId),
            () => Assert.Equal(UserId, evt.UserId),
            () => Assert.Equal(instrumentId, evt.InstrumentId));
    }
}
