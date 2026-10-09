using NetArchTest.Rules;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Tests;

public sealed class ArchitectureTests
{
    // Global reference data and a value stored inside a tenant-scoped row, listed by type so any other entity in the namespace is still forced to be tenant-scoped.
    private static readonly HashSet<Type> GlobalReferenceData =
    [
        typeof(Skarbiec.Reporting.Data.LatestInstrumentPrice),
        typeof(Skarbiec.Reporting.Data.LatestFxRate),
        typeof(Skarbiec.Reporting.Data.PositionQuantityPoint),
    ];

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

    [Fact]
    public void ReportingAssembly_ContainsNoPortfolioHttpClientTypes()
    {
        var types = Types.InAssembly(typeof(Program).Assembly).GetTypes().ToList();

        Assert.DoesNotContain(types, t => t.Namespace == "Skarbiec.Reporting.Portfolio");
        Assert.DoesNotContain(types, t => t.Name.EndsWith("PositionsClient", StringComparison.Ordinal));
    }
}
