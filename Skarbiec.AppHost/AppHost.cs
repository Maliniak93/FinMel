var builder = DistributedApplication.CreateBuilder(args);

// Fixed, non-generated local-dev credentials (not a real secret — nothing outside this machine's
// Docker network can reach these containers). Postgres/RabbitMQ bake their admin password into
// the persisted data volume the first time they initialize and never change it afterwards; a
// *generated* password (Aspire's own default) only stays valid as long as it's faithfully
// reloaded from user-secrets on every single run. Any run that skips that (e.g. missing
// ASPNETCORE_ENVIRONMENT=Development, a different launch profile, CI) silently regenerates it,
// permanently breaking auth against the already-initialized volume — which is exactly what
// happened locally and showed up as "Unhealthy" in the dashboard. A fixed value can't drift.
var postgresPassword = builder.AddParameter("postgres-password", "skarbiec-local-postgres", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "skarbiec-local-rabbitmq", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume();

var rabbitmq = builder.AddRabbitMQ("rabbitmq", password: rabbitmqPassword)
    .WithDataVolume()
    .WithManagementPlugin();

var identityDb = await AddServiceDatabase("identity", "identity_db");
var portfolioDb = await AddServiceDatabase("portfolio", "portfolio_db");
var marketDataDb = await AddServiceDatabase("marketdata", "marketdata_db");
var strategyDb = await AddServiceDatabase("strategy", "strategy_db");
var reportingDb = await AddServiceDatabase("reporting", "reporting_db");

// Proves the F5 experience (AppHost + ServiceDefaults + dashboard tracing) end-to-end (T0.3/T0.4)
// until the real services land (Identity in T0.5, the rest in T0.13).
builder.AddProject<Projects.Skarbiec_ServiceDefaults_Sample>("sample");

builder.Build().Run();

// Provisions a dedicated Postgres role per service, scoped to only its own database (ADR-003):
// the role owns the database it's granted, and PUBLIC's default CONNECT privilege is revoked so
// no other service's user can even open a connection to it (verified manually, see deploy/README.md).
async Task<IResourceBuilder<PostgresDatabaseResource>> AddServiceDatabase(string serviceName, string databaseName)
{
    var dbUser = $"{serviceName}_user";
    var dbPassword = builder.AddParameter($"{serviceName}-db-password", $"skarbiec-local-{serviceName}", secret: true);
    var dbPasswordValue = await dbPassword.Resource.GetValueAsync(CancellationToken.None);

    return postgres.AddDatabase(serviceName, databaseName)
        .WithCreationScript($"""
            CREATE DATABASE "{databaseName}";
            CREATE USER "{dbUser}" WITH PASSWORD '{dbPasswordValue}';
            REVOKE CONNECT ON DATABASE "{databaseName}" FROM PUBLIC;
            GRANT CONNECT, TEMP ON DATABASE "{databaseName}" TO "{dbUser}";
            ALTER DATABASE "{databaseName}" OWNER TO "{dbUser}";
            """);
}
