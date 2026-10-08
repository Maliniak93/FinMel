using Npgsql;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Tests;

public sealed class SkarbiecContainersFixtureTests
{
    [Fact]
    public async Task Each_fixture_gets_its_own_database_and_vhost()
    {
        await using var first = await InitializeAsync();
        await using var second = await InitializeAsync();

        var firstDb = new NpgsqlConnectionStringBuilder(first.PostgresConnectionString);
        var secondDb = new NpgsqlConnectionStringBuilder(second.PostgresConnectionString);
        Assert.NotEqual(firstDb.Database, secondDb.Database);
        Assert.Equal(firstDb.Host, secondDb.Host);
        Assert.Equal(firstDb.Port, secondDb.Port);

        var firstMq = new Uri(first.RabbitMqConnectionString);
        var secondMq = new Uri(second.RabbitMqConnectionString);
        Assert.NotEqual(firstMq.AbsolutePath, secondMq.AbsolutePath);
        Assert.Equal(firstMq.Host, secondMq.Host);
        Assert.Equal(firstMq.Port, secondMq.Port);
    }

    [Fact]
    public async Task Reset_clears_only_its_own_database()
    {
        await using var first = await InitializeAsync();
        await using var second = await InitializeAsync();
        await ExecuteAsync(first, "CREATE TABLE things (id int); INSERT INTO things VALUES (1);");
        await ExecuteAsync(second, "CREATE TABLE things (id int); INSERT INTO things VALUES (1);");

        await first.ResetDatabaseAsync();

        Assert.Equal(0, await CountAsync(first));
        Assert.Equal(1, await CountAsync(second));
    }

    [Fact]
    public async Task Reset_after_migration_clears_rows_on_every_call()
    {
        await using var fixture = await InitializeAsync();
        await ExecuteAsync(fixture, "CREATE TABLE things (id int); INSERT INTO things VALUES (1);");

        await fixture.ResetDatabaseAsync();
        Assert.Equal(0, await CountAsync(fixture));

        await ExecuteAsync(fixture, "INSERT INTO things VALUES (2);");
        await fixture.ResetDatabaseAsync();
        Assert.Equal(0, await CountAsync(fixture));
    }

    [Fact]
    public async Task Reset_without_tables_is_a_no_op()
    {
        await using var fixture = await InitializeAsync();
        await ExecuteAsync(fixture, """CREATE TABLE "__EFMigrationsHistory" (id int); INSERT INTO "__EFMigrationsHistory" VALUES (1);""");

        await fixture.ResetDatabaseAsync();

        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""SELECT count(*) FROM "__EFMigrationsHistory" """, connection);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    private static async Task<SkarbiecContainersFixture> InitializeAsync()
    {
        var fixture = new SkarbiecContainersFixture();
        await fixture.InitializeAsync();

        return fixture;
    }

    private static async Task ExecuteAsync(SkarbiecContainersFixture fixture, string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(SkarbiecContainersFixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM things", connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
