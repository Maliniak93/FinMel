using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Skarbiec.Testing.Containers;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> for a service's slice tests: points the
/// service at the shared containers from <see cref="SkarbiecContainersFixture"/> instead of a
/// per-class container, and disables background jobs by default (see
/// <see cref="TestingDefaults.DisableBackgroundJobsConfigKey"/>).
/// </summary>
/// <param name="containers">The collection-shared containers fixture.</param>
/// <param name="databaseConnectionStringName">
/// The service's own connection string name (e.g. <c>"identity-db"</c>) — matches the name Aspire
/// registers via <c>WithReference</c> in the AppHost, and what the service resolves through
/// <c>builder.AddNpgsqlDbContext&lt;TContext&gt;(name)</c>.
/// </param>
public abstract class SkarbiecApiFactory<TProgram>(SkarbiecContainersFixture containers, string databaseConnectionStringName)
    : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{databaseConnectionStringName}", containers.PostgresConnectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", containers.RabbitMqConnectionString);
        builder.UseSetting(TestingDefaults.DisableBackgroundJobsConfigKey, "true");

        // WebApplication.CreateBuilder registers the Windows EventLog provider by default, and it is
        // the one provider that ignores the general log levels — it always writes Warning and above.
        // A test run builds and disposes one host per fact in a single process, and the native
        // EventLog handle behind that provider does not survive the first host's disposal: every
        // later host that logs a warning gets ObjectDisposedException('EventLogInternal') thrown
        // back out of Logger.Log. It surfaces during shutdown (MassTransit logs a warning when a bus
        // stop runs past its timeout on a loaded broker), so the exception escapes
        // WebApplicationFactory.DisposeAsync and fails whichever test happened to own that host —
        // never the one that caused it. Tests have no business writing to the machine's event log
        // anyway; silencing the provider by its alias keeps Console/Debug output intact.
        builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
    }

    /// <summary>
    /// Boots this host if it hasn't already (applying the service's migrations, same as
    /// production start-up in Development) and then resets the shared database. Call from the
    /// test class's own <c>IAsyncLifetime.InitializeAsync</c> instead of calling
    /// <see cref="SkarbiecContainersFixture.ResetDatabaseAsync"/> directly — Respawn needs the
    /// schema to exist, and on the very first test in the collection nothing has migrated yet.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        _ = Services;
        await containers.ResetDatabaseAsync();
    }
}
