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
            .Or()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Features.GetFxRate")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void OnlyGetFxRateSlice_DependsOn_FxRateSource_InAFeature()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Features")
            .And()
            .DoNotResideInNamespaceStartingWith("Skarbiec.MarketData.Features.GetFxRate")
            .ShouldNot()
            .HaveDependencyOn("Skarbiec.MarketData.Sources.IFxRateSource")
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

    [Fact]
    public void OnlySearchSlice_DependsOn_InstrumentSearchSource()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .HaveDependencyOn("Skarbiec.MarketData.Sources.IInstrumentSearchSource")
            .Should()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Sources")
            .Or()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Features.SearchInstruments")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
