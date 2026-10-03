using NetArchTest.Rules;
using Skarbiec.Portfolio.Data;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DataNamespaceEntities_ImplementIUserOwned()
    {
        var entityTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespace("Skarbiec.Portfolio.Data")
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

    [Fact]
    public void RequestTypes_NeverExposeSettable_UserId()
    {
        var requestTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Skarbiec.Portfolio.Features")
            .And()
            .HaveNameEndingWith("Request")
            .GetTypes();

        Assert.All(requestTypes, t => Assert.Null(
            t.GetProperty("UserId")));
    }

    [Fact]
    public void PortfolioEntities_ExposeNoDenormalizedCounters()
    {
        Assert.Null(typeof(PortfolioEntity).GetProperty("AssetCount"));
        Assert.Null(typeof(Asset).GetProperty("TransactionCount"));
    }
}
