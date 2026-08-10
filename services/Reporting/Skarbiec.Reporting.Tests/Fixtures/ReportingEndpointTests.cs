using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>
/// Base for Reporting's HTTP slice tests. Supplies the test host to
/// <see cref="ServiceEndpointTests{TProgram}"/>, so a test class declares only
/// <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts. Extracted once a second
/// host-backed test class arrived (T2.12) — see <c>HealthCheckTests</c>' remarks for why it was
/// the sole one until now.
/// </summary>
public abstract class ReportingEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override ReportingApiFactory Factory { get; } = new(containers);

    /// <summary>
    /// A <see cref="ReportingDbContext"/> scoped to <paramref name="userId"/>, talking to the same
    /// database as <see cref="ServiceEndpointTests{TProgram}.Factory"/>. There is no HTTP write path
    /// for <see cref="ValuationSnapshot"/> — only the <c>DailyPricesSynced</c> consumer (T2.11)
    /// writes it — so dashboard/history slice tests seed it directly instead of standing up a real
    /// consumer.
    /// </summary>
    protected ReportingDbContext CreateDbContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new ReportingDbContext(options, new StubCurrentUser(userId));
    }
}
