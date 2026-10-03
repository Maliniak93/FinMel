using Npgsql;
using Quartz;

namespace Skarbiec.MarketData.Sources;

// Quartz owns its schema: ProvisionSchema creates the qrtz_* tables, and the EF model never contains them.
public static class QuartzStore
{
    public const string SchemaName = "quartz";

    public const string QuartzTablePrefix = SchemaName + ".qrtz_";

    // Quartz creates the tables, not the Postgres schema, which must exist before the scheduler starts.
    public static IQuartzBuilder UseMarketDataStore(
        this IQuartzBuilder quartz,
        string connectionString,
        Action<ClusteringOptions>? configureClustering = null)
    {
        // Unique per instance, as clustering requires.
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

    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS {SchemaName}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static IServiceCollection AddMarketDataScheduler(
        this IServiceCollection services,
        string connectionString,
        Action<IQuartzBuilder>? configure = null)
    {
        // Before AddQuartzHostedService: hosted services start in registration order, so the schema exists before Quartz touches it.
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
