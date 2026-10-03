var builder = DistributedApplication.CreateBuilder(args);

// Fixed local-dev passwords: Postgres and RabbitMQ bake the first one into their volume, and a regenerated one would break auth.
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
var reportingDb = await AddServiceDatabase("reporting", "reporting_db");

var identityService = builder.AddProject<Projects.Skarbiec_Identity>("identity-service")
    .WithReference(identityDb.ConnectionString)
    .WaitFor(identityDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

var marketDataService = builder.AddProject<Projects.Skarbiec_MarketData>("marketdata-service")
    .WithReference(marketDataDb.ConnectionString)
    .WaitFor(marketDataDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

// WithReference injects the service-discovery config the MarketData typed client resolves against.
var portfolioService = builder.AddProject<Projects.Skarbiec_Portfolio>("portfolio-service")
    .WithReference(portfolioDb.ConnectionString)
    .WaitFor(portfolioDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithReference(marketDataService)
    .WaitFor(marketDataService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

// No reference to portfolio-service: Reporting values from its own Position read model.
var reportingService = builder.AddProject<Projects.Skarbiec_Reporting>("reporting-service")
    .WithReference(reportingDb.ConnectionString)
    .WaitFor(reportingDb.Database)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithReference(marketDataService)
    .WaitFor(marketDataService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

// WithReference injects the Services:<name> config YARP's service-discovery resolver reads.
builder.AddProject<Projects.Skarbiec_Gateway>("gateway")
    .WithReference(identityService)
    .WaitFor(identityService)
    .WithReference(portfolioService)
    .WaitFor(portfolioService)
    .WithReference(marketDataService)
    .WaitFor(marketDataService)
    .WithReference(reportingService)
    .WaitFor(reportingService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.Build().Run();

// A dedicated role per service owning only its database, with PUBLIC's CONNECT revoked.
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

    // Services never use the admin connection: this one scopes the same password to the restricted role.
    var connectionString = builder.AddConnectionString(
        $"{serviceName}-db",
        ReferenceExpression.Create(
            $"Host={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)};" +
            $"Port={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};" +
            $"Database={databaseName};Username={dbUser};Password={dbPassword}"));

    return new ServiceDatabase(database, connectionString);
}

// Database (admin) is only for WaitFor; ConnectionString (restricted role) is what services reference.
readonly record struct ServiceDatabase(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<IResourceWithConnectionString> ConnectionString);
