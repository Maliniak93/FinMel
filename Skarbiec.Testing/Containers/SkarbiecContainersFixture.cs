using Npgsql;
using Respawn;

namespace Skarbiec.Testing.Containers;

public sealed class SkarbiecContainersFixture : IAsyncLifetime
{
    private Respawner? _respawner;

    public string PostgresConnectionString { get; private set; } = null!;

    public string RabbitMqConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var containers = SkarbiecContainers.Current;
        PostgresConnectionString = await containers.CreateDatabaseAsync();
        RabbitMqConnectionString = await containers.CreateVirtualHostAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // Cached only once tables exist: a Respawner built before the first migration would miss every table.
        if (_respawner is null)
        {
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

            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = ["__EFMigrationsHistory"]
            });
        }

        // Retried on deadlock (40P01): a previous host's outbox poller may still be shutting down.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _respawner.ResetAsync(connection);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }
}
