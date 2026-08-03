using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests.Fixtures;

/// <summary>
/// Base for MarketData's host-backed tests. Supplies the test host to
/// <see cref="ServiceEndpointTests{TProgram}"/>, so a test class declares only
/// <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts.
/// </summary>
public abstract class MarketDataEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override MarketDataApiFactory Factory { get; } = new(containers);

    /// <summary>
    /// A <see cref="MarketDataDbContext"/> talking to the same database as
    /// <see cref="ServiceEndpointTests{TProgram}.Factory"/> — no <c>ICurrentUser</c> to stub since
    /// this service has no tenancy filter (global reference data).
    /// </summary>
    protected MarketDataDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new MarketDataDbContext(options);
    }
}
