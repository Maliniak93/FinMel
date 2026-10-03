using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests.Fixtures;

public abstract class MarketDataEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override MarketDataApiFactory Factory { get; } = new(containers);

    protected MarketDataDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new MarketDataDbContext(options);
    }
}
