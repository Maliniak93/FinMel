using NetArchTest.Rules;

namespace Skarbiec.MarketData.Tests;

// No IUserOwned rule here: instruments, quotes and rates are global reference data.
public sealed class ArchitectureTests
{
    [Fact]
    public void RequestTypes_NeverExposeSettable_UserId()
    {
        var requestTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Features")
            .And()
            .HaveNameEndingWith("Request")
            .GetTypes();

        Assert.All(requestTypes, t => Assert.Null(
            t.GetProperty("UserId")));
    }

    [Fact]
    public void OnlySourcesNamespace_DependsOn_PriceSourceAbstractions()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .HaveDependencyOnAny(
                "Skarbiec.MarketData.Sources.IPriceSource",
                "Skarbiec.MarketData.Sources.IFxRateSource",
                "Skarbiec.MarketData.Sources.MfBonds.IMfBondSource")
            .Should()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Sources")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Confines the one request-path provider exception: widening this allow-list must be a deliberate change here.
    [Fact]
    public void OnlyVerificationSlice_DependsOn_TickerVerifier()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .HaveDependencyOn("Skarbiec.MarketData.Sources.Verification.ITickerVerifier")
            .Should()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Sources.Verification")
            .Or()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Features.AddCustomInstrument")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
