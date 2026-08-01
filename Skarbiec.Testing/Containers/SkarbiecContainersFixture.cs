using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Skarbiec.Testing.Containers;

/// <summary>
/// Starts one PostgreSQL and one RabbitMQ container for an entire xUnit collection, so every test
/// class in a service's test project shares them instead of paying container startup cost per class.
/// Register as a collection fixture (see <see cref="TestingDefaults.CollectionName"/>).
/// </summary>
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

    /// <summary>
    /// Deletes every row from every table (except EF's own migration history) so each test starts
    /// from a clean, already-migrated schema. Call from the test class's own <see cref="IAsyncLifetime"/>
    /// (not this fixture's) so the reset runs before each individual test, not once per collection.
    /// </summary>
    /// <remarks>
    /// The <see cref="Respawner"/> is rebuilt on every call rather than cached: the schema doesn't
    /// exist yet when the very first test in the collection resets (migrations run lazily, the first
    /// time a service's <c>WebApplicationFactory</c> boots its host) — a cached snapshot taken that
    /// early would permanently miss every table.
    /// </remarks>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // A service whose migrations create no business tables (T0.13 skeletons) still gets
        // __EFMigrationsHistory from Migrate() — nothing left to reset once that's ignored.
        // Respawner.CreateAsync throws on zero non-ignored tables, so skip it rather than treat an
        // empty schema as a configuration error.
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

        // Retried on deadlock (Postgres 40P01): once a service has an EF outbox (T1.5), its
        // WebApplicationFactory host runs a live MassTransit bus + outbox delivery poller as a
        // background hosted service, which isn't always fully drained by the *previous* test
        // class's WebApplicationFactory.DisposeAsync shutdown window (see
        // Skarbiec.Identity's UserRegisteredLoggingConsumer for the same caveat) — so this DELETE
        // batch can occasionally lock-clash with a still-shutting-down poller touching
        // OutboxMessage/OutboxState. Postgres's own recommendation for 40P01 is exactly this: retry
        // the losing transaction.
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
