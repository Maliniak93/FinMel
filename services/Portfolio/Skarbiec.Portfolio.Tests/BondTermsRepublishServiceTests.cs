using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondTermsRepublishServiceTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task Startup_RepublishesBonds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var otherUserId = Guid.NewGuid();
        Guid bondId;
        Guid otherBondId;

        await using (var scope = Provider.CreateAsyncScope())
        {
            bondId = await AddFundedBondAsync(scope.ServiceProvider, "Bonds A", cancellationToken);
        }

        ActAs(otherUserId);
        await using (var scope = Provider.CreateAsyncScope())
        {
            otherBondId = await AddFundedBondAsync(scope.ServiceProvider, "Bonds B", cancellationToken);
        }

        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        var service = ActivatorUtilities.CreateInstance<BondTermsRepublishService>(Provider);

        await ((IHostedService)service).StartAsync(cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        IReadOnlyList<AssetPositionChanged> events;
        while (true)
        {
            await using var scope = Provider.CreateAsyncScope();
            events = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
            if (events.Count >= eventsBefore + 2 || DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        var republished = events.Skip(eventsBefore).Where(e => e.BondTerms is not null).ToList();
        Assert.Equal(2, republished.Count);
        var first = Assert.Single(republished, e => e.AssetId == bondId);
        var second = Assert.Single(republished, e => e.AssetId == otherBondId);
        Assert.Equal(1, first.Version);
        Assert.Equal(1, second.Version);
        Assert.Equal(otherUserId, second.UserId);
        Assert.NotEqual(otherUserId, first.UserId);

        await using var verify = Provider.CreateAsyncScope();
        var versions = await verify.ServiceProvider.GetRequiredService<PortfolioDbContext>().Assets
            .IgnoreQueryFilters()
            .Where(a => a.Id == bondId || a.Id == otherBondId)
            .Select(a => a.Version)
            .ToListAsync(cancellationToken);
        Assert.All(versions, v => Assert.Equal(1, v));
    }

    private static async Task<Guid> AddFundedBondAsync(IServiceProvider services, string accountName, CancellationToken cancellationToken)
    {
        var accountId = await CreateAccountAsync(services, accountName, cancellationToken, AccountType.Bonds);
        await FundAccountCashAsync(services, accountId, cancellationToken, 10_000m);

        var result = await services.GetRequiredService<AddBondHandler>()
            .HandleAsync(PortfolioApi.NewBondRequest() with { AccountId = accountId }, cancellationToken);
        Assert.True(result.IsSuccess);

        return result.Value.AssetId;
    }
}
