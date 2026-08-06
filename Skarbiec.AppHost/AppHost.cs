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

// First real service (T0.5); the rest land in T0.13.
var identityService = builder.AddProject<Projects.Skarbiec_Identity>("identity-service")
    .WithReference(identityDb.ConnectionString)
    .WaitFor(identityDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

var marketDataService = builder.AddProject<Projects.Skarbiec_MarketData>("marketdata-service")
    .WithReference(marketDataDb.ConnectionString)
    .WaitFor(marketDataDb.Database)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

// T1.5: publishes AssetChanged/TransactionRecorded through the outbox — RabbitMQ reference lands
// alongside this, its first published event. T2.9: AddAsset/UpdateAsset validate a market asset's
// InstrumentId by calling MarketData directly (not through the Gateway) — WithReference here is what
// injects the "Services:marketdata-service:..." config the typed HttpClient's service discovery
// resolves "https+http://marketdata-service" against.
var portfolioService = builder.AddProject<Projects.Skarbiec_Portfolio>("portfolio-service")
    .WithReference(portfolioDb.ConnectionString)
    .WaitFor(portfolioDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithReference(marketDataService)
    .WaitFor(marketDataService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

var strategyService = builder.AddProject<Projects.Skarbiec_Strategy>("strategy-service")
    .WithReference(strategyDb.ConnectionString)
    .WaitFor(strategyDb.Database)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

var reportingService = builder.AddProject<Projects.Skarbiec_Reporting>("reporting-service")
    .WithReference(reportingDb.ConnectionString)
    .WaitFor(reportingDb.Database)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

// T0.15: single entry point for Angular (ADR-013) — routes per prefix, JWT validated at the
// gateway, downstream addresses resolved via Aspire service discovery (WithReference below injects
// the "Services:<name>:..." config YARP's AddServiceDiscoveryDestinationResolver reads).
builder.AddProject<Projects.Skarbiec_Gateway>("gateway")
    .WithReference(identityService)
    .WaitFor(identityService)
    .WithReference(portfolioService)
    .WaitFor(portfolioService)
    .WithReference(marketDataService)
    .WaitFor(marketDataService)
    .WithReference(strategyService)
    .WaitFor(strategyService)
    .WithReference(reportingService)
    .WaitFor(reportingService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.Build().Run();

// Provisions a dedicated Postgres role per service, scoped to only its own database (ADR-003):
// the role owns the database it's granted, and PUBLIC's default CONNECT privilege is revoked so
// no other service's user can even open a connection to it (verified manually, see deploy/README.md).
async Task<ServiceDatabase> AddServiceDatabase(string serviceName, string databaseName)
{
    var dbUser = $"{serviceName}_user";
    var dbPassword = builder.AddParameter($"{serviceName}-db-password", $"skarbiec-local-{serviceName}", secret: true);
    var dbPasswordValue = await dbPassword.Resource.GetValueAsync(CancellationToken.None);

    var database = postgres.AddDatabase(serviceName, databaseName)
        .WithCreationScript($"""
            CREATE DATABASE "{databaseName}";
            CREATE USER "{dbUser}" WITH PASSWORD '{dbPasswordValue}';
            REVOKE CONNECT ON DATABASE "{databaseName}" FROM PUBLIC;
            GRANT CONNECT, TEMP ON DATABASE "{databaseName}" TO "{dbUser}";
            ALTER DATABASE "{databaseName}" OWNER TO "{dbUser}";
            """);

    // `database` above authenticates as the Postgres *admin* user — services must never consume
    // it directly (deploy/README.md). This connection string reuses the same fixed per-service
    // password but scopes to the restricted role the creation script just provisioned.
    var connectionString = builder.AddConnectionString(
        $"{serviceName}-db",
        ReferenceExpression.Create(
            $"Host={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)};" +
            $"Port={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};" +
            $"Database={databaseName};Username={dbUser};Password={dbPassword}"));

    return new ServiceDatabase(database, connectionString);
}

// `Database` (admin-authenticated) is only for `WaitFor` — it exists once the creation script has
// run. `ConnectionString` (restricted role) is what services pass to `WithReference`.
readonly record struct ServiceDatabase(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<IResourceWithConnectionString> ConnectionString);
