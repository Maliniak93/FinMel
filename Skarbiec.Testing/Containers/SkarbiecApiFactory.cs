using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Skarbiec.Testing.Containers;

public abstract class SkarbiecApiFactory<TProgram>(SkarbiecContainersFixture containers, string databaseConnectionStringName)
    : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{databaseConnectionStringName}", containers.PostgresConnectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", containers.RabbitMqConnectionString);
        builder.UseSetting(TestingDefaults.DisableBackgroundJobsConfigKey, "true");

        // The EventLog provider's handle dies with the first disposed host, then throws out of later hosts' Logger.Log on shutdown.
        builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
    }

    // Boots the host first: Respawn needs the migrated schema, which does not exist before the first host starts.
    public async Task ResetDatabaseAsync()
    {
        _ = Services;
        await containers.ResetDatabaseAsync();
    }
}
