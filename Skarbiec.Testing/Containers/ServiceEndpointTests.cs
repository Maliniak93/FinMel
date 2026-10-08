namespace Skarbiec.Testing.Containers;

// Each test project declares one service-level base supplying Factory; override InitializeAsync, calling base, to seed per test.
public abstract class ServiceEndpointTests<TProgram> : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture> where TProgram : class
{
    protected abstract SkarbiecApiFactory<TProgram> Factory { get; }

    public virtual ValueTask InitializeAsync() => new(Factory.ResetDatabaseAsync());

    public virtual ValueTask DisposeAsync() => Factory.DisposeAsync();
}
