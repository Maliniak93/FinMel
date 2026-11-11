using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Skarbiec.Portfolio.Data;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class MigrationTestDatabase
{
    public static async Task<PortfolioDbContext> CreateAtMigrationAsync(
        SkarbiecContainersFixture containers, string migrationId, CancellationToken cancellationToken)
    {
        var name = $"migration_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(containers.PostgresConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(containers.PostgresConnectionString) { Database = name, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<PortfolioDbContext>().UseNpgsql(connectionString).Options;
        var db = new PortfolioDbContext(options, new StubCurrentUser(Guid.NewGuid()));
        await db.Database.GetService<IMigrator>().MigrateAsync(migrationId, cancellationToken);

        return db;
    }

    public static async Task<Guid> InsertAccountAsync(
        PortfolioDbContext db, Guid userId, string name, string wrapper, bool archived, CancellationToken cancellationToken, string type = "Brokerage")
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Accounts" ("Id", "UserId", "Name", "Type", "Wrapper", "Currency", "IsArchived") VALUES ({id}, {userId}, {name}, {type}, {wrapper}, 'PLN', {archived})""",
            cancellationToken);

        return id;
    }
}
