namespace Skarbiec.Testing.Containers;

/// <summary>
/// Base for a service's HTTP slice tests: owns the per-class <see cref="SkarbiecApiFactory{TProgram}"/>
/// lifetime so no test class repeats the <c>InitializeAsync</c>/<c>DisposeAsync</c> pair. Each test
/// still gets a freshly reset database (see <see cref="SkarbiecApiFactory{TProgram}.ResetDatabaseAsync"/>)
/// while the containers stay shared across the collection.
/// </summary>
/// <remarks>
/// Services don't inherit this directly — each test project declares one service-level base
/// supplying <see cref="Factory"/> (e.g. <c>PortfolioEndpointTests</c>), so a test class only
/// carries <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts.
/// Override <see cref="InitializeAsync"/> (calling <c>base</c>) to seed shared state per test.
/// </remarks>
public abstract class ServiceEndpointTests<TProgram> : IAsyncLifetime where TProgram : class
{
    /// <summary>The test host under test. Kept as a property so the service-level base can construct it from the collection fixture.</summary>
    protected abstract SkarbiecApiFactory<TProgram> Factory { get; }

    public virtual ValueTask InitializeAsync() => new(Factory.ResetDatabaseAsync());

    public virtual ValueTask DisposeAsync() => Factory.DisposeAsync();
}
