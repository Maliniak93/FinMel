using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Skarbiec.Testing.Containers;

public sealed class SkarbiecContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    // Rebuilt on every call: a Respawner cached before the first migration would miss every table.
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // Respawner.CreateAsync throws when only __EFMigrationsHistory exists, so that case is skipped.
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = """
                SELECT count(*) FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
                """;

            if ((long)(await countCommand.ExecuteScalarAsync())! == 0)
            {
                return;
            }
        }

        var respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });

        // Retried on deadlock (40P01): a previous host's outbox poller may still be shutting down.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await respawner.ResetAsync(connection);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }
}
