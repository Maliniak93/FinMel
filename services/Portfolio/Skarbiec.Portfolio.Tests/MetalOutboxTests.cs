using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals.AddMetal;
using Skarbiec.Portfolio.Features.Metals.UpdateMetal;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class MetalOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddMetal_PublishesPositionChangedWithFineWeight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Metal outbox portfolio", cancellationToken);
        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddMetalHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewMetalRequest(), cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        Assert.Equal(AssetClass.PreciousMetal, asset.AssetClass);
        Assert.Equal(10m, asset.Quantity);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), asset.InstrumentId);
        Assert.True(await verifyDb.Set<MetalHolding>().AnyAsync(h => h.AssetId == assetId, cancellationToken));
        Assert.Single(await verifyDb.Transactions.Where(t => t.AssetId == assetId).ToListAsync(cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.PreciousMetal, evt.AssetClass);
        Assert.Equal(AssetValuationMode.Market, evt.ValuationMode);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), evt.InstrumentId);
        Assert.Equal(10m, evt.Quantity);
        Assert.Equal(31.10347680m, evt.QuoteUnitsPerQuantity);
    }

    [Fact]
    public async Task UpdateMetal_PublishesPositionChangedWithNewFineWeight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Metal update portfolio", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddMetalHandler>().HandleAsync(
                portfolioId, PortfolioApi.NewMetalRequest(metal: Metal.Gold), cancellationToken);
            assetId = added.Value.AssetId;
        }

        SaveChanges.Reset();
        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UpdateMetalHandler>().HandleAsync(
                portfolioId, assetId, PortfolioApi.NewUpdateMetalRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);
        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.Equal(31.10347680m, events[0].QuoteUnitsPerQuantity);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), events[1].InstrumentId);
        Assert.Equal(100m, events[1].QuoteUnitsPerQuantity);
        Assert.True(events[1].Version > events[0].Version);
    }

    [Fact]
    public async Task PositionEventPublisher_NonMetalAsset_PublishesMultiplierOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Plain portfolio", cancellationToken);
        await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(1m, e.QuoteUnitsPerQuantity));
    }
}
