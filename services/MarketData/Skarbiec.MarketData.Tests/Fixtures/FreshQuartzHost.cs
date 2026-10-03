using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Quartz;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests.Fixtures;

internal static class FreshQuartzHost
{
    public static async Task<string> CreateMigratedDatabaseAsync(SkarbiecContainersFixture containers, CancellationToken cancellationToken)
    {
        var name = $"quartz_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(containers.PostgresConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(containers.PostgresConnectionString) { Database = name, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<MarketDataDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new MarketDataDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    public static async Task<List<string>> QuartzTablesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'quartz' ORDER BY 1";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    // Quartz 4 starts the scheduler only after the application has started, so the status has to be awaited.
    public static async Task<IScheduler> WaitForRunningSchedulerAsync(IHost host, CancellationToken cancellationToken)
    {
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (scheduler.Status != SchedulerStatus.Running && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return scheduler;
    }

    public static IHost BuildSchedulerHost(SkarbiecContainersFixture containers, string marketDataConnectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = marketDataConnectionString;
        builder.Configuration["ConnectionStrings:rabbitmq"] = containers.RabbitMqConnectionString;
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(marketDataConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new NoOpFxRateSource());
        builder.Services.AddHealthChecks();
        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();
        return builder.Build();
    }
}
