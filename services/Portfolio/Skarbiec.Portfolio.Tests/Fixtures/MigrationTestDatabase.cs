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
        PortfolioDbContext db,
        Guid userId,
        string name,
        string wrapper,
        bool archived,
        CancellationToken cancellationToken,
        string type = "Brokerage",
        Guid? defaultGroupId = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Accounts" ("Id", "UserId", "Name", "Type", "Wrapper", "Currency", "IsArchived", "DefaultGroupId") VALUES ({id}, {userId}, {name}, {type}, {wrapper}, 'PLN', {archived}, {defaultGroupId})""",
            cancellationToken);

        return id;
    }

    // assetClass is the stored int: Cash 0, Stock 2, Etf 3, Bond 4, BankAccount 9.
    public static async Task<Guid> InsertAssetAsync(
        PortfolioDbContext db,
        Guid userId,
        Guid accountId,
        string name,
        int assetClass,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Assets" ("Id", "UserId", "AccountId", "AssetClass", "ValuationMode", "Name", "Currency", "Quantity", "Version", "IsArchived", "GroupId") VALUES ({id}, {userId}, {accountId}, {assetClass}, 0, {name}, 'PLN', 0, 0, false, {groupId})""",
            cancellationToken);

        return id;
    }
}
