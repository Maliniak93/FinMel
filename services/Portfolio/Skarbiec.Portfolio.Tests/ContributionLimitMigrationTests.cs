using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class ContributionLimitMigrationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string MigrationBeforeGlobalLimits = "20261107033116_RecurringExpensePaymentSettlement";

    [Fact]
    public async Task MovesPerAccountLimitsToPerKind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var db = await CreateDatabaseAtPreviousMigrationAsync(cancellationToken);
        var ikeSmall = await InsertAccountAsync(db, userId, "IKE A", "Ike", false, cancellationToken);
        var ikeArchived = await InsertAccountAsync(db, userId, "IKE B", "Ike", true, cancellationToken);
        var ikze = await InsertAccountAsync(db, userId, "IKZE", "Ikze", false, cancellationToken);
        var otherIke = await InsertAccountAsync(db, otherUserId, "IKE C", "Ike", false, cancellationToken);
        await InsertLimitAsync(db, userId, ikeSmall, 2026, 25_000m, cancellationToken);
        await InsertLimitAsync(db, userId, ikeArchived, 2026, 28_260m, cancellationToken);
        await InsertLimitAsync(db, userId, ikze, 2026, 11_304m, cancellationToken);
        await InsertLimitAsync(db, userId, ikeSmall, 2025, 26_019m, cancellationToken);
        await InsertLimitAsync(db, otherUserId, otherIke, 2026, 5_000m, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var rows = await ReadLimitsAsync(db, cancellationToken);
        Assert.Equal(
            [
                (userId, "Ike", 2025, 26_019m),
                (otherUserId, "Ike", 2026, 5_000m),
                (userId, "Ike", 2026, 28_260m),
                (userId, "Ikze", 2026, 11_304m)
            ],
            rows);
    }

    [Fact]
    public async Task DropsLimitWithoutAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using var db = await CreateDatabaseAtPreviousMigrationAsync(cancellationToken);
        var ike = await InsertAccountAsync(db, userId, "IKE", "Ike", false, cancellationToken);
        await InsertLimitAsync(db, userId, ike, 2026, 28_260m, cancellationToken);
        await InsertLimitAsync(db, userId, Guid.NewGuid(), 2026, 99_999m, cancellationToken);
        await InsertLimitAsync(db, userId, Guid.NewGuid(), 2024, 77_777m, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Equal([(userId, "Ike", 2026, 28_260m)], await ReadLimitsAsync(db, cancellationToken));
    }

    private async Task<PortfolioDbContext> CreateDatabaseAtPreviousMigrationAsync(CancellationToken cancellationToken)
    {
        var name = $"contributionlimits_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_containers.PostgresConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_containers.PostgresConnectionString) { Database = name, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<PortfolioDbContext>().UseNpgsql(connectionString).Options;
        var db = new PortfolioDbContext(options, new StubCurrentUser(Guid.NewGuid()));
        await db.Database.GetService<IMigrator>().MigrateAsync(MigrationBeforeGlobalLimits, cancellationToken);

        return db;
    }

    private static async Task<Guid> InsertAccountAsync(
        PortfolioDbContext db, Guid userId, string name, string wrapper, bool archived, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Accounts" ("Id", "UserId", "Name", "Type", "Wrapper", "Currency", "IsArchived") VALUES ({id}, {userId}, {name}, 'Brokerage', {wrapper}, 'PLN', {archived})""",
            cancellationToken);

        return id;
    }

    private static async Task InsertLimitAsync(
        PortfolioDbContext db, Guid userId, Guid accountId, int year, decimal amount, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "AccountContributionLimits" ("Id", "UserId", "AccountId", "Year", "Amount") VALUES ({Guid.NewGuid()}, {userId}, {accountId}, {year}, {amount})""",
            cancellationToken);

    private static async Task<List<(Guid UserId, string Wrapper, int Year, decimal Amount)>> ReadLimitsAsync(
        PortfolioDbContext db, CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, string, int, decimal)>();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "UserId", "Wrapper", "Year", "Amount" FROM "ContributionLimits" ORDER BY "Wrapper", "Year", "Amount" """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetDecimal(3)));
        }

        return rows;
    }
}
