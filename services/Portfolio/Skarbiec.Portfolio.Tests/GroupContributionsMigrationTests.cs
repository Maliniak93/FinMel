using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupContributionsMigrationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string MigrationBeforeGroupContributions = "20261107195656_GlobalContributionLimits";

    [Fact]
    public async Task MovesMonthlyAmountToOneContributionWithoutAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = await CreateDatabaseAtPreviousMigrationAsync(cancellationToken);
        var groupId = await InsertGroupAsync(db, "Emerytura", "blue", 1_500.50m, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var contributions = await ReadContributionsAsync(db, groupId, cancellationToken);
        var single = Assert.Single(contributions);
        Assert.Equal((1_500.50m, null), single);
    }

    [Fact]
    public async Task ZeroAmountBecomesNoContribution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = await CreateDatabaseAtPreviousMigrationAsync(cancellationToken);
        var groupId = await InsertGroupAsync(db, "Dom", "green", 0m, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Empty(await ReadContributionsAsync(db, groupId, cancellationToken));
    }

    private async Task<PortfolioDbContext> CreateDatabaseAtPreviousMigrationAsync(CancellationToken cancellationToken)
    {
        var name = $"groupcontributions_{Guid.NewGuid():N}";
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
        await db.Database.GetService<IMigrator>().MigrateAsync(MigrationBeforeGroupContributions, cancellationToken);

        return db;
    }

    private static async Task<Guid> InsertGroupAsync(
        PortfolioDbContext db, string name, string color, decimal monthly, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "AssetGroups" ("Id", "UserId", "Name", "Color", "Kind", "MonthlyContributionPln", "ClassAllocation") VALUES ({id}, {Guid.NewGuid()}, {name}, {color}, 'Regular', {monthly}, '[]'::jsonb)""",
            cancellationToken);

        return id;
    }

    private static async Task<List<(decimal AmountPln, Guid? AccountId)>> ReadContributionsAsync(
        PortfolioDbContext db, Guid groupId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Contributions"::text FROM "AssetGroups" WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", groupId);
        var json = (string)(await command.ExecuteScalarAsync(cancellationToken))!;

        return JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(e => (
                e.GetProperty("AmountPln").GetDecimal(),
                e.GetProperty("AccountId") is { ValueKind: not JsonValueKind.Null } id ? (Guid?)id.GetGuid() : null))
            .ToList();
    }
}
