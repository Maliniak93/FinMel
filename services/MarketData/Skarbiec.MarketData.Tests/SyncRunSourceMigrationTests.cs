using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class SyncRunSourceMigrationTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string MigrationBeforeSource = "20261013133449_InitialCreate";

    [Fact]
    public async Task ExistingRows_DefaultToSchedule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = $"syncsource_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_containers.PostgresConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_containers.PostgresConnectionString) { Database = name, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<MarketDataDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new MarketDataDbContext(options);
        await db.Database.GetService<IMigrator>().MigrateAsync(MigrationBeforeSource, cancellationToken);

        var runId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "SyncRuns" ("Id", "Kind", "StartedAt", "Status", "SyncedCount", "NoDataCount", "FailedCount") VALUES ({runId}, 'Prices', now(), 'Completed', 1, 0, 0)""",
            cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var run = await db.SyncRuns.AsNoTracking().SingleAsync(r => r.Id == runId, cancellationToken);
        Assert.Equal(SyncRunSource.Schedule, run.Source);
    }
}
