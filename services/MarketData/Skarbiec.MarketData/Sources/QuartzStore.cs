using Npgsql;
using Quartz;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// The one place MarketData's Quartz job store is configured — production (<see cref="PriceSyncJobExtensions"/>)
/// and the scheduling tests go through it, so the tests exercise the store production runs on.
/// Quartz owns its schema: <c>ProvisionSchema()</c> creates the <c>quartz.qrtz_*</c> tables at
/// scheduler start and validates them on every later start. The EF model never contains them.
/// </summary>
public static class QuartzStore
{
    /// <summary>Postgres schema holding Quartz's tables, kept apart from the EF-owned ones.</summary>
    public const string SchemaName = "quartz";

    /// <summary>Schema-qualified table prefix — every Quartz table is <c>quartz.qrtz_&lt;name&gt;</c>.</summary>
    public const string QuartzTablePrefix = SchemaName + ".qrtz_";

    /// <summary>
    /// Postgres-backed, clustered, System.Text.Json-serialized store that provisions its own schema.
    /// Works on any <see cref="IQuartzBuilder"/> — the DI one from <c>AddQuartz</c> and a standalone
    /// <see cref="QuartzSchedulerBuilder"/> alike. Quartz does not create the Postgres schema the tables
    /// live in: a DI host gets it from <see cref="AddMarketDataScheduler"/>, a standalone scheduler
    /// calls <see cref="EnsureSchemaAsync"/> first.
    /// </summary>
    public static IQuartzBuilder UseMarketDataStore(
        this IQuartzBuilder quartz,
        string connectionString,
        Action<ClusteringOptions>? configureClustering = null)
    {
        // Unique per instance — required for clustering (the 4.x spelling of SchedulerId = "AUTO").
        quartz.ConfigureScheduler(scheduler => scheduler.GenerateInstanceId = true);

        quartz.UsePersistentStore(store =>
        {
            store.UsePostgres(connectionString);
            store.ConfigureStore(options => options.TablePrefix = QuartzTablePrefix);
            store.UseSystemTextJsonSerializer();
            store.UseClustering(clustering => configureClustering?.Invoke(clustering));
            store.ProvisionSchema();
        });

        return quartz;
    }

    /// <summary>
    /// Creates the <see cref="SchemaName"/> Postgres schema when it is missing. Quartz's Postgres
    /// provisioning creates tables only, so the schema must exist before the scheduler starts.
    /// </summary>
    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS {SchemaName}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Registers the scheduler on <see cref="UseMarketDataStore"/>, its hosted service, and a startup
    /// step that runs <see cref="EnsureSchemaAsync"/> before the hosted service starts the scheduler.
    /// <paramref name="configure"/> adds jobs and triggers on top of the shared store.
    /// </summary>
    public static IServiceCollection AddMarketDataScheduler(
        this IServiceCollection services,
        string connectionString,
        Action<IQuartzBuilder>? configure = null)
    {
        // Registered before AddQuartzHostedService: hosted lifecycle services run StartingAsync in
        // registration order, so the schema exists before Quartz's own hosted service touches the store.
        services.AddHostedService(_ => new QuartzSchemaInitializer(connectionString));

        services.AddQuartz(quartz =>
        {
            quartz.UseMarketDataStore(connectionString);
            configure?.Invoke(quartz);
        });

        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }

    private sealed class QuartzSchemaInitializer(string connectionString) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken) => EnsureSchemaAsync(connectionString, cancellationToken);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
