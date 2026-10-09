using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Skarbiec.Testing.Containers;

public sealed class SkarbiecContainers : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithCommand("-c", "max_connections=300")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    // The class fixture keeps a parameterless constructor, so it reaches the assembly fixture through this.
    internal static SkarbiecContainers Current { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
        Current = this;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public async Task<string> CreateVirtualHostAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await RunRabbitMqCtlAsync("add_vhost", name);
        var uri = new UriBuilder(_rabbitMq.GetConnectionString()) { Path = $"/{name}" };
        await RunRabbitMqCtlAsync("set_permissions", "-p", name, Uri.UnescapeDataString(uri.UserName), ".*", ".*", ".*");

        return uri.Uri.AbsoluteUri;
    }

    private async Task RunRabbitMqCtlAsync(params string[] arguments)
    {
        var result = await _rabbitMq.ExecAsync(["rabbitmqctl", .. arguments]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl {string.Join(' ', arguments)} failed: {result.Stderr}");
        }
    }
}
