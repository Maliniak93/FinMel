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
                && !t.Name.EndsWith("DbContextFactory", StringComparison.Ordinal))
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
}
