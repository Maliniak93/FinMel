using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.MigrationTestDatabase;

namespace Skarbiec.Portfolio.Tests;

public sealed class OneOffIncomePlannedMigrationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string MigrationBeforePlanned = "20261111041804_GroupContributions";

    [Fact]
    public async Task ExistingIncomesArePlanned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using var db = await CreateAtMigrationAsync(_containers, MigrationBeforePlanned, cancellationToken);
        var account = await InsertAccountAsync(db, userId, "mBank ROR", "Regular", false, cancellationToken, "Bank");
        var settledId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 30);
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "OneOffIncomes" ("Id", "UserId", "Name", "Amount", "AccountId", "Date", "ActualAccountId", "ActualAmount", "ActualDate") VALUES ({settledId}, {userId}, 'Premia', 2500, {account}, {date}, {account}, 2500, {date})""",
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "OneOffIncomes" ("Id", "UserId", "Name", "Amount", "AccountId", "Date") VALUES ({Guid.NewGuid()}, {userId}, 'Zwrot', 1000, {account}, {date})""",
            cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Equal(
            [("Premia", true), ("Zwrot", true)],
            await ReadIncomesAsync(db, cancellationToken));
    }

    private static async Task<List<(string Name, bool Planned)>> ReadIncomesAsync(
        PortfolioDbContext db, CancellationToken cancellationToken)
    {
        var rows = new List<(string, bool)>();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Name", "Planned" FROM "OneOffIncomes" ORDER BY "Name" """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetBoolean(1)));
        }

        return rows;
    }
}
