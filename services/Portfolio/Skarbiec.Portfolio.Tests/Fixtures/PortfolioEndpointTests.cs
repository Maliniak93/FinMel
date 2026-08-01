using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Base for Portfolio's HTTP slice tests. Supplies the test host to
/// <see cref="ServiceEndpointTests{TProgram}"/>, so a test class declares only
/// <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts.
/// </summary>
public abstract class PortfolioEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    /// <summary>
    /// A <see cref="PortfolioDbContext"/> scoped to <paramref name="userId"/>, talking to the same
    /// database as <see cref="ServiceEndpointTests{TProgram}.Factory"/>. For the few facts the HTTP
    /// surface can't express — seeding a denormalized counter, or forcing a genuine write race that
    /// a single request handler can never produce.
    /// </summary>
    protected PortfolioDbContext CreateDbContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new PortfolioDbContext(options, new StubCurrentUser(userId));
    }
}
