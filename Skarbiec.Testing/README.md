# Skarbiec.Testing

Shared slice-test foundation (T0.9): real PostgreSQL + RabbitMQ via Testcontainers, a
`WebApplicationFactory` base class wired to them, and a JWT helper for tenancy tests.

## Wiring a service's test project into it

1. Reference this project from `services/<Name>/Skarbiec.<Name>.Tests`.
2. Add a one-line collection definition (xUnit only discovers `[CollectionDefinition]` classes
   declared in the assembly under test, so each test project needs its own):

   ```csharp
   [CollectionDefinition(TestingDefaults.CollectionName)]
   public sealed class ContainersCollection : ICollectionFixture<SkarbiecContainersFixture>;
   ```

3. Subclass `SkarbiecApiFactory<TProgram>` with the service's own DB connection-string name:

   ```csharp
   public sealed class MyServiceApiFactory(SkarbiecContainersFixture containers)
       : SkarbiecApiFactory<Program>(containers, "myservice-db");
   ```

4. In each test class: apply `[Collection(TestingDefaults.CollectionName)]`, take
   `SkarbiecContainersFixture` in the constructor, create the factory from it, and implement
   `IAsyncLifetime` to reset the database before every test (xUnit creates a fresh test-class
   instance per `[Fact]`, so this runs before each one — not once per class). Reset through the
   *factory* (`_factory.ResetDatabaseAsync()`), not the containers fixture directly — the factory
   boots its host first (applying migrations), which the raw fixture method can't do, and Respawn
   needs the schema to already exist:

   ```csharp
   [Collection(TestingDefaults.CollectionName)]
   public sealed class SomeEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
   {
       private readonly MyServiceApiFactory _factory = new(containers);

       public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());
       public ValueTask DisposeAsync() => _factory.DisposeAsync();

       [Fact]
       public async Task Some_Test() { /* use _factory.CreateClient() */ }
   }
   ```

5. For tenancy tests (T0.14), mint a token for an arbitrary user without registering/logging in:

   ```csharp
   var tokenForUserB = _factory.IssueAccessToken(userB);
   ```

## Why reset per test class instance, not per collection

The containers (and their migrated schema) are shared for the whole assembly run, but the *data*
must not leak between tests — two facts that both register the same fixed e-mail address must both
succeed. Resetting in the test class's own `IAsyncLifetime.InitializeAsync` achieves that because
xUnit constructs a new test class instance per `[Fact]`.

## Background jobs

`SkarbiecApiFactory<TProgram>` sets `Testing:DisableBackgroundJobs = "true"` on every test host.
Services that register scheduled work (e.g. MarketData's Quartz jobs, Phase 2) should check
`TestingDefaults.DisableBackgroundJobsConfigKey` before scheduling it, so slice tests never race a
background job touching the same data.
