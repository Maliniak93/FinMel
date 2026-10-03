using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests.Fixtures;

public abstract class ReportingEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override ReportingApiFactory Factory { get; } = new(containers);

    // Snapshots have no HTTP write path, so slice tests seed them directly.
    protected ReportingDbContext CreateDbContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new ReportingDbContext(options, new StubCurrentUser(userId));
    }
}
