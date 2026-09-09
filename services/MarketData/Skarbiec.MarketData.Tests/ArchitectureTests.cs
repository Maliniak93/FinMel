using NetArchTest.Rules;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Architecture guardrails (T0.14, ADR-006). No Testcontainers needed — pure reflection over the
/// compiled assembly.
/// </summary>
/// <remarks>
/// Decision: unlike Portfolio/Strategy/Reporting, MarketData has no "<c>Data</c> entities must
/// implement <c>IUserOwned</c>" rule here — Instrument/PriceQuote/FxRate are global reference data
/// shared across users, not user-owned (ADR-006 scope note in T0.13); MarketData has no tenancy
/// query filter at all. Custom per-user instruments are revisited in Phase 2.
/// </remarks>
public sealed class ArchitectureTests
{
    /// <summary>
    /// UserId always comes from the JWT (<c>ICurrentUser</c>), never from caller-supplied input
    /// (ADR-006) — a request record binding it would let one user stamp another user's id.
    /// </summary>
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

    /// <summary>
    /// External price APIs are called only from <see cref="Skarbiec.MarketData.Sources.PriceSyncJob"/>
    /// (ADR-007) — everything that depends on <c>IPriceSource</c>/<c>IFxRateSource</c> (the sole
    /// gateway to those APIs, per T2.2) lives in the <c>Sources</c> namespace alongside the job, never
    /// in a request-path <c>Features/*</c> handler.
    /// </summary>
    [Fact]
    public void OnlySourcesNamespace_DependsOn_PriceSourceAbstractions()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .HaveDependencyOnAny("Skarbiec.MarketData.Sources.IPriceSource", "Skarbiec.MarketData.Sources.IFxRateSource")
            .Should()
            .ResideInNamespaceStartingWith("Skarbiec.MarketData.Sources")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// ADR-018's other guard rail (M1.6). <c>ITickerVerifier</c> lives in a <c>Sources.*</c> namespace
    /// (<c>Sources.Verification</c>), so it already satisfies
    /// <see cref="OnlySourcesNamespace_DependsOn_PriceSourceAbstractions"/> above — but nothing about
    /// that test stops an unrelated <c>Features/*</c> handler from also injecting it and using
    /// MarketData's one request-path provider exception for something other than "verify a ticker
    /// before creating an instrument" (ADR-018: "never for valuation, never to persist a quote"). This
    /// test is the actual containment: only the verification slice itself (<c>Sources.Verification</c>)
    /// and the one feature that legitimately consumes it (<c>Features.AddCustomInstrument</c>) may
    /// depend on <c>ITickerVerifier</c> at all — widening that allow-list is a deliberate, visible
    /// change to this test, not something a new handler can do by accident.
    /// </summary>
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
