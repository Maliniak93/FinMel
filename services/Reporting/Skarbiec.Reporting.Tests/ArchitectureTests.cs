using NetArchTest.Rules;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// Architecture guardrails (T0.14, ADR-006). No Testcontainers needed — pure reflection over the
/// compiled assembly.
/// </summary>
public sealed class ArchitectureTests
{
    /// <summary>
    /// spec-07 design decision 4: the last known prices and FX rates are global reference data, not
    /// user data — no <c>UserId</c>, no query filter (ADR-006 covers user-owned entities only). Listed
    /// by type so any other entity added to the namespace is still forced to be tenant-scoped.
    /// </summary>
    private static readonly HashSet<Type> GlobalReferenceData =
    [
        typeof(Skarbiec.Reporting.Data.LatestInstrumentPrice),
        typeof(Skarbiec.Reporting.Data.LatestFxRate),
    ];

    /// <summary>
    /// Decision: every class in <c>Skarbiec.Reporting.Data</c> other than the DbContext and its
    /// design-time factory is a domain entity and must implement <see cref="IUserOwned"/> —
    /// Reporting is one of the three tenancy-filtered services (ADR-006, T0.13). No entities exist
    /// yet (T0.13 skeleton), so this currently passes vacuously and starts guarding the moment
    /// Phase 2 adds the first one (e.g. ValuationSnapshot).
    /// </summary>
    [Fact]
    public void DataNamespaceEntities_ImplementIUserOwned()
    {
        var entityTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespace("Skarbiec.Reporting.Data")
            .GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && !t.Name.EndsWith("DbContext", StringComparison.Ordinal)
                && !t.Name.EndsWith("DbContextFactory", StringComparison.Ordinal)
                && !GlobalReferenceData.Contains(t))
            .ToList();

        Assert.All(entityTypes, t => Assert.True(
            typeof(IUserOwned).IsAssignableFrom(t),
            $"{t.Name} lives in the tenancy-scoped Data namespace but doesn't implement IUserOwned — " +
            "every domain entity here must be tenant-scoped (ADR-006)."));
    }

    /// <summary>
    /// UserId always comes from the JWT (<c>ICurrentUser</c>), never from caller-supplied input
    /// (ADR-006) — a request record binding it would let one user stamp another user's id.
    /// </summary>
    [Fact]
    public void RequestTypes_NeverExposeSettable_UserId()
    {
        var requestTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Skarbiec.Reporting.Features")
            .And()
            .HaveNameEndingWith("Request")
            .GetTypes();

        Assert.All(requestTypes, t => Assert.Null(
            t.GetProperty("UserId")));
    }

    /// <summary>
    /// spec-03 AC15: Reporting stops calling Portfolio over REST — <c>DailyPricesSyncedConsumer</c>
    /// values every portfolio from its own local <c>Position</c> table instead of
    /// <c>PortfolioPositionsClient</c>. No type may remain in the old <c>Skarbiec.Reporting.Portfolio</c>
    /// namespace and nothing may still be named like a Portfolio HTTP client.
    /// </summary>
    [Fact]
    public void ReportingAssembly_ContainsNoPortfolioHttpClientTypes()
    {
        var types = Types.InAssembly(typeof(Program).Assembly).GetTypes().ToList();

        Assert.DoesNotContain(types, t => t.Namespace == "Skarbiec.Reporting.Portfolio");
        Assert.DoesNotContain(types, t => t.Name.EndsWith("PositionsClient", StringComparison.Ordinal));
    }
}
